using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace LPM.Services;

/// <summary>
/// Singleton-backed cache for the per-table snapshots shown on the Diagnosis "Database"
/// tab. Two reasons it exists:
///
///   1. <b>Cap memory at the SQL layer.</b> Loading every row of every table built a 50–80 MB
///      <c>List&lt;List&lt;string&gt;&gt;</c> per circuit. The cache always queries with
///      LIMIT 200 (the same number the UI renders) and stores a separate COUNT(*) so the UI
///      can show "200 of 19,655". The 50-MB-per-tab problem goes away.
///
///   2. <b>Deduplicate across tabs.</b> If three admins open Diagnosis at once, they all
///      share the same snapshot in this singleton instead of each loading their own copy.
///
/// Cache key includes the limit so a one-off "show 1000" query doesn't poison the default
/// 200-row entries everyone else uses. TTL is 30 s — short enough that admins see fresh
/// data within a sane window, long enough that opening multiple tabs in a session doesn't
/// re-query for every one.
/// </summary>
public sealed class DiagnosisCacheService
{
    public record TableSnapshot(
        string         Name,
        string         Icon,
        List<string>   Columns,
        List<List<string>> Rows,
        int            TotalRows,
        string         OrderBy,
        string[]?      SkipCols,
        int            Limit);

    private record CachedEntry(DateTime CachedAt, TableSnapshot Data);

    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<(string Table, string OrderBy, int Limit), CachedEntry> _cache = new();
    private static readonly TimeSpan TTL = TimeSpan.FromSeconds(30);

    public DiagnosisCacheService(IConfiguration config) { _config = config; }

    private string DbCs() => $"Data Source={_config["Database:Path"] ?? "lifepower.db"}";

    /// <summary>
    /// Return a cached snapshot if &lt; TTL old, else query fresh and cache. Each call
    /// runs at most one SQL round-trip per cache miss, plus a tiny COUNT(*) for the
    /// total-rows badge.
    /// </summary>
    public TableSnapshot GetOrLoad(string table, string icon, string orderBy, string[]? skipCols, int limit)
    {
        var key = (table, orderBy, limit);
        if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.CachedAt < TTL)
            return entry.Data;

        var fresh = LoadFromDb(table, icon, orderBy, skipCols, limit);
        _cache[key] = new CachedEntry(DateTime.UtcNow, fresh);
        return fresh;
    }

    /// <summary>Drop every cache entry for <paramref name="table"/>. Call after admin edits.</summary>
    public void Invalidate(string table)
    {
        var matches = _cache.Keys.Where(k => k.Table.Equals(table, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var k in matches) _cache.TryRemove(k, out _);
    }

    /// <summary>Drop the entire cache. Cheap fallback when an edit might have touched
    /// FK-related tables and you don't want to enumerate which.</summary>
    public void InvalidateAll() => _cache.Clear();

    private TableSnapshot LoadFromDb(string table, string icon, string orderBy, string[]? skipCols, int limit)
    {
        var skip = new HashSet<string>(skipCols ?? [], StringComparer.OrdinalIgnoreCase);
        var cols = new List<string>();
        var rows = new List<List<string>>();
        int total = 0;
        try
        {
            using var conn = new SqliteConnection(DbCs());
            conn.Open();

            // 1) Fast COUNT(*) — SQLite reads the index, doesn't materialise rows.
            try
            {
                using var ccmd = conn.CreateCommand();
                ccmd.CommandText = $"SELECT COUNT(*) FROM {table}";
                var c = ccmd.ExecuteScalar();
                total = c == null || c == DBNull.Value ? 0 : Convert.ToInt32(c);
            }
            catch { /* count failure is non-fatal — we'll just show "?"" in the UI */ }

            // 2) Bounded SELECT. ORDER BY + LIMIT keeps us at ≤ N rows regardless of table size.
            using var cmd = conn.CreateCommand();
            cmd.CommandText = limit > 0
                ? $"SELECT * FROM {table} {orderBy} LIMIT {limit}"
                : $"SELECT * FROM {table} {orderBy}";
            using var r = cmd.ExecuteReader();
            for (int i = 0; i < r.FieldCount; i++)
                if (!skip.Contains(r.GetName(i))) cols.Add(r.GetName(i));
            while (r.Read())
            {
                var row = new List<string>();
                for (int i = 0; i < r.FieldCount; i++)
                    if (!skip.Contains(r.GetName(i)))
                    {
                        var val = r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
                        row.Add(MaskValue(r.GetName(i), val));
                    }
                rows.Add(row);
            }
        }
        catch (Exception ex) { cols = [$"Error: {ex.Message}"]; }

        return new TableSnapshot(table, icon, cols, rows, total, orderBy, skipCols, limit);
    }

    // Mirrors Diagnosis.razor's column-mask list. Kept here so the cache returns
    // already-masked values (the UI never sees raw secrets, which would also be wrong
    // to leak into the diff stream / log output).
    private static readonly HashSet<string> MaskCols = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "PasswordHash", "Token", "TokenHash", "ApiKey", "Secret",
        "TrustToken", "MagicTokenHash", "PrivateKey", "EncryptedPrivateKey", "Credential",
    };

    private static string MaskValue(string col, string val)
    {
        if (string.IsNullOrEmpty(val)) return val;
        if (MaskCols.Contains(col)) return "••••••";
        return val;
    }
}
