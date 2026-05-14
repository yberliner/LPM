using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

// ── Record DTOs ─────────────────────────────────────────────────────────────

public record ExcalRow(
    int Id,
    int PcId,
    int? SessionId,
    string CreatedAt,
    string? Area,
    int? NumPlugs,
    string? DateEp,
    int CreatedBy,
    string CreatedByName);

public record PhoenixRow(
    int Id,
    int PcId,
    int? SessionId,
    string CreatedAt,
    string? Item,
    string? TAA,
    string? Formulating,
    string? OT9,
    string? OT10,
    string? OT11,
    int CreatedBy,
    string CreatedByName);

public record Ot12SpotRow(
    int Id,
    int PcId,
    int? SessionId,
    string CreatedAt,
    string? DateStart,
    string? Ability,
    int? NumCreations,
    string? DateEnd,
    int CreatedBy,
    string CreatedByName);

public class SoloRecordService
{
    private readonly string _connectionString;

    public SoloRecordService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    // ── Excal ───────────────────────────────────────────────────────────────

    public int InsertExcal(int pcId, int? sessionId, string? createdAt, string? area, int? numPlugs, string? dateEp, int createdBy)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_solo_excal (PcId, SessionId, CreatedAt, Area, NumPlugs, DateEp, CreatedBy)
            VALUES (@pc, @sid, COALESCE(@ca, datetime('now')), @area, @plugs, @dep, @cb);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@sid", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(createdAt) ? (object)DBNull.Value : createdAt);
        cmd.Parameters.AddWithValue("@area", (object?)area ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@plugs", (object?)numPlugs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dep", (object?)dateEp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", createdBy);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateExcal(int id, int? sessionId, string? createdAt, string? area, int? numPlugs, string? dateEp, int createdBy)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_solo_excal SET
                SessionId = @sid,
                CreatedAt = COALESCE(@ca, CreatedAt),
                Area = @a, NumPlugs = @p, DateEp = @d,
                CreatedBy = @cb
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@sid", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(createdAt) ? (object)DBNull.Value : createdAt);
        cmd.Parameters.AddWithValue("@a", (object?)area ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@p", (object?)numPlugs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@d", (object?)dateEp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", createdBy);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteExcal(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sess_solo_excal WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public List<ExcalRow> GetExcalForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT e.Id, e.PcId, e.SessionId, e.CreatedAt, e.Area, e.NumPlugs, e.DateEp, e.CreatedBy,
                   COALESCE(NULLIF(TRIM(p.FirstName || ' ' || COALESCE(p.LastName,'')),''), u.Username) AS CreatedByName
            FROM sess_solo_excal e
            LEFT JOIN core_users u   ON u.Id = e.CreatedBy
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE e.PcId = @pc
            ORDER BY e.Id ASC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var list = new List<ExcalRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ExcalRow(
                r.GetInt32(0), r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetInt32(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetInt32(7),
                r.IsDBNull(8) ? "" : r.GetString(8)));
        }
        return list;
    }

    // ── Phoenix ─────────────────────────────────────────────────────────────

    public int InsertPhoenix(int pcId, int? sessionId, string? createdAt, string? item, string? taa, string? formulating,
        string? ot9, string? ot10, string? ot11, int createdBy)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_solo_phoenix (PcId, SessionId, CreatedAt, Item, TAA, Formulating, OT9, OT10, OT11, CreatedBy)
            VALUES (@pc, @sid, COALESCE(@ca, datetime('now')), @it, @taa, @fm, @o9, @o10, @o11, @cb);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@sid", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(createdAt) ? (object)DBNull.Value : createdAt);
        cmd.Parameters.AddWithValue("@it", (object?)item ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@taa", (object?)taa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fm", (object?)formulating ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o9", (object?)ot9 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o10", (object?)ot10 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o11", (object?)ot11 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", createdBy);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdatePhoenix(int id, int? sessionId, string? createdAt, string? item, string? taa, string? formulating,
        string? ot9, string? ot10, string? ot11, int createdBy)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_solo_phoenix SET
                SessionId = @sid,
                CreatedAt = COALESCE(@ca, CreatedAt),
                Item = @it, TAA = @taa, Formulating = @fm,
                OT9 = @o9, OT10 = @o10, OT11 = @o11,
                CreatedBy = @cb
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@sid", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(createdAt) ? (object)DBNull.Value : createdAt);
        cmd.Parameters.AddWithValue("@it", (object?)item ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@taa", (object?)taa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fm", (object?)formulating ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o9", (object?)ot9 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o10", (object?)ot10 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o11", (object?)ot11 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", createdBy);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeletePhoenix(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sess_solo_phoenix WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public List<PhoenixRow> GetPhoenixForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ph.Id, ph.PcId, ph.SessionId, ph.CreatedAt, ph.Item, ph.TAA, ph.Formulating,
                   ph.OT9, ph.OT10, ph.OT11, ph.CreatedBy,
                   COALESCE(NULLIF(TRIM(p.FirstName || ' ' || COALESCE(p.LastName,'')),''), u.Username) AS CreatedByName
            FROM sess_solo_phoenix ph
            LEFT JOIN core_users u   ON u.Id = ph.CreatedBy
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE ph.PcId = @pc
            ORDER BY ph.Id ASC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var list = new List<PhoenixRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PhoenixRow(
                r.GetInt32(0), r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetInt32(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.GetInt32(10),
                r.IsDBNull(11) ? "" : r.GetString(11)));
        }
        return list;
    }

    // ── OT12 - Spot ─────────────────────────────────────────────────────────

    public int InsertOt12Spot(int pcId, int? sessionId, string? createdAt, string? dateStart, string? ability,
        int? numCreations, string? dateEnd, int createdBy)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_solo_ot12_spot (PcId, SessionId, CreatedAt, DateStart, Ability, NumCreations, DateEnd, CreatedBy)
            VALUES (@pc, @sid, COALESCE(@ca, datetime('now')), @ds, @ab, @nc, @de, @cb);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@sid", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(createdAt) ? (object)DBNull.Value : createdAt);
        cmd.Parameters.AddWithValue("@ds", (object?)dateStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ab", (object?)ability ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nc", (object?)numCreations ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@de", (object?)dateEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", createdBy);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateOt12Spot(int id, int? sessionId, string? createdAt, string? dateStart, string? ability,
        int? numCreations, string? dateEnd, int createdBy)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_solo_ot12_spot SET
                SessionId = @sid,
                CreatedAt = COALESCE(@ca, CreatedAt),
                DateStart = @ds, Ability = @ab,
                NumCreations = @nc, DateEnd = @de,
                CreatedBy = @cb
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@sid", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(createdAt) ? (object)DBNull.Value : createdAt);
        cmd.Parameters.AddWithValue("@ds", (object?)dateStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ab", (object?)ability ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nc", (object?)numCreations ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@de", (object?)dateEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", createdBy);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteOt12Spot(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sess_solo_ot12_spot WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Ot12SpotRow> GetOt12SpotForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT o.Id, o.PcId, o.SessionId, o.CreatedAt, o.DateStart, o.Ability,
                   o.NumCreations, o.DateEnd, o.CreatedBy,
                   COALESCE(NULLIF(TRIM(p.FirstName || ' ' || COALESCE(p.LastName,'')),''), u.Username) AS CreatedByName
            FROM sess_solo_ot12_spot o
            LEFT JOIN core_users u   ON u.Id = o.CreatedBy
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE o.PcId = @pc
            ORDER BY o.Id ASC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var list = new List<Ot12SpotRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Ot12SpotRow(
                r.GetInt32(0), r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetInt32(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetInt32(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.GetInt32(8),
                r.IsDBNull(9) ? "" : r.GetString(9)));
        }
        return list;
    }
}
