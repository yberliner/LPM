using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Comprehensive tests for <see cref="AcademyService"/>.
/// Covers person management, academy visits, weekly summaries, and edge cases.
/// </summary>
public class AcademyServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AcademyService _svc;

    public AcademyServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new AcademyService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // AddPersonForAcademy
    // =========================================================================

    [Fact]
    public void AddPersonForAcademy_CreatesPersonRecord()
    {
        var id = _svc.AddPersonForAcademy("Alice", "Green", "050-111", "alice@x.com", "1990-01-15", "F");

        using var conn = Open();
        Assert.Equal(1L, TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM core_persons WHERE PersonId={id}"));
    }

    [Fact]
    public void AddPersonForAcademy_ReturnsUniqueIds()
    {
        var id1 = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "F");
        var id2 = _svc.AddPersonForAcademy("Bob",   "B", "", "", "", "M");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void AddPersonForAcademy_StoresAllFields()
    {
        var id = _svc.AddPersonForAcademy("Dana", "Blue", "052-999", "dana@t.com", "1985-07-20", "F");

        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT FirstName,LastName,Phone,Email,DateOfBirth,Gender FROM core_persons WHERE PersonId={id}";
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal("Dana",       r.GetString(0));
        Assert.Equal("Blue",       r.GetString(1));
        Assert.Equal("052-999",    r.GetString(2));
        Assert.Equal("dana@t.com", r.GetString(3));
        Assert.Equal("1985-07-20", r.GetString(4));
        Assert.Equal("F",          r.GetString(5));
    }

    [Fact]
    public void AddPersonForAcademy_WithEmptyOptionals_StoresNull()
    {
        var id = _svc.AddPersonForAcademy("Eli", "", "", "", "", "");

        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT Phone FROM core_persons WHERE PersonId={id}";
        var phone = cmd.ExecuteScalar();
        Assert.True(phone is DBNull || phone is null);
    }

    [Fact]
    public void AddPersonForAcademy_TrimsWhitespace()
    {
        var id = _svc.AddPersonForAcademy("  Zara  ", "  Lee  ", "", "", "", "");

        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT FirstName,LastName FROM core_persons WHERE PersonId={id}";
        using var r = cmd.ExecuteReader();
        r.Read();
        Assert.Equal("Zara", r.GetString(0));
        Assert.Equal("Lee",  r.GetString(1));
    }

    // =========================================================================
    // GetAllPersons
    // =========================================================================

    [Fact]
    public void GetAllPersons_ReturnsEmpty_WhenNoPersons()
    {
        Assert.Empty(_svc.GetAllPersons());
    }

    [Fact]
    public void GetAllPersons_ReturnsAllPersons()
    {
        _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        _svc.AddPersonForAcademy("Bob",   "B", "", "", "", "");
        _svc.AddPersonForAcademy("Carol", "C", "", "", "", "");
        Assert.Equal(3, _svc.GetAllPersons().Count);
    }

    [Fact]
    public void GetAllPersons_OrderedByFirstNameThenLastName()
    {
        _svc.AddPersonForAcademy("Zara",  "A", "", "", "", "");
        _svc.AddPersonForAcademy("Alice", "B", "", "", "", "");
        _svc.AddPersonForAcademy("Mike",  "C", "", "", "", "");

        var persons = _svc.GetAllPersons();
        var names   = persons.Select(p => p.FullName).ToList();
        Assert.Equal(names.OrderBy(n => n).ToList(), names);
    }

    [Fact]
    public void GetAllPersons_FullName_CombinesFirstAndLast()
    {
        _svc.AddPersonForAcademy("John", "Doe", "", "", "", "");
        var persons = _svc.GetAllPersons();
        Assert.Contains(persons, p => p.FullName == "John Doe");
    }

    [Fact]
    public void GetAllPersons_FullName_HandlesEmptyLastName()
    {
        _svc.AddPersonForAcademy("Solo", "", "", "", "", "");
        var persons = _svc.GetAllPersons();
        Assert.Contains(persons, p => p.FullName == "Solo");
    }

    // =========================================================================
    // AddVisit / GetVisitsForDay
    // =========================================================================

    [Fact]
    public void AddVisit_CreatesStudentRecord()
    {
        var pid = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var day = new DateOnly(2024, 1, 15);
        _svc.AddVisit(pid, day);

        var visits = _svc.GetVisitsForDay(day);
        Assert.Single(visits);
        Assert.Equal(pid, visits[0].PersonId);
    }

    [Fact]
    public void AddVisit_DuplicateOnSameDay_IsIgnored()
    {
        var pid = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var day = new DateOnly(2024, 1, 15);
        _svc.AddVisit(pid, day);
        _svc.AddVisit(pid, day);  // duplicate — INSERT OR IGNORE

        Assert.Single(_svc.GetVisitsForDay(day));
    }

    [Fact]
    public void AddVisit_SamePerson_DifferentDays_BothStored()
    {
        var pid  = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var day1 = new DateOnly(2024, 1, 15);
        var day2 = new DateOnly(2024, 1, 16);
        _svc.AddVisit(pid, day1);
        _svc.AddVisit(pid, day2);

        Assert.Single(_svc.GetVisitsForDay(day1));
        Assert.Single(_svc.GetVisitsForDay(day2));
    }

    [Fact]
    public void GetVisitsForDay_ReturnsEmpty_WhenNoVisitsOnDate()
    {
        Assert.Empty(_svc.GetVisitsForDay(new DateOnly(2024, 6, 1)));
    }

    [Fact]
    public void GetVisitsForDay_ReturnsOnlyVisitsForThatDay()
    {
        var pid1 = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var pid2 = _svc.AddPersonForAcademy("Bob",   "B", "", "", "", "");
        var day  = new DateOnly(2024, 1, 15);
        _svc.AddVisit(pid1, day);
        _svc.AddVisit(pid2, new DateOnly(2024, 1, 16));  // different day

        var visits = _svc.GetVisitsForDay(day);
        Assert.Single(visits);
        Assert.Equal(pid1, visits[0].PersonId);
    }

    [Fact]
    public void GetVisitsForDay_MultiplePeople_ReturnsAll()
    {
        var pid1 = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var pid2 = _svc.AddPersonForAcademy("Bob",   "B", "", "", "", "");
        var pid3 = _svc.AddPersonForAcademy("Carol", "C", "", "", "", "");
        var day  = new DateOnly(2024, 1, 15);
        _svc.AddVisit(pid1, day);
        _svc.AddVisit(pid2, day);
        _svc.AddVisit(pid3, day);

        Assert.Equal(3, _svc.GetVisitsForDay(day).Count);
    }

    [Fact]
    public void GetVisitsForDay_ReturnsCorrectFullName()
    {
        var pid = _svc.AddPersonForAcademy("Dana", "Blue", "", "", "", "");
        var day = new DateOnly(2024, 1, 15);
        _svc.AddVisit(pid, day);

        var visit = _svc.GetVisitsForDay(day)[0];
        Assert.Equal("Dana Blue", visit.FullName);
    }

    [Fact]
    public void GetVisitsForDay_ReturnsVisitId_GreaterThanZero()
    {
        var pid = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        _svc.AddVisit(pid, new DateOnly(2024, 1, 15));

        var visits = _svc.GetVisitsForDay(new DateOnly(2024, 1, 15));
        Assert.True(visits[0].VisitId > 0);
    }

    // =========================================================================
    // RemoveVisit
    // =========================================================================

    [Fact]
    public void RemoveVisit_DeletesVisitRecord()
    {
        var pid = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var day = new DateOnly(2024, 1, 15);
        _svc.AddVisit(pid, day);

        var visitId = _svc.GetVisitsForDay(day)[0].VisitId;
        _svc.RemoveVisit(visitId);

        Assert.Empty(_svc.GetVisitsForDay(day));
    }

    [Fact]
    public void RemoveVisit_OnlyRemovesSpecifiedVisit()
    {
        var pid1 = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var pid2 = _svc.AddPersonForAcademy("Bob",   "B", "", "", "", "");
        var day  = new DateOnly(2024, 1, 15);
        _svc.AddVisit(pid1, day);
        _svc.AddVisit(pid2, day);

        var visits  = _svc.GetVisitsForDay(day);
        var aliceId = visits.First(v => v.PersonId == pid1).VisitId;
        _svc.RemoveVisit(aliceId);

        var remaining = _svc.GetVisitsForDay(day);
        Assert.Single(remaining);
        Assert.Equal(pid2, remaining[0].PersonId);
    }

    [Fact]
    public void RemoveVisit_NonExistentId_DoesNotThrow()
    {
        // Deleting a non-existent ID should be a no-op
        var ex = Record.Exception(() => _svc.RemoveVisit(99999));
        Assert.Null(ex);
    }

    // =========================================================================
    // GetStudentVisitsForWeek
    // =========================================================================

    [Fact]
    public void GetStudentVisitsForWeek_ReturnsEmpty_WhenNoVisits()
    {
        Assert.Empty(_svc.GetStudentVisitsForWeek(new DateOnly(2024, 1, 11)));
    }

    [Fact]
    public void GetStudentVisitsForWeek_CountsVisitsCorrectly()
    {
        var pid  = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var week = new DateOnly(2024, 1, 11); // Thu
        _svc.AddVisit(pid, new DateOnly(2024, 1, 11)); // Thu
        _svc.AddVisit(pid, new DateOnly(2024, 1, 13)); // Sat
        _svc.AddVisit(pid, new DateOnly(2024, 1, 15)); // Mon

        var result = _svc.GetStudentVisitsForWeek(week);
        Assert.Single(result);
        Assert.Equal(3, result[0].VisitCount);
    }

    [Fact]
    public void GetStudentVisitsForWeek_ExcludesVisitsOutsideWeek()
    {
        var pid  = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var week = new DateOnly(2024, 1, 11);
        _svc.AddVisit(pid, new DateOnly(2024, 1, 11)); // In week
        _svc.AddVisit(pid, new DateOnly(2024, 1, 18)); // Next week — excluded

        var result = _svc.GetStudentVisitsForWeek(week);
        Assert.Single(result);
        Assert.Equal(1, result[0].VisitCount);
    }

    [Fact]
    public void GetStudentVisitsForWeek_MultipleStudents_OrderedByVisitCountDesc()
    {
        var pid1 = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var pid2 = _svc.AddPersonForAcademy("Bob",   "B", "", "", "", "");
        var week = new DateOnly(2024, 1, 11);

        // Alice visits 3 times, Bob 1 time
        _svc.AddVisit(pid1, new DateOnly(2024, 1, 11));
        _svc.AddVisit(pid1, new DateOnly(2024, 1, 12));
        _svc.AddVisit(pid1, new DateOnly(2024, 1, 13));
        _svc.AddVisit(pid2, new DateOnly(2024, 1, 11));

        var result = _svc.GetStudentVisitsForWeek(week);
        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].VisitCount);  // Alice first (more visits)
        Assert.Equal(1, result[1].VisitCount);
    }

    [Fact]
    public void GetStudentVisitsForWeek_IncludesAllDaysInWeek_ThuToWed()
    {
        // Week is Thu 2024-01-11 to Wed 2024-01-17
        var pid  = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var week = new DateOnly(2024, 1, 11);

        // Visit on last day of week (Wednesday)
        _svc.AddVisit(pid, new DateOnly(2024, 1, 17));

        var result = _svc.GetStudentVisitsForWeek(week);
        Assert.Single(result);
        Assert.Equal(1, result[0].VisitCount);
    }

    // =========================================================================
    // GetWeeklyVisitCounts
    // =========================================================================

    [Fact]
    public void GetWeeklyVisitCounts_ReturnsRequestedNumberOfWeeks()
    {
        var result = _svc.GetWeeklyVisitCounts(new DateOnly(2024, 1, 11), numWeeks: 5);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void GetWeeklyVisitCounts_Default_Returns20Weeks()
    {
        var result = _svc.GetWeeklyVisitCounts(new DateOnly(2024, 1, 11));
        Assert.Equal(20, result.Count);
    }

    [Fact]
    public void GetWeeklyVisitCounts_EmptyDb_AllZeroCounts()
    {
        var result = _svc.GetWeeklyVisitCounts(new DateOnly(2024, 1, 11), 4);
        Assert.All(result, w => Assert.Equal(0, w.TotalVisits));
    }

    [Fact]
    public void GetWeeklyVisitCounts_CorrectWeekLabel_Format_ddMM()
    {
        var result = _svc.GetWeeklyVisitCounts(new DateOnly(2024, 1, 11), 1);
        Assert.Equal("11/01", result[0].WeekLabel);
    }

    [Fact]
    public void GetWeeklyVisitCounts_TotalVisits_MatchesVisitsInWeek()
    {
        var pid  = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var pid2 = _svc.AddPersonForAcademy("Bob",   "B", "", "", "", "");
        var week = new DateOnly(2024, 1, 11);

        _svc.AddVisit(pid,  new DateOnly(2024, 1, 11));
        _svc.AddVisit(pid,  new DateOnly(2024, 1, 12));
        _svc.AddVisit(pid2, new DateOnly(2024, 1, 13));

        var result   = _svc.GetWeeklyVisitCounts(week, 1);
        Assert.Equal(3, result[0].TotalVisits);
    }

    [Fact]
    public void GetWeeklyVisitCounts_TopStudents_OrderedByVisitCountDesc()
    {
        var pid1 = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var pid2 = _svc.AddPersonForAcademy("Bob",   "B", "", "", "", "");
        var week = new DateOnly(2024, 1, 11);

        // Bob visits 2, Alice 1
        _svc.AddVisit(pid2, new DateOnly(2024, 1, 11));
        _svc.AddVisit(pid2, new DateOnly(2024, 1, 12));
        _svc.AddVisit(pid1, new DateOnly(2024, 1, 13));

        var top = _svc.GetWeeklyVisitCounts(week, 1)[0].TopStudents;
        Assert.Equal(2, top.Count);
        Assert.Equal("Bob B", top[0].FullName); // most visits first
    }

    [Fact]
    public void GetWeeklyVisitCounts_VisitsInDifferentWeeks_CountedSeparately()
    {
        var pid  = _svc.AddPersonForAcademy("Alice", "A", "", "", "", "");
        var week = new DateOnly(2024, 1, 11);

        _svc.AddVisit(pid, new DateOnly(2024, 1, 11));  // week 0
        _svc.AddVisit(pid, new DateOnly(2024, 1, 18));  // week 1 (next Thursday)

        var result = _svc.GetWeeklyVisitCounts(new DateOnly(2024, 1, 18), 2);
        Assert.Equal(1, result[0].TotalVisits);  // week of Jan 11
        Assert.Equal(1, result[1].TotalVisits);  // week of Jan 18
    }

    // =========================================================================
    // Cross-service: AcademyService person visible in DashboardService
    // =========================================================================

    [Fact]
    public void PersonAddedViaAcademy_VisibleInDashboardService_GetUserIdByUsername()
    {
        // GetUserIdByUsername queries core_users.Username (not core_persons.FirstName).
        // AddPersonForAcademy only creates a core_persons row; we must also create a core_users entry.
        var id  = _svc.AddPersonForAcademy("Miriam", "Katz", "", "", "", "");
        var svc = new DashboardService(TestConfig.For(_dbPath), new LPM.Services.MessageNotifier(), new LPM.Services.HtmlSanitizerService());

        using var conn = Open();
        TestDbHelper.InsertCoreUser(conn, id, "miriam", "pass1234");

        var result = svc.GetUserIdByUsername("miriam");
        Assert.Equal(id, result);
    }

    [Fact]
    public void PersonAddedViaAcademy_AppearsInStatisticsService_BodyInShop()
    {
        var pid  = _svc.AddPersonForAcademy("Ron", "Levi", "", "", "", "");
        var week = new DateOnly(2024, 1, 11);
        _svc.AddVisit(pid, new DateOnly(2024, 1, 11));  // Thursday

        var stats = new StatisticsService(TestConfig.For(_dbPath));
        var days  = stats.GetWeekDayStats(week);
        // The student visit should appear in BodyInShop for Thursday
        Assert.Equal(1, days[0].BodyInShop);
    }

    [Fact]
    public void AcademyVisit_AppearsInStatistics_AcademyCount()
    {
        var pid  = _svc.AddPersonForAcademy("Ora", "Ben", "", "", "", "");
        var week = new DateOnly(2024, 1, 11);
        _svc.AddVisit(pid, new DateOnly(2024, 1, 11));

        var stats = new StatisticsService(TestConfig.For(_dbPath));
        var days  = stats.GetWeekDayStats(week);
        Assert.Equal(1, days[0].AcademyCount);
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
