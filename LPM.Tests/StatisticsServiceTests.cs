using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Tests for <see cref="StatisticsService"/> – weekly statistics calculations including
/// per-day auditor breakdowns, PC counts, academy visits, and weekly summaries.
/// Each test uses a fresh isolated SQLite database.
/// </summary>
public class StatisticsServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly StatisticsService _svc;

    public StatisticsServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new StatisticsService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // GetWeekDayStats – basic
    // =========================================================================

    [Fact]
    public void GetWeekDayStats_Returns7DayResults_AlwaysExactly()
    {
        var week   = new DateOnly(2024, 1, 11);  // Thursday
        var result = _svc.GetWeekDayStats(week);
        Assert.Equal(7, result.Count);
    }

    [Fact]
    public void GetWeekDayStats_EmptyDb_AllDaysHaveZeroCounts()
    {
        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeekDayStats(week);

        foreach (var day in result)
        {
            Assert.Empty(day.Staff);
            Assert.Equal(0, day.AcademyCount);
            Assert.Equal(0, day.BodyInShop);
            Assert.Equal(0, day.PcCount);
        }
    }

    [Fact]
    public void GetWeekDayStats_DayDates_MatchWeekStartPlusDays()
    {
        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeekDayStats(week);

        for (int i = 0; i < 7; i++)
            Assert.Equal(week.AddDays(i), result[i].Date);
    }

    // =========================================================================
    // GetWeekDayStats – session data
    // =========================================================================

    [Fact]
    public void GetWeekDayStats_ShowsAuditorWork_ForCorrectDay()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Tami", "Cohen");
        TestDbHelper.InsertAuditor(conn, audId, type: 1, isActive: true);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        // Session on Thursday (day 0) = 3600 sec
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600, adminSec: 600);

        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeekDayStats(week);

        var thu = result[0];
        Assert.Single(thu.Staff);
        var tamiRow = thu.Staff[0];
        Assert.Contains("Tami", tamiRow.Name);
        // LengthSeconds + AdminSeconds = 3600 + 600 = 4200
        Assert.Equal(4200, tamiRow.AuditSec);
        Assert.Equal(4200, tamiRow.TotalSec);
    }

    [Fact]
    public void GetWeekDayStats_AuditorOnDifferentDays_AppearsOnCorrectDays()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Genia");
        TestDbHelper.InsertAuditor(conn, audId, type: 1, isActive: true);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        // Thursday (day 0) and Monday (day 4)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600); // Thu
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-15", 1800); // Mon

        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeekDayStats(week);

        Assert.Single(result[0].Staff);  // Thursday
        Assert.Empty(result[1].Staff);   // Friday
        Assert.Empty(result[2].Staff);   // Saturday
        Assert.Empty(result[3].Staff);   // Sunday
        Assert.Single(result[4].Staff);  // Monday
    }

    [Fact]
    public void GetWeekDayStats_MultipleAuditors_AllAppearOnSameDay()
    {
        using var conn = Open();
        var aud1 = TestDbHelper.InsertPerson(conn, "Tami");
        var aud2 = TestDbHelper.InsertPerson(conn, "Genia");
        TestDbHelper.InsertAuditor(conn, aud1, type: 1, isActive: true);
        TestDbHelper.InsertAuditor(conn, aud2, type: 1, isActive: true);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        // Both work on Thursday
        TestDbHelper.InsertSession(conn, pcId, aud1, "2024-01-11", 3600, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, aud2, "2024-01-11", 2700, seqInDay: 2);

        var result = _svc.GetWeekDayStats(new DateOnly(2024, 1, 11));
        Assert.Equal(2, result[0].Staff.Count);
    }

    [Fact]
    public void GetWeekDayStats_StaffOrderedByTotalSecDesc()
    {
        using var conn = Open();
        var aud1 = TestDbHelper.InsertPerson(conn, "Low");
        var aud2 = TestDbHelper.InsertPerson(conn, "High");
        TestDbHelper.InsertAuditor(conn, aud1, type: 1, isActive: true);
        TestDbHelper.InsertAuditor(conn, aud2, type: 1, isActive: true);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        TestDbHelper.InsertSession(conn, pcId, aud1, "2024-01-11", 1800, seqInDay: 1); // Less
        TestDbHelper.InsertSession(conn, pcId, aud2, "2024-01-11", 7200, seqInDay: 2); // More

        var staff = _svc.GetWeekDayStats(new DateOnly(2024, 1, 11))[0].Staff;
        Assert.Equal("High", staff[0].Name);  // More seconds first
        Assert.Equal("Low",  staff[1].Name);
    }

    // =========================================================================
    // GetWeekDayStats – PC count
    // =========================================================================

    [Fact]
    public void GetWeekDayStats_PcCount_CountsDistinctPcsPerDay()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId, type: 1, isActive: true);
        var pc1   = TestDbHelper.InsertPerson(conn, "Client1");
        var pc2   = TestDbHelper.InsertPerson(conn, "Client2");
        TestDbHelper.InsertPC(conn, pc1);
        TestDbHelper.InsertPC(conn, pc2);

        // Both PCs have sessions on Thursday
        TestDbHelper.InsertSession(conn, pc1, audId, "2024-01-11", 3600, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pc2, audId, "2024-01-11", 1800, seqInDay: 2);

        var result = _svc.GetWeekDayStats(new DateOnly(2024, 1, 11));
        Assert.Equal(2, result[0].PcCount);
    }

    [Fact]
    public void GetWeekDayStats_PcCount_SamePcMultipleSessions_CountedOnce()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId, type: 1, isActive: true);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        // Same PC, two sessions on Thursday
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 1800, seqInDay: 2);

        var result = _svc.GetWeekDayStats(new DateOnly(2024, 1, 11));
        Assert.Equal(1, result[0].PcCount);
    }

    // =========================================================================
    // GetWeekDayStats – Solo CS
    // =========================================================================

    [Fact]
    public void GetWeekDayStats_SoloCsSeconds_AppearsInSoloCsSec_Field()
    {
        using var conn = Open();
        // Solo-type auditor (type=3) does a CS review on a solo session
        var soloAudId = TestDbHelper.InsertPerson(conn, "Aviv");
        TestDbHelper.InsertAuditor(conn, soloAudId, type: 3, isActive: true);

        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        // Solo session on Thursday (PcId == AuditorId)
        var sid = TestDbHelper.InsertSession(conn, soloAudId, soloAudId, "2024-01-11", 3600);

        // CS review by the solo-type auditor on that session
        using var csReviewCmd = conn.CreateCommand();
        csReviewCmd.CommandText = @"
            INSERT INTO CsReviews (SessionId, CsId, ReviewLengthSeconds, Status)
            VALUES (@sid, @csId, 1800, 'Draft')";
        csReviewCmd.Parameters.AddWithValue("@sid",  sid);
        csReviewCmd.Parameters.AddWithValue("@csId", soloAudId);
        csReviewCmd.ExecuteNonQuery();

        var result = _svc.GetWeekDayStats(new DateOnly(2024, 1, 11));
        var thu    = result[0];
        var row    = thu.Staff.Single(s => s.Name.Contains("Aviv"));
        Assert.Equal(1800, row.SoloCsSec);
        Assert.Equal(3600 + 1800, row.TotalSec);
    }

    // =========================================================================
    // GetWeekDayStats – BodyInShop
    // =========================================================================

    [Fact]
    public void GetWeekDayStats_BodyInShop_IncludesBothSessionsAndAcademyVisits()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId, type: 1, isActive: true);
        var pcId    = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);
        var studentId = TestDbHelper.InsertPerson(conn, "Student1");

        // PC has a session on Thursday
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);

        // Student visits on Thursday
        using var sc = conn.CreateCommand();
        sc.CommandText = "INSERT INTO AcademyAttendance (PersonId, VisitDate) VALUES (@pid, '2024-01-11')";
        sc.Parameters.AddWithValue("@pid", studentId);
        sc.ExecuteNonQuery();

        var result = _svc.GetWeekDayStats(new DateOnly(2024, 1, 11));
        // pcId + studentId = 2 distinct persons in the building
        Assert.Equal(2, result[0].BodyInShop);
    }

    [Fact]
    public void GetWeekDayStats_BodyInShop_SamePersonAsBothPcAndStudent_CountedOnce()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId, type: 1, isActive: true);
        var personId = TestDbHelper.InsertPerson(conn, "SameGuy");
        TestDbHelper.InsertPC(conn, personId);

        // Same person has both a session AND an academy visit on Thursday
        TestDbHelper.InsertSession(conn, personId, audId, "2024-01-11", 3600);
        using var sc = conn.CreateCommand();
        sc.CommandText = "INSERT INTO AcademyAttendance (PersonId, VisitDate) VALUES (@pid, '2024-01-11')";
        sc.Parameters.AddWithValue("@pid", personId);
        sc.ExecuteNonQuery();

        var result = _svc.GetWeekDayStats(new DateOnly(2024, 1, 11));
        Assert.Equal(1, result[0].BodyInShop);
    }

    // =========================================================================
    // GetWeeklySummaries
    // =========================================================================

    [Fact]
    public void GetWeeklySummaries_ReturnsRequestedNumberOfWeeks()
    {
        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeeklySummaries(week, numWeeks: 5);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void GetWeeklySummaries_DefaultNumWeeks_Is20()
    {
        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeeklySummaries(week);
        Assert.Equal(20, result.Count);
    }

    [Fact]
    public void GetWeeklySummaries_EmptyDb_AllZeros()
    {
        var result = _svc.GetWeeklySummaries(new DateOnly(2024, 1, 11), 4);

        foreach (var week in result)
        {
            Assert.Equal(0, week.TotalAuditCsSec);
            Assert.Equal(0, week.AcademyCount);
            Assert.Equal(0, week.BodyInShop);
            Assert.Equal(0, week.PcCount);
        }
    }

    [Fact]
    public void GetWeeklySummaries_LastWeekIsLatestWeekStart()
    {
        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeeklySummaries(week, 3);
        Assert.Equal(week, result.Last().WeekStart);
    }

    [Fact]
    public void GetWeeklySummaries_TotalAuditCsSec_SumsAllSessionsInWeek()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId, type: 1, isActive: true);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        // Two sessions in the same week (Thu–Wed)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600, adminSec: 0);  // Thu
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-13", 1800, adminSec: 0);  // Sat

        var result = _svc.GetWeeklySummaries(new DateOnly(2024, 1, 11), 2);
        var thisWeek = result.Last();
        Assert.Equal(3600 + 1800, thisWeek.TotalAuditCsSec);
    }

    [Fact]
    public void GetWeeklySummaries_WeekLabel_ContainsBothDates()
    {
        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeeklySummaries(week, 1);
        var label  = result[0].WeekRangeLabel;
        Assert.Contains("11/01", label);
        Assert.Contains("17/01", label);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }
}
