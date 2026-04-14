using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Tests for <see cref="PcService"/> – client/patient (PC) management.
/// Each test gets a fresh isolated SQLite database via <see cref="TestDbHelper"/>.
/// </summary>
public class PcServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PcService _svc;

    public PcServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new PcService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // AddPcWithPerson
    // =========================================================================

    [Fact]
    public void AddPcWithPerson_CreatesPersonAndPcRecord()
    {
        var id = _svc.AddPcWithPerson("Alice", "Green", "050-1111111", "alice@email.com", "1990-01-15", "F");

        using var conn = Open();
        Assert.Equal(1L, TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM core_persons WHERE PersonId={id}"));
        Assert.Equal(1L, TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM core_pcs     WHERE PcId={id}"));
    }

    [Fact]
    public void AddPcWithPerson_ReturnsUniqueIds_ForMultipleClients()
    {
        var id1 = _svc.AddPcWithPerson("Alice", "Green",  "", "", "", "F");
        var id2 = _svc.AddPcWithPerson("Bob",   "Silver", "", "", "", "M");
        var id3 = _svc.AddPcWithPerson("Carol", "White",  "", "", "", "F");

        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
    }

    [Fact]
    public void AddPcWithPerson_StoresAllPersonFields()
    {
        var id = _svc.AddPcWithPerson("Dana", "Blue", "052-9999999", "dana@test.com", "1985-07-20", "F");

        var detail = _svc.GetPcDetail(id)!;
        Assert.Equal("Dana",          detail.FirstName);
        Assert.Equal("Blue",          detail.LastName);
        Assert.Equal("052-9999999",   detail.Phone);
        Assert.Equal("dana@test.com", detail.Email);
        Assert.Equal("1985-07-20",    detail.DateOfBirth);
        Assert.Equal("F",             detail.Gender);
    }

    [Fact]
    public void AddPcWithPerson_WithOptionalFieldsEmpty_Succeeds()
    {
        var id = _svc.AddPcWithPerson("Eli", "", "", "", "", "");
        Assert.True(id > 0);

        var detail = _svc.GetPcDetail(id)!;
        Assert.Equal("Eli", detail.FirstName);
        Assert.Equal("",    detail.LastName);
    }

    // =========================================================================
    // GetAllPcs
    // =========================================================================

    [Fact]
    public void GetAllPcs_ReturnsEmptyList_WhenNoPcs()
    {
        var list = _svc.GetAllPcs();
        Assert.Empty(list);
    }

    [Fact]
    public void GetAllPcs_ReturnsAllAddedClients()
    {
        _svc.AddPcWithPerson("Alice", "A", "", "", "", "");
        _svc.AddPcWithPerson("Bob",   "B", "", "", "", "");
        _svc.AddPcWithPerson("Carol", "C", "", "", "", "");

        var list = _svc.GetAllPcs();
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void GetAllPcs_RemainingSec_IsZero_WhenNoPurchasesAndNoSessions()
    {
        _svc.AddPcWithPerson("Alice", "A", "", "", "", "");
        var list = _svc.GetAllPcs();
        Assert.Equal(0L, list[0].RemainSec);
    }

    [Fact]
    public void GetAllPcs_RemainingSec_IsPositive_WhenHoursBoughtExceedUsed()
    {
        var pcId = _svc.AddPcWithPerson("Alice", "A", "", "", "", "");
        // Buy 2 hours = 7200 sec via CreatePurchase
        _svc.CreatePurchase(pcId, "2024-01-10", null, null, null,
            new List<(string, int?, int?, double, int)> { ("Auditing", null, null, 2.0, 500) });

        // Session of 1 hour = 3600 sec (not free)
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Auditor1");
        TestDbHelper.InsertAuditor(conn, audId);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-15", 3600);

        var list = _svc.GetAllPcs();
        var pc   = list.Single(p => p.PcId == pcId);
        Assert.Equal(3600L, pc.RemainSec);   // 7200 − 3600
    }

    [Fact]
    public void GetAllPcs_RemainingSec_IsNegative_WhenUsedExceedsBought()
    {
        var pcId = _svc.AddPcWithPerson("Alice", "A", "", "", "", "");
        // Buy only 1 hour
        _svc.CreatePurchase(pcId, "2024-01-10", null, null, null,
            new List<(string, int?, int?, double, int)> { ("Auditing", null, null, 1.0, 300) });

        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Auditor1");
        TestDbHelper.InsertAuditor(conn, audId);
        // Sessions totalling 5400 sec (1.5 h)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-15", 3600, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-16", 1800, seqInDay: 1);

        var list = _svc.GetAllPcs();
        var pc   = list.Single(p => p.PcId == pcId);
        Assert.Equal(-1800L, pc.RemainSec);  // 3600 − 5400
    }

    [Fact]
    public void GetAllPcs_RemainingSec_IgnoresFreeSessions()
    {
        var pcId = _svc.AddPcWithPerson("Alice", "A", "", "", "", "");
        _svc.CreatePurchase(pcId, "2024-01-10", null, null, null,
            new List<(string, int?, int?, double, int)> { ("Auditing", null, null, 2.0, 0) }); // 7200 sec

        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Auditor1");
        TestDbHelper.InsertAuditor(conn, audId);
        // Free session — should NOT reduce remaining time
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-15", 3600, isFree: true);

        var list = _svc.GetAllPcs();
        var pc   = list.Single(p => p.PcId == pcId);
        Assert.Equal(7200L, pc.RemainSec);  // free session excluded
    }

    // =========================================================================
    // GetPcDetail / UpdatePcDetail
    // =========================================================================

    [Fact]
    public void GetPcDetail_ReturnsNull_ForNonExistentPc()
    {
        var result = _svc.GetPcDetail(9999);
        Assert.Null(result);
    }

    [Fact]
    public void GetPcDetail_ReturnsCorrectFullName()
    {
        var id     = _svc.AddPcWithPerson("John", "Doe", "", "", "", "M");
        var detail = _svc.GetPcDetail(id)!;
        Assert.Equal("John Doe", detail.FullName);
    }

    [Fact]
    public void UpdatePcDetail_UpdatesBothPersonsAndPcsTable()
    {
        var id = _svc.AddPcWithPerson("Old", "Name", "050-1", "old@x.com", "1990-01-01", "M");
        _svc.UpdatePcDetail(id, "New", "Name", "Nick1", "054-9",
            "new@x.com", "Updated notes", "1990-06-15", "F");

        var d = _svc.GetPcDetail(id)!;
        Assert.Equal("New",           d.FirstName);
        Assert.Equal("Name",          d.LastName);
        Assert.Equal("054-9",         d.Phone);
        Assert.Equal("new@x.com",     d.Email);
        Assert.Equal("Updated notes", d.Notes);
        Assert.Equal("1990-06-15",    d.DateOfBirth);
        Assert.Equal("F",             d.Gender);
    }

    [Fact]
    public void UpdatePcDetail_CanClearOptionalFields()
    {
        var id = _svc.AddPcWithPerson("Alice", "Smith", "050-1111", "a@b.com", "1990-01-01", "F");
        _svc.UpdatePcDetail(id, "Alice", "Smith", "", "", "", "", "", "");

        var d = _svc.GetPcDetail(id)!;
        Assert.Equal("", d.Phone);
        Assert.Equal("", d.Email);
    }

    // =========================================================================
    // GetPcStats
    // =========================================================================

    [Fact]
    public void GetPcStats_AllZero_WhenNoSessionsOrPurchases()
    {
        var id    = _svc.AddPcWithPerson("Alice", "A", "", "", "", "");
        var stats = _svc.GetPcStats(id);

        Assert.Equal(0, stats.TotalSessions);
        Assert.Equal(0, stats.FreeSessions);
        Assert.Equal(0L, stats.UsedSec);
        Assert.Equal(0, stats.TotalHoursPurchased);
        Assert.Equal(0, stats.TotalAmountPaid);
        Assert.Null(stats.LastSessionDate);
    }

    [Fact]
    public void GetPcStats_CountsSessionsCorrectly()
    {
        var pcId = _svc.AddPcWithPerson("Alice", "A", "", "", "", "");

        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "AudX");
        TestDbHelper.InsertAuditor(conn, audId);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 1800, seqInDay: 1, isFree: true);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-12", 900,  seqInDay: 1);

        var stats = _svc.GetPcStats(pcId);
        Assert.Equal(3, stats.TotalSessions);
        Assert.Equal(1, stats.FreeSessions);
        Assert.Equal(3600L + 900L, stats.UsedSec);   // free session excluded from used time
        Assert.Equal("2024-01-12", stats.LastSessionDate);
    }

    [Fact]
    public void GetPcStats_SumsPurchasesCorrectly()
    {
        var pcId = _svc.AddPcWithPerson("Bob", "B", "", "", "", "");
        _svc.CreatePurchase(pcId, "2024-01-01", null, null, null,
            new List<(string, int?, int?, double, int)> { ("Auditing", null, null, 3.0, 900) });
        _svc.CreatePurchase(pcId, "2024-02-01", null, null, null,
            new List<(string, int?, int?, double, int)> { ("Auditing", null, null, 5.0, 1500) });

        var stats = _svc.GetPcStats(pcId);
        Assert.Equal(8,    stats.TotalHoursPurchased);
        Assert.Equal(2400, stats.TotalAmountPaid);
    }

    [Fact]
    public void GetPcStats_UsedSec_DoesNotCountFreeSessionTime()
    {
        var pcId = _svc.AddPcWithPerson("Carol", "C", "", "", "", "");

        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "AudZ");
        TestDbHelper.InsertAuditor(conn, audId);
        // Two sessions: one paid (3600), one free (7200)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-03-01", 3600, seqInDay: 1, isFree: false);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-03-02", 7200, seqInDay: 1, isFree: true);

        var stats = _svc.GetPcStats(pcId);
        Assert.Equal(3600L, stats.UsedSec);
    }

    // =========================================================================
    // GetPcSessions
    // =========================================================================

    [Fact]
    public void GetPcSessions_ReturnsEmpty_WhenNoSessions()
    {
        var pcId = _svc.AddPcWithPerson("Alice", "A", "", "", "", "");
        Assert.Empty(_svc.GetPcSessions(pcId));
    }

    [Fact]
    public void GetPcSessions_ReturnsAllSessions_OrderedByDateDesc()
    {
        var pcId = _svc.AddPcWithPerson("Alice", "A", "", "", "", "");

        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "AudT");
        TestDbHelper.InsertAuditor(conn, audId);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-15", 1800, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-12", 900,  seqInDay: 1);

        var sessions = _svc.GetPcSessions(pcId);
        Assert.Equal(3, sessions.Count);
        Assert.Equal("2024-01-15", sessions[0].Date);
        Assert.Equal("2024-01-12", sessions[1].Date);
        Assert.Equal("2024-01-10", sessions[2].Date);
    }

    [Fact]
    public void GetPcSessions_ReturnsAuditorFirstName()
    {
        var pcId = _svc.AddPcWithPerson("Alice", "A", "", "", "", "");

        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Tami", "Cohen");
        TestDbHelper.InsertAuditor(conn, audId);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600);

        var sessions = _svc.GetPcSessions(pcId);
        Assert.Equal("Tami", sessions[0].AuditorName);
    }

    // =========================================================================
    // Remaining hours – business-logic scenarios
    // =========================================================================

    [Fact]
    public void RemainingHours_MatchesTotalBoughtMinusUsed()
    {
        var pcId = _svc.AddPcWithPerson("Alice", "A", "", "", "", "");
        _svc.CreatePurchase(pcId, "2024-01-01", null, null, null,
            new List<(string, int?, int?, double, int)> { ("Auditing", null, null, 10.0, 3000) }); // 10 h = 36000 sec

        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "AudQ");
        TestDbHelper.InsertAuditor(conn, audId);
        // Use 3 h (10800 sec)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 5400, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 5400, seqInDay: 1);

        var pcs = _svc.GetAllPcs();
        var pc  = pcs.Single(p => p.PcId == pcId);
        Assert.Equal(36000L - 10800L, pc.RemainSec);  // 25200 sec = 7 h
    }

    [Fact]
    public void RemainingHours_MultiplePurchases_SummedCorrectly()
    {
        var pcId = _svc.AddPcWithPerson("Bob", "B", "", "", "", "");
        _svc.CreatePurchase(pcId, "2024-01-01", null, null, null,
            new List<(string, int?, int?, double, int)> { ("Auditing", null, null, 5.0, 1500) }); // 5 h
        _svc.CreatePurchase(pcId, "2024-02-01", null, null, null,
            new List<(string, int?, int?, double, int)> { ("Auditing", null, null, 5.0, 1500) }); // 5 h = total 10 h = 36000 sec

        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "AudW");
        TestDbHelper.InsertAuditor(conn, audId);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-15", 3600); // 1 h

        var pcs = _svc.GetAllPcs();
        var pc  = pcs.Single(p => p.PcId == pcId);
        Assert.Equal(32400L, pc.RemainSec);  // 36000 − 3600
    }

    [Fact]
    public void RemainingHours_LengthSecondsCountedTowardUsedTime()
    {
        // LengthSeconds counts as used time in the remaining-hours query
        var pcId = _svc.AddPcWithPerson("Carol", "C", "", "", "", "");
        _svc.CreatePurchase(pcId, "2024-01-01", null, null, null,
            new List<(string, int?, int?, double, int)> { ("Auditing", null, null, 2.0, 0) }); // 7200 sec

        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "AudV");
        TestDbHelper.InsertAuditor(conn, audId);
        // 3000 sec session with 600 admin
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3000, adminSec: 600);

        var pcs = _svc.GetAllPcs();
        var pc  = pcs.Single(p => p.PcId == pcId);
        // GetAllPcs uses SUM(LengthSeconds) only (not admin) for remaining balance
        Assert.Equal(7200L - 3000L, pc.RemainSec);
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
