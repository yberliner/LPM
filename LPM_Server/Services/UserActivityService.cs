using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace LPM.Services;

public class UserActivityService
{
    private readonly string _connectionString;
    // In-memory circuit count only — intentionally resets on server restart
    private readonly ConcurrentDictionary<string, int> _circuits = new(StringComparer.OrdinalIgnoreCase);
    // In-memory last-interaction timestamp (mouse/keyboard/touch heartbeat, no DB)
    private readonly ConcurrentDictionary<string, DateTime> _lastInteraction = new(StringComparer.OrdinalIgnoreCase);

    public record ActivitySummary(string Username, string LastActivityAt, string LastAction, string LastKind, bool IsOnline);
    public record ActivityEntry(long Id, string ActivityAt, string Action, string Kind);

    public UserActivityService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    public void Initialize()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var purge = conn.CreateCommand();
            purge.CommandText = "DELETE FROM sys_activity_log WHERE ActivityAt < datetime('now', '-90 days')";
            var deleted = purge.ExecuteNonQuery();
            Console.WriteLine($"[ActivitySvc] Initialized — purged {deleted} old entries");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActivitySvc] Initialize error (table may not exist yet): {ex.Message}");
        }
    }

    public void RecordActivity(string username, string action, string kind)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO sys_activity_log (Username, ActivityAt, Action, Kind) VALUES (@u, @at, @a, @k)";
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@a", action);
                cmd.Parameters.AddWithValue("@k", kind);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { Console.WriteLine($"[ActivitySvc] RecordActivity error: {ex.Message}"); }
        });
    }

    public void RecordLogin(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        _circuits.AddOrUpdate(username, 1, (_, old) => old + 1);
        RecordActivity(username, "Logged in", "login");
    }

    public void RecordLogout(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        _circuits.AddOrUpdate(username, 0, (_, old) => Math.Max(0, old - 1));
        // Clean up entries with zero circuits to prevent unbounded dictionary growth
        if (_circuits.TryGetValue(username, out var count) && count <= 0)
        {
            _circuits.TryRemove(username, out _);
            _lastInteraction.TryRemove(username, out _);
        }
        RecordActivity(username, "Left the system", "logout");
    }

    public bool IsOnline(string username) =>
        _circuits.TryGetValue(username, out var c) && c > 0;

    public List<ActivitySummary> GetSummary()
    {
        var result = new List<ActivitySummary>();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            // SQLite returns Action/Kind from the row that has MAX(ActivityAt) within the group.
            // Group by LOWER(Username) so "Aviv" and "aviv" are treated as the same user.
            cmd.CommandText = @"
                SELECT LOWER(Username) AS Username, MAX(ActivityAt) AS LastActivityAt, Action, Kind
                FROM sys_activity_log
                GROUP BY LOWER(Username)
                ORDER BY MAX(ActivityAt) DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var uname = r.GetString(0);
                result.Add(new ActivitySummary(uname, r.GetString(1), r.GetString(2), r.GetString(3), IsOnline(uname)));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActivitySvc] GetSummary error: {ex.Message}");
        }
        return result;
    }

    public List<ActivityEntry> GetAuditLog(string username, int days)
    {
        var result = new List<ActivityEntry>();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.Parameters.AddWithValue("@u", username.ToLowerInvariant());
            if (days > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd HH:mm:ss");
                cmd.CommandText = "SELECT Id, ActivityAt, Action, Kind FROM sys_activity_log WHERE LOWER(Username)=@u AND ActivityAt >= @cutoff ORDER BY ActivityAt DESC LIMIT 500";
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
            }
            else
            {
                cmd.CommandText = "SELECT Id, ActivityAt, Action, Kind FROM sys_activity_log WHERE LOWER(Username)=@u ORDER BY ActivityAt DESC LIMIT 500";
            }
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(new ActivityEntry(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActivitySvc] GetAuditLog error: {ex.Message}");
        }
        return result;
    }

    // ── Interaction heartbeat (in-memory only) ─────────────────────────────

    public void RecordInteraction(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        _lastInteraction[username.ToLowerInvariant()] = DateTime.UtcNow;
    }

    public string LastActiveAgo(string username)
    {
        if (!_lastInteraction.TryGetValue(username.ToLowerInvariant(), out var dt))
            return "—";
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalSeconds < 45)  return "Active now";
        if (diff.TotalSeconds < 90)  return "Idle 1m";
        if (diff.TotalMinutes < 60)  return $"Idle {(int)diff.TotalMinutes}m";
        if (diff.TotalHours < 24)    return $"Idle {(int)diff.TotalHours}h {diff.Minutes}m";
        return $"Idle {(int)diff.TotalDays}d";
    }

    public bool IsRecentlyActive(string username)
    {
        if (!_lastInteraction.TryGetValue(username.ToLowerInvariant(), out var dt))
            return false;
        return (DateTime.UtcNow - dt).TotalSeconds < 45;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public static string TimeAgo(string activityAtUtc)
    {
        if (!DateTime.TryParseExact(activityAtUtc, "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            return activityAtUtc;
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalSeconds < 60)  return "just now";
        if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalHours < 24)    return $"{(int)diff.TotalHours}h {diff.Minutes}m ago";
        if (diff.TotalDays < 7)      return $"{(int)diff.TotalDays} day{((int)diff.TotalDays == 1 ? "" : "s")} ago";
        return dt.ToLocalTime().ToString("dd/MM/yyyy");
    }

    public static string FormatTimestamp(string activityAtUtc)
    {
        if (!DateTime.TryParseExact(activityAtUtc, "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            return activityAtUtc;
        return dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    }

    public static string FriendlyPageName(string url)
    {
        var path = new Uri(url, UriKind.RelativeOrAbsolute).IsAbsoluteUri
            ? new Uri(url).AbsolutePath
            : url.Split('?')[0];
        path = path.TrimEnd('/');
        return path.ToLowerInvariant() switch
        {
            "/home"             => "Home",
            "/admin/backup"     => "Backup",
            "/admin/diagnosis"  => "Diagnosis",
            "/admin/users"      => "User Management",
            "/pcfolder"         => "PC Folder",
            "/import"           => "Import",
            "/login"            => "Login",
            "/usersettings"     => "User Settings",
            _ when path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) => "Admin / " + path.Split('/').Last(),
            _ when path.Length > 1 => path.Split('/').Last(),
            _ => "Home"
        };
    }
}
