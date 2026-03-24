using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Tests for <see cref="AuditorService"/> – auditor read, grades, stats.
/// Auditors are created directly via TestDbHelper (no AddAuditor service method exists).
/// Each test gets a fresh isolated SQLite database.
/// </summary>
public class AuditorServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AuditorService _svc;

    public AuditorServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new AuditorService(TestConfig.For(_dbPath), new LPM.Auth.UserDb(TestConfig.For(_dbPath)));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // GetAllAuditors
    // =========================================================================

    [Fact]
    public void GetAllAuditors_ReturnsEmpty_WhenNoAuditors()
    {
        Assert.Empty(_svc.GetAllAuditors());
    }

    [Fact]
    public void GetAllAuditors_ReturnsAllAddedAuditors()
    {
        using var conn = Open();
        var id1 = TestDbHelper.InsertPerson(conn, "Tami",  "C");
        var id2 = TestDbHelper.InsertPerson(conn, "Genia", "L");
        var id3 = TestDbHelper.InsertPerson(conn, "Eitan", "G");
        TestDbHelper.InsertAuditor(conn, id1, staffRole: "Auditor");
        TestDbHelper.InsertAuditor(conn, id2, staffRole: "Auditor");
        TestDbHelper.InsertAuditor(conn, id3, staffRole: "Auditor");

        Assert.Equal(3, _svc.GetAllAuditors().Count);
    }

    [Fact]
    public void GetAllAuditors_OrderedByFirstName()
    {
        using var conn = Open();
        var id1 = TestDbHelper.InsertPerson(conn, "Zara",  "Z");
        var id2 = TestDbHelper.InsertPerson(conn, "Alice", "A");
        var id3 = TestDbHelper.InsertPerson(conn, "Mike",  "M");
        TestDbHelper.InsertAuditor(conn, id1, staffRole: "Auditor");
        TestDbHelper.InsertAuditor(conn, id2, staffRole: "Auditor");
        TestDbHelper.InsertAuditor(conn, id3, staffRole: "Auditor");

        var list  = _svc.GetAllAuditors();
        var names = list.Select(a => a.FullName).ToList();
        Assert.Equal(names.OrderBy(n => n).ToList(), names);
    }

    [Fact]
    public void GetAllAuditors_IncludesStaffRoleAndIsActive()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Tami", "C");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "CS", isActive: true);

        var a = _svc.GetAllAuditors().Single(x => x.AuditorId == id);
        Assert.Equal("CS", a.StaffRole);
        Assert.True(a.IsActive);
    }

    [Fact]
    public void GetAllAuditors_ExcludesSoloStaff()
    {
        // GetAllAuditors only returns StaffRole IN ('Auditor','CS') — Solo is excluded
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "SoloUser", "S");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Solo", isActive: true);

        var list = _svc.GetAllAuditors();
        Assert.Empty(list);
    }

    // =========================================================================
    // GetAuditorDetail
    // =========================================================================

    [Fact]
    public void GetAuditorDetail_ReturnsNull_ForNonExistentAuditor()
    {
        Assert.Null(_svc.GetAuditorDetail(9999));
    }

    [Fact]
    public void GetAuditorDetail_ReturnsCorrectFields()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Carmela", "D");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "CS", isActive: true, gradeId: 1);

        var detail = _svc.GetAuditorDetail(id)!;
        Assert.Equal("Carmela", detail.FirstName);
        Assert.Equal("D",       detail.LastName);
        Assert.Equal("CS",      detail.StaffRole);
        Assert.True(detail.IsActive);
        Assert.Equal(1,    detail.CurrentGradeId);
        Assert.Equal("BA", detail.GradeCode);
    }

    [Fact]
    public void GetAuditorDetail_WithNullGrade_ReturnsNulls()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Eyal", "S");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Auditor");

        var detail = _svc.GetAuditorDetail(id)!;
        Assert.Null(detail.CurrentGradeId);
        Assert.Null(detail.GradeCode);
    }

    // =========================================================================
    // UpdateAuditor
    // =========================================================================

    [Fact]
    public void UpdateAuditor_ChangesNameAndGrade()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "OldFirst", "OldLast");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Auditor");

        _svc.UpdateAuditor(id, "NewFirst", "NewLast", gradeId: 3, staffRole: "Auditor", isActive: true, isAdmin: false, allowAll: false);

        var detail = _svc.GetAuditorDetail(id)!;
        Assert.Equal("NewFirst", detail.FirstName);
        Assert.Equal("NewLast",  detail.LastName);
        Assert.Equal(3,          detail.CurrentGradeId);
        Assert.Equal("PHD",      detail.GradeCode);
    }

    [Fact]
    public void UpdateAuditor_CanSetInactive()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Active", "A");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Auditor", isActive: true);

        _svc.UpdateAuditor(id, "Active", "A", null, staffRole: "Auditor", isActive: false, isAdmin: false, allowAll: false);

        var detail = _svc.GetAuditorDetail(id)!;
        Assert.False(detail.IsActive);
    }

    [Fact]
    public void UpdateAuditor_CanChangeStaffRole()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Staff", "S");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Auditor");

        _svc.UpdateAuditor(id, "Staff", "S", null, staffRole: "CS", isActive: true, isAdmin: false, allowAll: false);

        var detail = _svc.GetAuditorDetail(id)!;
        Assert.Equal("CS", detail.StaffRole);
    }

    // =========================================================================
    // GetAuditorStats
    // =========================================================================

    [Fact]
    public void GetAuditorStats_AllZero_WhenNoSessions()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Tami", "C");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Auditor");

        var stats = _svc.GetAuditorStats(id);
        Assert.Equal(0, stats.TotalSessions);
        Assert.Equal(0, stats.FreeSessions);
        Assert.Equal(0L, stats.TotalSec);
        Assert.Null(stats.LastSessionDate);
    }

    [Fact]
    public void GetAuditorStats_CountsNonFreeSessionsAndTime()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Tami", "C");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Auditor");
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 1800, seqInDay: 1);

        var stats = _svc.GetAuditorStats(audId);
        Assert.Equal(2,     stats.TotalSessions);
        Assert.Equal(0,     stats.FreeSessions);
        Assert.Equal(5400L, stats.TotalSec);
        Assert.Equal("2024-01-11", stats.LastSessionDate);
    }

    [Fact]
    public void GetAuditorStats_CountsFreeSessions()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Genia", "L");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Auditor");
        var pcId = TestDbHelper.InsertPerson(conn, "Client2");
        TestDbHelper.InsertPC(conn, pcId);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600, seqInDay: 1, isFree: false);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 1800, seqInDay: 1, isFree: true);

        var stats = _svc.GetAuditorStats(audId);
        Assert.Equal(2, stats.TotalSessions);
        Assert.Equal(1, stats.FreeSessions);
    }

    [Fact]
    public void GetAuditorStats_CountsAllSessionsWithAuditorId()
    {
        // GetAuditorStats counts ALL sessions WHERE AuditorId=@id, including solo-style ones
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aviv", "A");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Auditor");
        var pcId = TestDbHelper.InsertPerson(conn, "Client3");
        TestDbHelper.InsertPC(conn, pcId);

        // Regular session (PcId != AuditorId)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600, seqInDay: 1);
        // "Solo-style" session (PcId == AuditorId), AuditorId is still set
        TestDbHelper.InsertSession(conn, audId, audId, "2024-01-10", 1800, seqInDay: 2);

        var stats = _svc.GetAuditorStats(audId);
        // Both sessions have AuditorId=audId so both are counted
        Assert.Equal(2, stats.TotalSessions);
    }

    // =========================================================================
    // GetAllGrades
    // =========================================================================

    [Fact]
    public void GetAllGrades_ReturnsSeededGrades()
    {
        var grades = _svc.GetAllGrades();
        Assert.Equal(3, grades.Count);  // BA, MA, PHD seeded by TestDbHelper
    }

    [Fact]
    public void GetAllGrades_OrderedBySortOrder()
    {
        var grades = _svc.GetAllGrades();
        var codes  = grades.Select(g => g.Code).ToList();
        Assert.Equal(new[] { "BA", "MA", "PHD" }, codes);
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
