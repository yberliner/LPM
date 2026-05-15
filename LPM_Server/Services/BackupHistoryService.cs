using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net;

namespace LPM.Services;

public record BackupRun(
    int Id,
    string Username,
    string? ClientHost,
    string? ClientIp,
    string? UserAgent,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    DateTime LastHeartbeatUtc,
    string Status,
    string? Phase,
    int FilesDone,
    int FilesTotal,
    int FilesSkipped,
    int FilesErrors,
    string? CurrentFile,
    string? ErrorMessage);

public class BackupHistoryService
{
    public const string StatusRunning              = "running";
    public const string StatusCompleted            = "completed";
    public const string StatusCancelledByUser      = "cancelled_by_user";
    public const string StatusFailed               = "failed";
    public const string StatusInterruptedDisconnect = "interrupted_disconnect";
    public const string StatusInterruptedServerStop = "interrupted_server_stop";

    private readonly string _connectionString;

    public BackupHistoryService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    private static string ToIso(DateTime utc) =>
        utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTime ParseIso(string s)
    {
        // RoundtripKind preserves the Kind from the input — our "o" format always
        // includes a 'Z' suffix so the parser tags the result as UTC. Do NOT combine
        // with AssumeUniversal — those flags are mutually exclusive and .NET throws.
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();
        return DateTime.UtcNow;
    }

    /// <summary>Insert a new "running" row and return its Id.</summary>
    public int StartRun(string username, string? clientIp, string? clientHost, string? userAgent)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var now = ToIso(DateTime.UtcNow);
        cmd.CommandText = @"
            INSERT INTO sys_backup_runs
              (Username, ClientHost, ClientIp, UserAgent, StartedAt, LastHeartbeatAt, Status)
            VALUES
              (@u, @h, @ip, @ua, @t, @t, @s);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@u",  username ?? "");
        cmd.Parameters.AddWithValue("@h",  (object?)clientHost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ip", (object?)clientIp   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ua", (object?)userAgent  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t",  now);
        cmd.Parameters.AddWithValue("@s",  StatusRunning);
        var id = Convert.ToInt32((long)(cmd.ExecuteScalar() ?? 0L));
        return id;
    }

    /// <summary>Update progress columns. No-op if the row is no longer running.</summary>
    public void Heartbeat(int id, string? phase, int filesDone, int filesTotal,
                          int filesSkipped, int filesErrors, string? currentFile)
    {
        if (id <= 0) return;
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE sys_backup_runs SET
                    LastHeartbeatAt = @t,
                    Phase           = @ph,
                    FilesDone       = @fd,
                    FilesTotal      = @ft,
                    FilesSkipped    = @fs,
                    FilesErrors     = @fe,
                    CurrentFile     = @cf
                WHERE Id = @id AND Status = @running;";
            cmd.Parameters.AddWithValue("@t",       ToIso(DateTime.UtcNow));
            cmd.Parameters.AddWithValue("@ph",      (object?)phase       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fd",      filesDone);
            cmd.Parameters.AddWithValue("@ft",      filesTotal);
            cmd.Parameters.AddWithValue("@fs",      filesSkipped);
            cmd.Parameters.AddWithValue("@fe",      filesErrors);
            cmd.Parameters.AddWithValue("@cf",      (object?)currentFile ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id",      id);
            cmd.Parameters.AddWithValue("@running", StatusRunning);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BackupHistory] Heartbeat failed for run {id}: {ex.Message}");
        }
    }

    /// <summary>
    /// Backfill the ClientHost (reverse-DNS) column for a row that's already started.
    /// Independent of Status — we want to backfill the host even on rows that have
    /// finished or been cancelled, so the audit trail is complete.
    /// </summary>
    public void UpdateClientHost(int id, string? host)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(host)) return;
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE sys_backup_runs SET ClientHost = @h WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@h",  host);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BackupHistory] UpdateClientHost failed for run {id}: {ex.Message}");
        }
    }

    /// <summary>Finalize the run with a terminal status. No-op if already finalized.</summary>
    public void Finish(int id, string status, string? errorMessage)
    {
        if (id <= 0) return;
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE sys_backup_runs SET
                    EndedAt      = @t,
                    Status       = @newStatus,
                    ErrorMessage = COALESCE(@err, ErrorMessage)
                WHERE Id = @id AND Status = @running;";
            cmd.Parameters.AddWithValue("@t",         ToIso(DateTime.UtcNow));
            cmd.Parameters.AddWithValue("@newStatus", status);
            cmd.Parameters.AddWithValue("@err",       (object?)errorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id",        id);
            cmd.Parameters.AddWithValue("@running",   StatusRunning);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BackupHistory] Finish failed for run {id}: {ex.Message}");
        }
    }

    /// <summary>
    /// Called at startup AND from the ApplicationStopping hook. Any rows still marked
    /// "running" are by definition orphans (no process can be writing to them) and get
    /// marked as interrupted_server_stop, with EndedAt set to their last heartbeat.
    /// </summary>
    public int MarkZombiesAsInterrupted()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE sys_backup_runs SET
                    Status  = @newStatus,
                    EndedAt = COALESCE(LastHeartbeatAt, @nowIso)
                WHERE Status = @running;";
            cmd.Parameters.AddWithValue("@newStatus", StatusInterruptedServerStop);
            cmd.Parameters.AddWithValue("@nowIso",    ToIso(DateTime.UtcNow));
            cmd.Parameters.AddWithValue("@running",   StatusRunning);
            var n = cmd.ExecuteNonQuery();
            if (n > 0)
                Console.WriteLine($"[BackupHistory] Marked {n} zombie run(s) as interrupted_server_stop");
            return n;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BackupHistory] MarkZombiesAsInterrupted failed: {ex.Message}");
            return 0;
        }
    }

    public List<BackupRun> GetAll(int? limit = null)
    {
        var list = new List<BackupRun>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Username, ClientHost, ClientIp, UserAgent,
                   StartedAt, EndedAt, LastHeartbeatAt, Status, Phase,
                   FilesDone, FilesTotal, FilesSkipped, FilesErrors,
                   CurrentFile, ErrorMessage
              FROM sys_backup_runs
             ORDER BY StartedAt DESC, Id DESC" + (limit.HasValue ? " LIMIT @lim" : "") + ";";
        if (limit.HasValue) cmd.Parameters.AddWithValue("@lim", limit.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new BackupRun(
                Id:                r.GetInt32(0),
                Username:          r.IsDBNull(1) ? "" : r.GetString(1),
                ClientHost:        r.IsDBNull(2) ? null : r.GetString(2),
                ClientIp:          r.IsDBNull(3) ? null : r.GetString(3),
                UserAgent:         r.IsDBNull(4) ? null : r.GetString(4),
                StartedAtUtc:      ParseIso(r.GetString(5)),
                EndedAtUtc:        r.IsDBNull(6) ? null : ParseIso(r.GetString(6)),
                LastHeartbeatUtc:  ParseIso(r.GetString(7)),
                Status:            r.GetString(8),
                Phase:             r.IsDBNull(9)  ? null : r.GetString(9),
                FilesDone:         r.GetInt32(10),
                FilesTotal:        r.GetInt32(11),
                FilesSkipped:      r.GetInt32(12),
                FilesErrors:       r.GetInt32(13),
                CurrentFile:       r.IsDBNull(14) ? null : r.GetString(14),
                ErrorMessage:      r.IsDBNull(15) ? null : r.GetString(15)));
        }
        return list;
    }

    /// <summary>Best-effort reverse DNS with a hard 500ms timeout. Returns null on failure.</summary>
    public static async Task<string?> TryReverseDnsAsync(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        try
        {
            if (!IPAddress.TryParse(ip, out var addr)) return null;
            if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();

            var task = Dns.GetHostEntryAsync(addr);
            var winner = await Task.WhenAny(task, Task.Delay(500));
            if (winner != task)
            {
                // Timeout: observe the abandoned task to prevent UnobservedTaskException
                // if it later faults — fire-and-forget continuation that swallows.
                _ = task.ContinueWith(t => { _ = t.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return null;
            }
            var host = (await task).HostName;
            if (string.IsNullOrWhiteSpace(host)) return null;
            // GetHostEntry sometimes echoes the IP back when no PTR record exists
            if (string.Equals(host, ip, StringComparison.Ordinal)) return null;
            return host;
        }
        catch
        {
            return null;
        }
    }
}
