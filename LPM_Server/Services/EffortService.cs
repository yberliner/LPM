using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record EffortCategory(int CategoryId, string Code, string Label, int SortOrder);

public record EffortStaffUser(int UserId, int PersonId, string Username, string FullName, string StaffRole);

public record EffortPcOption(int PcId, string FullName);

public record EffortEntryRow(
    int EntryId,
    int PerformedByUserId,
    string PerformedByName,
    int PcId,
    string PcName,
    string EffortDate,
    int LengthSeconds,
    int CategoryId,
    string CategoryLabel,
    string? Notes,
    int CreatedByUserId,
    string CreatedByName,
    string CreatedAt,
    string? UpdatedAt,
    int? UpdatedByUserId);

public record EffortFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo   = null,
    int? PerformedByUserId = null,
    int? PcId = null,
    int? CategoryId = null,
    int? CreatedByUserId = null);

public class EffortService
{
    private readonly string _connectionString;
    private List<EffortCategory>? _categoriesCache;
    private readonly object _cacheLock = new();

    public EffortService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    // ── Dropdown helpers ─────────────────────────────────────────

    public List<EffortCategory> GetCategories()
    {
        lock (_cacheLock)
        {
            if (_categoriesCache != null) return _categoriesCache;
        }

        var list = new List<EffortCategory>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CategoryId, Code, Label, SortOrder
            FROM lkp_effort_categories
            WHERE IsActive = 1
            ORDER BY SortOrder, Label";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new EffortCategory(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));

        lock (_cacheLock) { _categoriesCache = list; }
        return list;
    }

    public void InvalidateCategoriesCache()
    {
        lock (_cacheLock) { _categoriesCache = null; }
    }

    public List<EffortStaffUser> GetStaffUsers()
    {
        var list = new List<EffortStaffUser>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.Id, u.PersonId, u.Username,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   u.StaffRole
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.IsActive = 1
              AND u.StaffRole NOT IN ('None','Solo')
            ORDER BY FullName COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new EffortStaffUser(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }

    /// <summary>Look up core_users.Id by username (used to default "Performed By" to current user).</summary>
    public int? GetCoreUserIdByUsername(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM core_users WHERE LOWER(Username) = LOWER(@u) AND IsActive = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : null;
    }

    public List<EffortPcOption> GetAllPcs()
    {
        var list = new List<EffortPcOption>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName
            FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            ORDER BY FullName COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new EffortPcOption(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    // ── CRUD ─────────────────────────────────────────────────────

    public int AddEntry(int performedByUserId, int pcId, DateOnly effortDate,
                        int lengthSeconds, int categoryId, string? notes, int createdByUserId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sys_effort_entries
                (PerformedByUserId, PcId, EffortDate, LengthSeconds, CategoryId, Notes, CreatedByUserId)
            VALUES
                (@perf, @pc, @d, @len, @cat, @notes, @createdBy);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@perf",     performedByUserId);
        cmd.Parameters.AddWithValue("@pc",       pcId);
        cmd.Parameters.AddWithValue("@d",        effortDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@len",      lengthSeconds);
        cmd.Parameters.AddWithValue("@cat",      categoryId);
        cmd.Parameters.AddWithValue("@notes",    (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy", createdByUserId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateEntry(int entryId, int performedByUserId, int pcId, DateOnly effortDate,
                            int lengthSeconds, int categoryId, string? notes, int updatedByUserId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sys_effort_entries
               SET PerformedByUserId = @perf,
                   PcId              = @pc,
                   EffortDate        = @d,
                   LengthSeconds     = @len,
                   CategoryId        = @cat,
                   Notes             = @notes,
                   UpdatedAt         = datetime('now'),
                   UpdatedByUserId   = @upd
             WHERE EntryId = @id";
        cmd.Parameters.AddWithValue("@id",    entryId);
        cmd.Parameters.AddWithValue("@perf",  performedByUserId);
        cmd.Parameters.AddWithValue("@pc",    pcId);
        cmd.Parameters.AddWithValue("@d",     effortDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@len",   lengthSeconds);
        cmd.Parameters.AddWithValue("@cat",   categoryId);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@upd",   updatedByUserId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteEntry(int entryId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sys_effort_entries WHERE EntryId = @id";
        cmd.Parameters.AddWithValue("@id", entryId);
        cmd.ExecuteNonQuery();
    }

    public EffortEntryRow? GetById(int entryId)
    {
        var filter = new EffortFilter();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelectSql() + " WHERE e.EntryId = @id";
        cmd.Parameters.AddWithValue("@id", entryId);
        using var r = cmd.ExecuteReader();
        if (r.Read()) return ReadRow(r);
        return null;
    }

    public List<EffortEntryRow> Query(EffortFilter filter)
    {
        var list = new List<EffortEntryRow>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        var where = new List<string>();
        if (filter.DateFrom.HasValue) { where.Add("e.EffortDate >= @from"); cmd.Parameters.AddWithValue("@from", filter.DateFrom.Value.ToString("yyyy-MM-dd")); }
        if (filter.DateTo.HasValue)   { where.Add("e.EffortDate <= @to");   cmd.Parameters.AddWithValue("@to",   filter.DateTo.Value.ToString("yyyy-MM-dd")); }
        if (filter.PerformedByUserId.HasValue) { where.Add("e.PerformedByUserId = @perf"); cmd.Parameters.AddWithValue("@perf", filter.PerformedByUserId.Value); }
        if (filter.PcId.HasValue) { where.Add("e.PcId = @pc"); cmd.Parameters.AddWithValue("@pc", filter.PcId.Value); }
        if (filter.CategoryId.HasValue) { where.Add("e.CategoryId = @cat"); cmd.Parameters.AddWithValue("@cat", filter.CategoryId.Value); }
        if (filter.CreatedByUserId.HasValue) { where.Add("(e.PerformedByUserId = @scope OR e.CreatedByUserId = @scope)"); cmd.Parameters.AddWithValue("@scope", filter.CreatedByUserId.Value); }

        cmd.CommandText = BaseSelectSql()
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY e.EffortDate DESC, e.EntryId DESC";

        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadRow(r));
        return list;
    }

    private static string BaseSelectSql() => @"
        SELECT e.EntryId, e.PerformedByUserId,
               TRIM(pp.FirstName || ' ' || COALESCE(NULLIF(pp.LastName,''),'')) AS PerformedName,
               e.PcId,
               TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''),'')) AS PcName,
               e.EffortDate, e.LengthSeconds, e.CategoryId, c.Label,
               e.Notes, e.CreatedByUserId,
               TRIM(cp.FirstName || ' ' || COALESCE(NULLIF(cp.LastName,''),'')) AS CreatedName,
               e.CreatedAt, e.UpdatedAt, e.UpdatedByUserId
        FROM sys_effort_entries e
        JOIN core_users perfU ON perfU.Id = e.PerformedByUserId
        JOIN core_persons pp ON pp.PersonId = perfU.PersonId
        JOIN core_persons pc ON pc.PersonId = e.PcId
        JOIN lkp_effort_categories c ON c.CategoryId = e.CategoryId
        JOIN core_users createdU ON createdU.Id = e.CreatedByUserId
        JOIN core_persons cp ON cp.PersonId = createdU.PersonId";

    private static EffortEntryRow ReadRow(SqliteDataReader r) => new(
        r.GetInt32(0), r.GetInt32(1), r.GetString(2),
        r.GetInt32(3), r.GetString(4),
        r.GetString(5), r.GetInt32(6),
        r.GetInt32(7), r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9),
        r.GetInt32(10), r.GetString(11),
        r.GetString(12),
        r.IsDBNull(13) ? null : r.GetString(13),
        r.IsDBNull(14) ? null : r.GetInt32(14));

    // ── Dashboard feeder: Days × PCs grid for one user ───────────

    /// <summary>
    /// Returns (pcIds, grid keyed by (pcId, dayIdx 0..6)) for one user's effort in a week.
    /// Week start = Thursday (matching the other weekly grids).
    /// </summary>
    public (List<(int PcId, string FullName)> Pcs, Dictionary<(int pcId, int dayIdx), int> Grid)
        GetWeekEffortGrid(int performedByUserId, DateOnly weekStart)
    {
        var dates = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var startStr = weekStart.ToString("yyyy-MM-dd");
        var endStr   = weekStart.AddDays(6).ToString("yyyy-MM-dd");

        var grid = new Dictionary<(int pcId, int dayIdx), int>();
        var pcNames = new Dictionary<int, string>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT e.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS FullName,
                   e.EffortDate, SUM(e.LengthSeconds)
            FROM sys_effort_entries e
            JOIN core_persons p ON p.PersonId = e.PcId
            WHERE e.PerformedByUserId = @uid
              AND e.EffortDate BETWEEN @s AND @e
            GROUP BY e.PcId, e.EffortDate";
        cmd.Parameters.AddWithValue("@uid", performedByUserId);
        cmd.Parameters.AddWithValue("@s",   startStr);
        cmd.Parameters.AddWithValue("@e",   endStr);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int pcId = r.GetInt32(0);
            pcNames[pcId] = r.GetString(1);
            var d = DateOnly.Parse(r.GetString(2));
            int dayIdx = dates.IndexOf(d);
            if (dayIdx < 0) continue;
            grid[(pcId, dayIdx)] = grid.GetValueOrDefault((pcId, dayIdx)) + r.GetInt32(3);
        }

        var pcs = pcNames
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
        return (pcs, grid);
    }

    /// <summary>Same as GetWeekEffortGrid but keyed by PersonId (matches the rest of the dashboard, which uses PersonId).</summary>
    public (List<(int PcId, string FullName)> Pcs, Dictionary<(int pcId, int dayIdx), int> Grid)
        GetWeekEffortGridByPerson(int personId, DateOnly weekStart)
    {
        var dates = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var startStr = weekStart.ToString("yyyy-MM-dd");
        var endStr   = weekStart.AddDays(6).ToString("yyyy-MM-dd");

        var grid = new Dictionary<(int pcId, int dayIdx), int>();
        var pcNames = new Dictionary<int, string>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT e.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS FullName,
                   e.EffortDate, SUM(e.LengthSeconds)
            FROM sys_effort_entries e
            JOIN core_persons p ON p.PersonId = e.PcId
            JOIN core_users   u ON u.Id       = e.PerformedByUserId
            WHERE u.PersonId = @pid
              AND e.EffortDate BETWEEN @s AND @e
            GROUP BY e.PcId, e.EffortDate";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@s",   startStr);
        cmd.Parameters.AddWithValue("@e",   endStr);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int pcId = r.GetInt32(0);
            pcNames[pcId] = r.GetString(1);
            var d = DateOnly.Parse(r.GetString(2));
            int dayIdx = dates.IndexOf(d);
            if (dayIdx < 0) continue;
            grid[(pcId, dayIdx)] = grid.GetValueOrDefault((pcId, dayIdx)) + r.GetInt32(3);
        }

        var pcs = pcNames
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
        return (pcs, grid);
    }

    // ── Statistics feeders ───────────────────────────────────────

    /// <summary>Per-user effort seconds across a date range (inclusive).</summary>
    public Dictionary<int, int> GetEffortByPersonInRange(DateOnly startDate, DateOnly endDate)
    {
        var map = new Dictionary<int, int>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.PersonId, SUM(e.LengthSeconds)
            FROM sys_effort_entries e
            JOIN core_users u ON u.Id = e.PerformedByUserId
            WHERE e.EffortDate BETWEEN @s AND @e
            GROUP BY u.PersonId";
        cmd.Parameters.AddWithValue("@s", startDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@e", endDate.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (!r.IsDBNull(0)) map[r.GetInt32(0)] = r.IsDBNull(1) ? 0 : r.GetInt32(1);
        return map;
    }

    /// <summary>Per-day effort seconds across a date range (keyed by DateOnly).</summary>
    public Dictionary<DateOnly, int> GetEffortPerDay(DateOnly startDate, DateOnly endDate)
    {
        var map = new Dictionary<DateOnly, int>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT EffortDate, SUM(LengthSeconds)
            FROM sys_effort_entries
            WHERE EffortDate BETWEEN @s AND @e
            GROUP BY EffortDate";
        cmd.Parameters.AddWithValue("@s", startDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@e", endDate.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[DateOnly.Parse(r.GetString(0))] = r.IsDBNull(1) ? 0 : r.GetInt32(1);
        return map;
    }

    /// <summary>Total effort seconds for a date range (inclusive).</summary>
    public int GetEffortTotalInRange(DateOnly startDate, DateOnly endDate)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(LengthSeconds), 0)
            FROM sys_effort_entries
            WHERE EffortDate BETWEEN @s AND @e";
        cmd.Parameters.AddWithValue("@s", startDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@e", endDate.ToString("yyyy-MM-dd"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
