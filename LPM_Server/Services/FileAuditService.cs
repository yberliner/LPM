using Microsoft.Data.Sqlite;

namespace LPM.Services;

public record FileAuditStats(int Count, string LastChange);

public record FileAuditEntry(
    int Id, int PcId, bool Solo, string FilePath, string Operation,
    long? SizeBytes, int? UserId, string? Username, string Context,
    string? Detail, string CreatedAt);

public class FileAuditService(IConfiguration config)
{
    private readonly string _cs = $"Data Source={config["Database:Path"] ?? "lifepower.db"}";

    /// <summary>Log a file mutation. Never throws — audit failure must not break file operations.</summary>
    public void Log(int pcId, bool solo, string filePath, string operation,
                    long? sizeBytes, int? userId, string? username,
                    string context, string? detail = null)
    {
        try
        {
            var normalizedPath = filePath.Replace('\\', '/');
            using var conn = new SqliteConnection(_cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO sys_file_audit
                (PcId, Solo, FilePath, Operation, SizeBytes, UserId, Username, Context, Detail)
                VALUES (@pcId, @solo, @path, @op, @size, @uid, @uname, @ctx, @detail)";
            cmd.Parameters.AddWithValue("@pcId", pcId);
            cmd.Parameters.AddWithValue("@solo", solo ? 1 : 0);
            cmd.Parameters.AddWithValue("@path", normalizedPath);
            cmd.Parameters.AddWithValue("@op", operation);
            cmd.Parameters.AddWithValue("@size", sizeBytes.HasValue ? sizeBytes.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@uid", userId.HasValue ? userId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@uname", (object?)username ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ctx", context);
            cmd.Parameters.AddWithValue("@detail", (object?)detail ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileAudit] Log failed: {ex.Message}");
        }
    }

    /// <summary>Get full history for a specific file in a PC folder.</summary>
    public List<FileAuditEntry> GetHistory(int pcId, string filePath, bool solo)
    {
        var normalizedPath = filePath.Replace('\\', '/');
        var entries = new List<FileAuditEntry>();
        try
        {
            using var conn = new SqliteConnection(_cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Id, PcId, Solo, FilePath, Operation, SizeBytes,
                UserId, Username, Context, Detail, CreatedAt
                FROM sys_file_audit
                WHERE PcId = @pcId AND Solo = @solo AND FilePath = @path
                ORDER BY Id DESC";
            cmd.Parameters.AddWithValue("@pcId", pcId);
            cmd.Parameters.AddWithValue("@solo", solo ? 1 : 0);
            cmd.Parameters.AddWithValue("@path", normalizedPath);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                entries.Add(new FileAuditEntry(
                    r.GetInt32(0), r.GetInt32(1), r.GetInt32(2) == 1,
                    r.GetString(3), r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetInt64(5),
                    r.IsDBNull(6) ? null : r.GetInt32(6),
                    r.IsDBNull(7) ? null : r.GetString(7),
                    r.GetString(8),
                    r.IsDBNull(9) ? null : r.GetString(9),
                    r.GetString(10)));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileAudit] GetHistory failed: {ex.Message}");
        }
        return entries;
    }

    /// <summary>Get all audited files for a PC with entry count and last change date.</summary>
    public Dictionary<string, FileAuditStats> GetAuditedFiles(int pcId, bool solo)
    {
        var result = new Dictionary<string, FileAuditStats>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqliteConnection(_cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT FilePath, COUNT(*) AS Cnt, MAX(CreatedAt) AS LastChange
                FROM sys_file_audit
                WHERE PcId = @pcId AND Solo = @solo
                GROUP BY FilePath
                ORDER BY FilePath";
            cmd.Parameters.AddWithValue("@pcId", pcId);
            cmd.Parameters.AddWithValue("@solo", solo ? 1 : 0);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result[r.GetString(0)] = new FileAuditStats(r.GetInt32(1), r.GetString(2));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileAudit] GetAuditedFiles failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>Delete audit entries older than the specified number of months.</summary>
    public int PruneOlderThan(int months = 6)
    {
        try
        {
            using var conn = new SqliteConnection(_cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM sys_file_audit WHERE CreatedAt < datetime('now', '-{months} months')";
            return cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileAudit] Prune failed: {ex.Message}");
            return 0;
        }
    }
}
