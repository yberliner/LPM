using LPM.Services;
using LPM.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LPM.Tests;

public class CompletionServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CompletionService _svc;

    public CompletionServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new CompletionService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    private SqliteConnection OpenConn() => new($"Data Source={_dbPath}");

    private int CreatePc(SqliteConnection conn, string firstName, string lastName = "")
    {
        var pid = TestDbHelper.InsertPerson(conn, firstName, lastName);
        TestDbHelper.InsertPC(conn, pid);
        return pid;
    }

    private int CreateAuditor(SqliteConnection conn, string firstName, string lastName = "")
    {
        var pid = TestDbHelper.InsertPerson(conn, firstName, lastName);
        TestDbHelper.InsertAuditor(conn, pid);
        return pid;
    }

    // ── GetByPc ───────────────────────────────────────────────────────────

    [Fact]
    public void GetByPc_EmptyWhenNone()
    {
        using var conn = OpenConn(); conn.Open();
        var pc = CreatePc(conn, "Alice");

        Assert.Empty(_svc.GetByPc(pc));
    }

    [Fact]
    public void GetByPc_ReturnsOnlyMatchingPc()
    {
        using var conn = OpenConn(); conn.Open();
        var pc1 = CreatePc(conn, "Alice");
        var pc2 = CreatePc(conn, "Bob");
        _svc.Add(pc1, "2025-01-10", "BA", null);
        _svc.Add(pc2, "2025-01-11", "MA", null);

        var result = _svc.GetByPc(pc1);

        Assert.Single(result);
        Assert.Equal(pc1, result[0].PcId);
    }

    [Fact]
    public void GetByPc_OrderedByCompleteDateDesc()
    {
        using var conn = OpenConn(); conn.Open();
        var pc = CreatePc(conn, "Alice");
        _svc.Add(pc, "2025-01-01", "BA",  null);
        _svc.Add(pc, "2025-03-01", "MA",  null);
        _svc.Add(pc, "2025-02-01", "PHD", null);

        var result = _svc.GetByPc(pc);

        Assert.Equal(3, result.Count);
        Assert.Equal("2025-03-01", result[0].CompleteDate);
        Assert.Equal("2025-02-01", result[1].CompleteDate);
        Assert.Equal("2025-01-01", result[2].CompleteDate);
    }

    // ── Add ───────────────────────────────────────────────────────────────

    [Fact]
    public void Add_ReturnsNewId_AndRowIsVisible()
    {
        using var conn = OpenConn(); conn.Open();
        var pc = CreatePc(conn, "Alice");

        var id = _svc.Add(pc, "2025-06-01", "BA", null);

        Assert.True(id > 0);
        var list = _svc.GetByPc(pc);
        Assert.Single(list);
        Assert.Equal(id,           list[0].Id);
        Assert.Equal("2025-06-01", list[0].CompleteDate);
        Assert.Equal("BA",         list[0].FinishedGrade);
    }

    [Fact]
    public void Add_WithNullOptionals_Works()
    {
        using var conn = OpenConn(); conn.Open();
        var pc = CreatePc(conn, "Alice");

        _svc.Add(pc, null, null, null);

        var list = _svc.GetByPc(pc);
        Assert.Single(list);
        Assert.Null(list[0].CompleteDate);
        Assert.Null(list[0].FinishedGrade);
        Assert.Null(list[0].AuditorId);
    }

    [Fact]
    public void Add_WithAuditorId_Persists()
    {
        using var conn = OpenConn(); conn.Open();
        var pc  = CreatePc(conn, "Alice");
        var aud = CreateAuditor(conn, "Bob");

        _svc.Add(pc, "2025-06-01", "MA", aud);

        Assert.Equal(aud, _svc.GetByPc(pc)[0].AuditorId);
    }

    // ── Update ────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ChangesFieldsCorrectly()
    {
        using var conn = OpenConn(); conn.Open();
        var pc  = CreatePc(conn, "Alice");
        var aud = CreateAuditor(conn, "Bob");
        var id  = _svc.Add(pc, "2025-01-01", "BA", null);

        _svc.Update(id, "2025-12-31", "PHD", aud);

        var row = _svc.GetByPc(pc)[0];
        Assert.Equal("2025-12-31", row.CompleteDate);
        Assert.Equal("PHD",        row.FinishedGrade);
        Assert.Equal(aud,          row.AuditorId);
    }

    [Fact]
    public void Update_CanClearOptionalFields()
    {
        using var conn = OpenConn(); conn.Open();
        var pc = CreatePc(conn, "Alice");
        var id = _svc.Add(pc, "2025-01-01", "BA", null);

        _svc.Update(id, null, null, null);

        var row = _svc.GetByPc(pc)[0];
        Assert.Null(row.CompleteDate);
        Assert.Null(row.FinishedGrade);
        Assert.Null(row.AuditorId);
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_RemovesRow()
    {
        using var conn = OpenConn(); conn.Open();
        var pc = CreatePc(conn, "Alice");
        var id = _svc.Add(pc, "2025-06-01", "BA", null);

        _svc.Delete(id);

        Assert.Empty(_svc.GetByPc(pc));
    }

    [Fact]
    public void Delete_OnlyRemovesTargetRow()
    {
        using var conn = OpenConn(); conn.Open();
        var pc  = CreatePc(conn, "Alice");
        var id1 = _svc.Add(pc, "2025-06-01", "BA", null);
        var id2 = _svc.Add(pc, "2025-07-01", "MA", null);

        _svc.Delete(id1);

        var list = _svc.GetByPc(pc);
        Assert.Single(list);
        Assert.Equal(id2, list[0].Id);
    }

    // ── GetActiveAuditors ─────────────────────────────────────────────────

    [Fact]
    public void GetActiveAuditors_ReturnsAuditorsAndCS()
    {
        using var conn = OpenConn(); conn.Open();
        var pAud  = TestDbHelper.InsertPerson(conn, "Alice", "A");
        TestDbHelper.InsertAuditor(conn, pAud, staffRole: "Auditor");
        var pCs   = TestDbHelper.InsertPerson(conn, "Bob", "B");
        TestDbHelper.InsertCS(conn, pCs);
        var pNone = TestDbHelper.InsertPerson(conn, "Charlie", "C");
        TestDbHelper.InsertCoreUser(conn, pNone, "charlie", "pass", staffRole: "None");

        var result = _svc.GetActiveAuditors();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, a => a.FullName.Contains("Alice"));
        Assert.Contains(result, a => a.FullName.Contains("Bob"));
        Assert.DoesNotContain(result, a => a.FullName.Contains("Charlie"));
    }

    [Fact]
    public void GetActiveAuditors_ExcludesInactiveUsers()
    {
        using var conn = OpenConn(); conn.Open();
        var pActive   = TestDbHelper.InsertPerson(conn, "Active");
        TestDbHelper.InsertAuditor(conn, pActive, isActive: true);
        var pInactive = TestDbHelper.InsertPerson(conn, "Inactive");
        TestDbHelper.InsertAuditor(conn, pInactive, isActive: false);

        var result = _svc.GetActiveAuditors();

        Assert.Single(result);
        Assert.Contains(result, a => a.FullName.Contains("Active"));
    }

    // ── GetAllNonSoloPcs ──────────────────────────────────────────────────

    [Fact]
    public void GetAllNonSoloPcs_ReturnsNonSoloPcs()
    {
        using var conn = OpenConn(); conn.Open();
        CreatePc(conn, "Alice");
        CreatePc(conn, "Bob");

        Assert.Equal(2, _svc.GetAllNonSoloPcs().Count);
    }

    [Fact]
    public void GetAllNonSoloPcs_ExcludesSoloPcs()
    {
        using var conn = OpenConn(); conn.Open();
        var pcNormal = CreatePc(conn, "Alice");
        var pcSolo   = CreatePc(conn, "SoloPc");
        TestDbHelper.InsertCoreUser(conn, pcSolo, "solo.pc", "pass", staffRole: "Solo");

        var result = _svc.GetAllNonSoloPcs();

        Assert.Single(result);
        Assert.Equal(pcNormal, result[0].PcId);
    }

    // ── GetForWeek ────────────────────────────────────────────────────────

    [Fact]
    public void GetForWeek_ReturnsCompletionsInRange()
    {
        using var conn = OpenConn(); conn.Open();
        var pc  = CreatePc(conn, "Alice");
        var aud = CreateAuditor(conn, "Bob");
        _svc.Add(pc, "2025-03-10", "BA",  aud);
        _svc.Add(pc, "2025-03-17", "MA",  aud); // outside
        _svc.Add(pc, "2025-03-08", "PHD", aud); // outside

        var result = _svc.GetForWeek(aud, new DateOnly(2025, 3, 9), new DateOnly(2025, 3, 15));

        Assert.Single(result);
        Assert.Equal("2025-03-10", result[0].CompleteDate);
    }

    [Fact]
    public void GetForWeek_FiltersToSpecificAuditor()
    {
        using var conn = OpenConn(); conn.Open();
        var pc   = CreatePc(conn, "Alice");
        var aud1 = CreateAuditor(conn, "Aud1");
        var aud2 = CreateAuditor(conn, "Aud2");
        _svc.Add(pc, "2025-03-10", "BA", aud1);
        _svc.Add(pc, "2025-03-11", "MA", aud2);

        var result = _svc.GetForWeek(aud1, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        Assert.Single(result);
        Assert.Equal("2025-03-10", result[0].CompleteDate);
    }

    // ── GetByPcWithAuditorName ────────────────────────────────────────────

    [Fact]
    public void GetByPcWithAuditorName_JoinsAuditorName()
    {
        using var conn = OpenConn(); conn.Open();
        var pc  = CreatePc(conn, "Alice");
        var aud = CreateAuditor(conn, "Bob", "Smith");
        _svc.Add(pc, "2025-06-01", "BA", aud);

        var result = _svc.GetByPcWithAuditorName(pc);

        Assert.Single(result);
        Assert.NotNull(result[0].AuditorName);
        Assert.Contains("Bob", result[0].AuditorName);
    }

    [Fact]
    public void GetByPcWithAuditorName_NullAuditorId_ReturnsNullName()
    {
        using var conn = OpenConn(); conn.Open();
        var pc = CreatePc(conn, "Alice");
        _svc.Add(pc, "2025-06-01", "BA", null);

        var result = _svc.GetByPcWithAuditorName(pc);

        Assert.Single(result);
        Assert.Null(result[0].AuditorName);
    }

    // ── GetAllForRange ────────────────────────────────────────────────────

    [Fact]
    public void GetAllForRange_ReturnsCompletionsInRange()
    {
        using var conn = OpenConn(); conn.Open();
        var pc1 = CreatePc(conn, "Alice");
        var pc2 = CreatePc(conn, "Bob");
        _svc.Add(pc1, "2025-03-10", "BA",  null);
        _svc.Add(pc2, "2025-03-15", "MA",  null);
        _svc.Add(pc1, "2025-04-01", "PHD", null); // outside

        var result = _svc.GetAllForRange(new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, r => r.CompleteDate == "2025-04-01");
    }

    [Fact]
    public void GetAllForRange_IncludesAuditorName()
    {
        using var conn = OpenConn(); conn.Open();
        var pc  = CreatePc(conn, "Alice");
        var aud = CreateAuditor(conn, "Carol", "Jones");
        _svc.Add(pc, "2025-03-10", "BA", aud);

        var result = _svc.GetAllForRange(new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        Assert.Single(result);
        Assert.Contains("Carol", result[0].AuditorName);
    }

    [Fact]
    public void GetAllForRange_EmptyWhenNoneInRange()
    {
        using var conn = OpenConn(); conn.Open();
        var pc = CreatePc(conn, "Alice");
        _svc.Add(pc, "2025-01-01", "BA", null);

        var result = _svc.GetAllForRange(new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        Assert.Empty(result);
    }
}
