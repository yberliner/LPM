using LPM.Services;
using LPM.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LPM.Tests;

public class FileAuditServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FileAuditService _svc;

    public FileAuditServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc = new FileAuditService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    [Fact]
    public void Log_InsertsRow()
    {
        _svc.Log(1, false, "Front_Cover/file.pdf", "create", 1024, null, null, "Import");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var count = TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM sys_file_audit");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Log_NormalizesBackslashes()
    {
        _svc.Log(1, false, "Front_Cover\\sub\\file.pdf", "create", 512, null, null, "Upload");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT FilePath FROM sys_file_audit LIMIT 1";
        var path = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("Front_Cover/sub/file.pdf", path);
    }

    [Fact]
    public void GetHistory_ReturnsEntriesForFile()
    {
        _svc.Log(1, false, "Front_Cover/test.pdf", "create", 1000, null, "admin", "Import");
        _svc.Log(1, false, "Front_Cover/test.pdf", "overwrite", 2000, null, "admin", "Upload");
        _svc.Log(1, false, "Front_Cover/other.pdf", "create", 500, null, null, "Import");

        var history = _svc.GetHistory(1, "Front_Cover/test.pdf", false);
        Assert.Equal(2, history.Count);
        Assert.Equal("overwrite", history[0].Operation); // newest first
        Assert.Equal("create", history[1].Operation);
    }

    [Fact]
    public void GetHistory_FiltersBySolo()
    {
        _svc.Log(1, false, "Front_Cover/test.pdf", "create", 1000, null, null, "Import");
        _svc.Log(1, true, "Front_Cover/test.pdf", "create", 1000, null, null, "Import");

        var regular = _svc.GetHistory(1, "Front_Cover/test.pdf", false);
        var solo = _svc.GetHistory(1, "Front_Cover/test.pdf", true);
        Assert.Single(regular);
        Assert.Single(solo);
    }

    [Fact]
    public void GetAuditedFiles_ReturnsStatsPerFile()
    {
        _svc.Log(1, false, "Front_Cover/a.pdf", "create", 100, null, null, "Import");
        _svc.Log(1, false, "Front_Cover/a.pdf", "overwrite", 200, null, null, "Upload");
        _svc.Log(1, false, "Front_Cover/b.pdf", "create", 300, null, null, "Import");

        var stats = _svc.GetAuditedFiles(1, false);
        Assert.Equal(2, stats.Count);
        Assert.Equal(2, stats["Front_Cover/a.pdf"].Count);
        Assert.Equal(1, stats["Front_Cover/b.pdf"].Count);
    }

    [Fact]
    public void GetAuditedFiles_ReturnsLastChangeDate()
    {
        _svc.Log(1, false, "Front_Cover/x.pdf", "create", 100, null, null, "Import");
        _svc.Log(1, false, "Front_Cover/x.pdf", "shrink", 50, null, null, "PdfShrink");

        var stats = _svc.GetAuditedFiles(1, false);
        Assert.NotNull(stats["Front_Cover/x.pdf"].LastChange);
        Assert.NotEmpty(stats["Front_Cover/x.pdf"].LastChange);
    }

    [Fact]
    public void PruneOlderThan_DeletesOldEntries()
    {
        // Insert a row and backdate it
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        TestDbHelper.Exec(conn, @"
            INSERT INTO sys_file_audit (PcId, Solo, FilePath, Operation, Context, CreatedAt)
            VALUES (1, 0, 'old.pdf', 'create', 'Import', datetime('now', '-8 months'))");
        TestDbHelper.Exec(conn, @"
            INSERT INTO sys_file_audit (PcId, Solo, FilePath, Operation, Context, CreatedAt)
            VALUES (1, 0, 'new.pdf', 'create', 'Import', datetime('now'))");

        var deleted = _svc.PruneOlderThan(6);
        Assert.Equal(1, deleted);

        var remaining = TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM sys_file_audit");
        Assert.Equal(1, remaining);
    }

    [Fact]
    public void Log_StoresAllFields()
    {
        _svc.Log(42, true, "WorkSheets/session.pdf", "shrink", 5000, 7, "testuser", "PdfShrink", "100KB → 50KB");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PcId, Solo, FilePath, Operation, SizeBytes, UserId, Username, Context, Detail FROM sys_file_audit LIMIT 1";
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(42, r.GetInt32(0));
        Assert.Equal(1, r.GetInt32(1));  // solo
        Assert.Equal("WorkSheets/session.pdf", r.GetString(2));
        Assert.Equal("shrink", r.GetString(3));
        Assert.Equal(5000L, r.GetInt64(4));
        Assert.Equal(7, r.GetInt32(5));
        Assert.Equal("testuser", r.GetString(6));
        Assert.Equal("PdfShrink", r.GetString(7));
        Assert.Equal("100KB → 50KB", r.GetString(8));
    }

    [Fact]
    public void Log_NullableFieldsStoredCorrectly()
    {
        _svc.Log(1, false, "test.pdf", "delete", null, null, null, "ContextMenu");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT SizeBytes, UserId, Username, Detail FROM sys_file_audit LIMIT 1";
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.True(r.IsDBNull(0)); // SizeBytes
        Assert.True(r.IsDBNull(1)); // UserId
        Assert.True(r.IsDBNull(2)); // Username
        Assert.True(r.IsDBNull(3)); // Detail
    }

    [Fact]
    public void GetHistory_EmptyForUnknownFile()
    {
        var history = _svc.GetHistory(999, "nonexistent.pdf", false);
        Assert.Empty(history);
    }

    [Fact]
    public void GetAuditedFiles_EmptyForNewPc()
    {
        var stats = _svc.GetAuditedFiles(999, false);
        Assert.Empty(stats);
    }
}
