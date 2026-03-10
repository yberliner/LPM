using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Tests for <see cref="AuditorService"/> – auditor CRUD, grades, stats, and type logic.
/// Each test gets a fresh isolated SQLite database.
/// </summary>
public class AuditorServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AuditorService _svc;

    public AuditorServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new AuditorService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // AddAuditor
    // =========================================================================

    [Fact]
    public void AddAuditor_CreatesPersonAndAuditorRecord()
    {
        var id = _svc.AddAuditor("Tami", "Cohen", null, 1);

        using var conn = Open();
        Assert.Equal(1L, TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM Persons  WHERE PersonId={id}"));
        Assert.Equal(1L, TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM Auditors WHERE AuditorId={id}"));
    }

    [Fact]
    public void AddAuditor_DefaultsToActive()
    {
        var id = _svc.AddAuditor("Genia", "L", null, 1);

        using var conn = Open();
        var isActive = TestDbHelper.Scalar(conn, $"SELECT IsActive FROM Auditors WHERE AuditorId={id}");
        Assert.Equal(1L, isActive);
    }

    [Fact]
    public void AddAuditor_WithGrade_AssignsGradeId()
    {
        // Grades are seeded with GradeId 1=BA, 2=MA, 3=PHD by TestDbHelper
        var id = _svc.AddAuditor("Eitan", "G", gradeId: 2, type: 1);

        var detail = _svc.GetAuditorDetail(id)!;
        Assert.Equal(2, detail.CurrentGradeId);
        Assert.Equal("MA", detail.GradeCode);
    }

    [Fact]
    public void AddAuditor_WithNullGrade_Succeeds()
    {
        var id     = _svc.AddAuditor("Eyal", "S", null, 1);
        var detail = _svc.GetAuditorDetail(id)!;
        Assert.Null(detail.CurrentGradeId);
        Assert.Null(detail.GradeCode);
    }

    [Fact]
    public void AddAuditor_MultipleAuditors_EachGetUniqueId()
    {
        var id1 = _svc.AddAuditor("A1", "", null, 1);
        var id2 = _svc.AddAuditor("A2", "", null, 1);
        var id3 = _svc.AddAuditor("A3", "", null, 1);
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
    }

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
        _svc.AddAuditor("Tami",  "C", null, 1);
        _svc.AddAuditor("Genia", "L", null, 1);
        _svc.AddAuditor("Eitan", "G", null, 1);

        Assert.Equal(3, _svc.GetAllAuditors().Count);
    }

    [Fact]
    public void GetAllAuditors_OrderedByFirstName()
    {
        _svc.AddAuditor("Zara",  "", null, 1);
        _svc.AddAuditor("Alice", "", null, 1);
        _svc.AddAuditor("Mike",  "", null, 1);

        var list  = _svc.GetAllAuditors();
        var names = list.Select(a => a.FullName).ToList();
        Assert.Equal(names.OrderBy(n => n).ToList(), names);
    }

    [Fact]
    public void GetAllAuditors_IncludesTypeAndIsActive()
    {
        var id = _svc.AddAuditor("Tami", "C", null, type: 3);
        var a  = _svc.GetAllAuditors().Single(x => x.AuditorId == id);
        Assert.Equal(3, a.Type);
        Assert.True(a.IsActive);
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
        var id     = _svc.AddAuditor("Carmela", "D", gradeId: 1, type: 3);
        var detail = _svc.GetAuditorDetail(id)!;

        Assert.Equal("Carmela", detail.FirstName);
        Assert.Equal("D",       detail.LastName);
        Assert.Equal(3,         detail.Type);
        Assert.True(detail.IsActive);
        Assert.Equal(1,   detail.CurrentGradeId);
        Assert.Equal("BA", detail.GradeCode);
    }

    // =========================================================================
    // UpdateAuditor
    // =========================================================================

    [Fact]
    public void UpdateAuditor_ChangesNameAndGrade()
    {
        var id = _svc.AddAuditor("OldFirst", "OldLast", null, 1);
        _svc.UpdateAuditor(id, "NewFirst", "NewLast", gradeId: 3, type: 1, isActive: true);

        var detail = _svc.GetAuditorDetail(id)!;
        Assert.Equal("NewFirst", detail.FirstName);
        Assert.Equal("NewLast",  detail.LastName);
        Assert.Equal(3,          detail.CurrentGradeId);
        Assert.Equal("PHD",      detail.GradeCode);
    }

    [Fact]
    public void UpdateAuditor_CanSetInactive()
    {
        var id = _svc.AddAuditor("Active", "A", null, 1);
        _svc.UpdateAuditor(id, "Active", "A", null, 0, isActive: false);

        var detail = _svc.GetAuditorDetail(id)!;
        Assert.False(detail.IsActive);
    }

    [Fact]
    public void UpdateAuditor_CanChangeType()
    {
        var id = _svc.AddAuditor("Solo", "S", null, type: 1);
        _svc.UpdateAuditor(id, "Solo", "S", null, type: 2, isActive: true);

        var detail = _svc.GetAuditorDetail(id)!;
        Assert.Equal(2, detail.Type);
    }

    // =========================================================================
    // Auditor Type semantics
    // =========================================================================
    //   0 = InActive, 1 = RegularOnly, 2 = SoloOnly, 3 = RegularAndSolo

    [Fact]
    public void AuditorType_RegularOnly_Type1_CorrectlySaved()
    {
        var id = _svc.AddAuditor("Reg", "R", null, type: 1);
        Assert.Equal(1, _svc.GetAuditorDetail(id)!.Type);
    }

    [Fact]
    public void AuditorType_SoloOnly_Type2_CorrectlySaved()
    {
        var id = _svc.AddAuditor("Solo", "S", null, type: 2);
        Assert.Equal(2, _svc.GetAuditorDetail(id)!.Type);
    }

    [Fact]
    public void AuditorType_RegularAndSolo_Type3_CorrectlySaved()
    {
        var id = _svc.AddAuditor("Both", "B", null, type: 3);
        Assert.Equal(3, _svc.GetAuditorDetail(id)!.Type);
    }

    // =========================================================================
    // GetAuditorStats
    // =========================================================================

    [Fact]
    public void GetAuditorStats_AllZero_WhenNoSessions()
    {
        var id    = _svc.AddAuditor("Tami", "", null, 1);
        var stats = _svc.GetAuditorStats(id);

        Assert.Equal(0, stats.TotalSessions);
        Assert.Equal(0, stats.FreeSessions);
        Assert.Equal(0L, stats.TotalSec);
        Assert.Null(stats.LastSessionDate);
    }

    [Fact]
    public void GetAuditorStats_CountsNonFreeSessionsAndTime()
    {
        var audId = _svc.AddAuditor("Tami", "", null, 1);

        using var conn = Open();
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
        var audId = _svc.AddAuditor("Genia", "", null, 1);

        using var conn = Open();
        var pcId = TestDbHelper.InsertPerson(conn, "Client2");
        TestDbHelper.InsertPC(conn, pcId);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600, seqInDay: 1, isFree: false);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 1800, seqInDay: 1, isFree: true);

        var stats = _svc.GetAuditorStats(audId);
        Assert.Equal(2, stats.TotalSessions);
        Assert.Equal(1, stats.FreeSessions);
    }

    [Fact]
    public void GetAuditorStats_ExcludesSoloSessions()
    {
        // AuditorStats should only count non-solo sessions.
        // Solo is detected by PcId = AuditorId pattern.
        var audId = _svc.AddAuditor("Aviv", "", null, type: 3);

        using var conn = Open();
        var pcId = TestDbHelper.InsertPerson(conn, "Client3");
        TestDbHelper.InsertPC(conn, pcId);
        // Regular session: PcId != AuditorId
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600, seqInDay: 1);
        // Solo session: PcId == AuditorId
        TestDbHelper.InsertSession(conn, audId, audId, "2024-01-10", 1800, seqInDay: 2);

        var stats = _svc.GetAuditorStats(audId);
        Assert.Equal(1,     stats.TotalSessions);
        Assert.Equal(3600L, stats.TotalSec);
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
