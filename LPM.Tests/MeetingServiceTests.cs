using LPM.Services;
using LPM.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LPM.Tests;

public class MeetingServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MeetingService _svc;

    public MeetingServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new MeetingService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    private SqliteConnection OpenConn() => new($"Data Source={_dbPath}");

    private int CreatePc(SqliteConnection conn, string firstName)
    {
        var pid = TestDbHelper.InsertPerson(conn, firstName);
        TestDbHelper.InsertPC(conn, pid);
        return pid;
    }

    private static DateTime D(string s) => DateTime.Parse(s);

    // ── AddMeeting / GetMeetingById ───────────────────────────────────────

    [Fact]
    public void AddMeeting_ReturnsId_AndGetById_Works()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");

        var id = _svc.AddMeeting(pcId, null, "Intro",
            D("2025-03-10 10:00:00"), 3600, isWeekly: false, createdBy: creatorId);

        Assert.True(id > 0);
        var m = _svc.GetMeetingById(id);
        Assert.NotNull(m);
        Assert.Equal(pcId,   m.PcId);
        Assert.Equal("Intro", m.MeetingType);
        Assert.Equal(3600,    m.LengthSeconds);
        Assert.False(m.IsWeekly);
        Assert.Null(m.AuditorId);
    }

    [Fact]
    public void GetMeetingById_ReturnsNull_IfNotExists()
    {
        Assert.Null(_svc.GetMeetingById(99999));
    }

    [Fact]
    public void AddMeeting_WithAuditor_JoinsAuditorName()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var auditorId = TestDbHelper.InsertPerson(conn, "Bob", "Smith");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");

        var id = _svc.AddMeeting(pcId, auditorId, "Review",
            D("2025-03-10 14:00:00"), 1800, false, creatorId);

        var m = _svc.GetMeetingById(id);
        Assert.Equal(auditorId, m!.AuditorId);
        Assert.Contains("Bob", m.AuditorName);
    }

    [Fact]
    public void AddMeeting_PcNameIsJoined()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");

        var id = _svc.AddMeeting(pcId, null, "Intro",
            D("2025-03-10 10:00:00"), 3600, false, creatorId);

        var m = _svc.GetMeetingById(id);
        Assert.Contains("Alice", m!.PcName);
    }

    // ── GetMeetings — non-recurring ───────────────────────────────────────

    [Fact]
    public void GetMeetings_NonRecurring_InRange_Returned()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        _svc.AddMeeting(pcId, null, "Intro", D("2025-03-12 10:00:00"), 3600, false, creatorId);

        var result = _svc.GetMeetings(D("2025-03-10"), D("2025-03-15"), "Intro");

        Assert.Single(result);
    }

    [Fact]
    public void GetMeetings_NonRecurring_BeforeRange_Excluded()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        _svc.AddMeeting(pcId, null, "Intro", D("2025-03-05 10:00:00"), 3600, false, creatorId);

        var result = _svc.GetMeetings(D("2025-03-10"), D("2025-03-15"), "Intro");

        Assert.Empty(result);
    }

    [Fact]
    public void GetMeetings_NonRecurring_AfterRange_Excluded()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        _svc.AddMeeting(pcId, null, "Intro", D("2025-03-20 10:00:00"), 3600, false, creatorId);

        var result = _svc.GetMeetings(D("2025-03-10"), D("2025-03-15"), "Intro");

        Assert.Empty(result);
    }

    [Fact]
    public void GetMeetings_FiltersByMeetingType()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        _svc.AddMeeting(pcId, null, "Intro",  D("2025-03-12 10:00:00"), 3600, false, creatorId);
        _svc.AddMeeting(pcId, null, "Review", D("2025-03-12 11:00:00"), 3600, false, creatorId);

        var result = _svc.GetMeetings(D("2025-03-10"), D("2025-03-15"), "Intro");

        Assert.Single(result);
        Assert.Equal("Intro", result[0].MeetingType);
    }

    [Fact]
    public void GetMeetings_OrderedByStartAt()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        _svc.AddMeeting(pcId, null, "Intro", D("2025-03-12 14:00:00"), 3600, false, creatorId);
        _svc.AddMeeting(pcId, null, "Intro", D("2025-03-11 09:00:00"), 3600, false, creatorId);

        var result = _svc.GetMeetings(D("2025-03-10"), D("2025-03-15"), "Intro");

        Assert.Equal(2, result.Count);
        Assert.True(result[0].StartAt < result[1].StartAt);
    }

    // ── GetMeetings — weekly ──────────────────────────────────────────────

    [Fact]
    public void GetMeetings_Weekly_ExpandsToMultipleOccurrences()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        // 2025-03-03 is Monday; Mondays in March: 3, 10, 17, 24
        _svc.AddMeeting(pcId, null, "Class", D("2025-03-03 09:00:00"), 3600, true, creatorId);

        var result = _svc.GetMeetings(D("2025-03-03"), D("2025-03-31"), "Class");

        Assert.Equal(4, result.Count);
        Assert.Equal(D("2025-03-03 09:00:00"), result[0].StartAt);
        Assert.Equal(D("2025-03-10 09:00:00"), result[1].StartAt);
        Assert.Equal(D("2025-03-17 09:00:00"), result[2].StartAt);
        Assert.Equal(D("2025-03-24 09:00:00"), result[3].StartAt);
    }

    [Fact]
    public void GetMeetings_Weekly_AdvancesToFirstOccurrenceInRange()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        // 2025-01-06 is Monday; query 1 week in March
        _svc.AddMeeting(pcId, null, "Class", D("2025-01-06 09:00:00"), 3600, true, creatorId);

        var result = _svc.GetMeetings(D("2025-03-17"), D("2025-03-24"), "Class");

        Assert.Single(result);
        Assert.Equal(D("2025-03-17 09:00:00"), result[0].StartAt);
    }

    [Fact]
    public void GetMeetings_Weekly_NoOccurrencesBeforeStart()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        // Weekly starting after the range
        _svc.AddMeeting(pcId, null, "Class", D("2025-04-07 09:00:00"), 3600, true, creatorId);

        var result = _svc.GetMeetings(D("2025-03-01"), D("2025-03-31"), "Class");

        Assert.Empty(result);
    }

    [Fact]
    public void GetMeetings_AllOccurrencesShareOriginalMeetingId()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        var id = _svc.AddMeeting(pcId, null, "Class", D("2025-03-03 09:00:00"), 3600, true, creatorId);

        var result = _svc.GetMeetings(D("2025-03-03"), D("2025-03-31"), "Class");

        Assert.All(result, m => Assert.Equal(id, m.MeetingId));
    }

    // ── UpdateMeeting ─────────────────────────────────────────────────────

    [Fact]
    public void UpdateMeeting_ChangesAllFields()
    {
        using var conn = OpenConn(); conn.Open();
        var pc1       = CreatePc(conn, "Alice");
        var pc2       = CreatePc(conn, "Bob");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");

        var id = _svc.AddMeeting(pc1, null, "Intro", D("2025-03-10 10:00:00"), 3600, false, creatorId);
        _svc.UpdateMeeting(id, pc2, null, "Review", D("2025-04-01 14:00:00"), 1800, true);

        var m = _svc.GetMeetingById(id)!;
        Assert.Equal(pc2,     m.PcId);
        Assert.Equal("Review", m.MeetingType);
        Assert.Equal("2025-04-01 14:00", m.StartAt.ToString("yyyy-MM-dd HH:mm"));
        Assert.Equal(1800,    m.LengthSeconds);
        Assert.True(m.IsWeekly);
    }

    // ── DeleteMeeting ─────────────────────────────────────────────────────

    [Fact]
    public void DeleteMeeting_RemovesRow()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        var id = _svc.AddMeeting(pcId, null, "Intro", D("2025-03-10 10:00:00"), 3600, false, creatorId);

        _svc.DeleteMeeting(id);

        Assert.Null(_svc.GetMeetingById(id));
    }

    [Fact]
    public void DeleteMeeting_OnlyRemovesTarget()
    {
        using var conn = OpenConn(); conn.Open();
        var pcId      = CreatePc(conn, "Alice");
        var creatorId = TestDbHelper.InsertPerson(conn, "Admin");
        var id1 = _svc.AddMeeting(pcId, null, "Intro",  D("2025-03-10 10:00:00"), 3600, false, creatorId);
        var id2 = _svc.AddMeeting(pcId, null, "Review", D("2025-03-11 10:00:00"), 3600, false, creatorId);

        _svc.DeleteMeeting(id1);

        Assert.Null(_svc.GetMeetingById(id1));
        Assert.NotNull(_svc.GetMeetingById(id2));
    }
}
