using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record EvilPurposeRow(
    int Id,
    int PcId,
    int? SessionId,
    string CreatedAt,
    string? OriginalWording,
    string? Translation,
    string? FPRD,
    string? Other,
    int InsertedBy,
    string InsertedByName);

public record SerfacRow(
    int Id,
    int PcId,
    int? SessionId,
    string CreatedAt,
    string? OriginalWording,
    string? Translation,
    string? Brackets,
    string? R3RA,
    int CreatedBy,
    string CreatedByName);

public record PtsHandlingRow(
    int Id,
    int PcId,
    int? SessionId,
    string CreatedAt,
    string? Action,
    string? Item,
    string? QuadRuds,
    string? PU,
    string? PtsRd,
    string? Ls,
    int CreatedBy,
    string CreatedByName);

public record CaseDataRow(
    int Id,
    int PcId,
    int? SessionId,
    string CreatedAt,
    string? Text,
    int CreatedBy,
    string CreatedByName);

public record SessionLite(int SessionId, string Label);
public record UserLite(int Id, string DisplayName);

public class SessionDataService
{
    private readonly string _connectionString;

    public SessionDataService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    // ── Lookups used by the modal UI ─────────────────────────────

    public List<SessionLite> GetSessionsForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT SessionId, COALESCE(NULLIF(Name,''), 'Session #' || SessionId) AS Label, SessionDate
            FROM sess_sessions
            WHERE PcId = @pc
            ORDER BY SessionDate DESC, SessionId DESC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var list = new List<SessionLite>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt32(0);
            var name = r.GetString(1);
            var date = r.IsDBNull(2) ? "" : r.GetString(2);
            var label = string.IsNullOrEmpty(date) ? name : $"{name}  ({date})";
            list.Add(new SessionLite(id, label));
        }
        return list;
    }

    public List<UserLite> GetActiveUsers()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.Id,
                   COALESCE(NULLIF(TRIM(p.FirstName || ' ' || COALESCE(p.LastName,'')), ''), u.Username) AS DisplayName
            FROM core_users u
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.IsActive = 1
            ORDER BY DisplayName";
        var list = new List<UserLite>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UserLite(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public int? GetUserIdByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM core_users WHERE LOWER(Username) = LOWER(@u) LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username);
        var v = cmd.ExecuteScalar();
        return v is long l ? (int)l : (v is int i ? i : (int?)null);
    }

    // ── Common ───────────────────────────────────────────────────

    private const string ByNameExpr =
        "COALESCE(NULLIF(TRIM(p.FirstName || ' ' || COALESCE(p.LastName,'')), ''), u.Username, 'User ' || u.Id)";

    private static object Nullable(object? v) => v ?? DBNull.Value;
    private static int? ReadNullableInt(SqliteDataReader r, int ord) => r.IsDBNull(ord) ? null : r.GetInt32(ord);
    private static string? ReadNullableString(SqliteDataReader r, int ord) => r.IsDBNull(ord) ? null : r.GetString(ord);

    // ── Evil Purposes ────────────────────────────────────────────

    public List<EvilPurposeRow> GetEvilPurposes(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT e.Id, e.PcId, e.SessionId, e.CreatedAt,
                   e.OriginalWording, e.Translation, e.FPRD, e.Other,
                   e.InsertedBy, {ByNameExpr} AS ByName
            FROM sess_evil_purposes e
            LEFT JOIN core_users u   ON u.Id = e.InsertedBy
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE e.PcId = @pc
            ORDER BY e.CreatedAt DESC, e.Id DESC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var list = new List<EvilPurposeRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new EvilPurposeRow(
                r.GetInt32(0), r.GetInt32(1), ReadNullableInt(r, 2), r.GetString(3),
                ReadNullableString(r, 4), ReadNullableString(r, 5),
                ReadNullableString(r, 6), ReadNullableString(r, 7),
                r.GetInt32(8), r.IsDBNull(9) ? "" : r.GetString(9)));
        }
        return list;
    }

    public int InsertEvilPurpose(EvilPurposeRow row)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_evil_purposes
                (PcId, SessionId, CreatedAt, OriginalWording, Translation, FPRD, Other, InsertedBy)
            VALUES
                (@pc, @sid, COALESCE(@ca, datetime('now')), @ow, @tr, @fp, @ot, @ib)
            RETURNING Id";
        cmd.Parameters.AddWithValue("@pc", row.PcId);
        cmd.Parameters.AddWithValue("@sid", Nullable(row.SessionId));
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(row.CreatedAt) ? (object)DBNull.Value : row.CreatedAt);
        cmd.Parameters.AddWithValue("@ow", Nullable(row.OriginalWording));
        cmd.Parameters.AddWithValue("@tr", Nullable(row.Translation));
        cmd.Parameters.AddWithValue("@fp", Nullable(row.FPRD));
        cmd.Parameters.AddWithValue("@ot", Nullable(row.Other));
        cmd.Parameters.AddWithValue("@ib", row.InsertedBy);
        var v = cmd.ExecuteScalar();
        return v is long l ? (int)l : Convert.ToInt32(v);
    }

    public void UpdateEvilPurpose(EvilPurposeRow row)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_evil_purposes SET
                SessionId       = @sid,
                CreatedAt       = COALESCE(@ca, CreatedAt),
                OriginalWording = @ow,
                Translation     = @tr,
                FPRD            = @fp,
                Other           = @ot,
                InsertedBy      = @ib
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@sid", Nullable(row.SessionId));
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(row.CreatedAt) ? (object)DBNull.Value : row.CreatedAt);
        cmd.Parameters.AddWithValue("@ow", Nullable(row.OriginalWording));
        cmd.Parameters.AddWithValue("@tr", Nullable(row.Translation));
        cmd.Parameters.AddWithValue("@fp", Nullable(row.FPRD));
        cmd.Parameters.AddWithValue("@ot", Nullable(row.Other));
        cmd.Parameters.AddWithValue("@ib", row.InsertedBy);
        cmd.ExecuteNonQuery();
    }

    public void DeleteEvilPurpose(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sess_evil_purposes WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    // ── Service Facsimiles (serfacs) ─────────────────────────────

    public List<SerfacRow> GetSerfacs(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT s.Id, s.PcId, s.SessionId, s.CreatedAt,
                   s.OriginalWording, s.Translation, s.Brackets, s.R3RA,
                   s.CreatedBy, {ByNameExpr} AS ByName
            FROM sess_serfacs s
            LEFT JOIN core_users u   ON u.Id = s.CreatedBy
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE s.PcId = @pc
            ORDER BY s.CreatedAt DESC, s.Id DESC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var list = new List<SerfacRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new SerfacRow(
                r.GetInt32(0), r.GetInt32(1), ReadNullableInt(r, 2), r.GetString(3),
                ReadNullableString(r, 4), ReadNullableString(r, 5),
                ReadNullableString(r, 6), ReadNullableString(r, 7),
                r.GetInt32(8), r.IsDBNull(9) ? "" : r.GetString(9)));
        }
        return list;
    }

    public int InsertSerfac(SerfacRow row)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_serfacs
                (PcId, SessionId, CreatedAt, OriginalWording, Translation, Brackets, R3RA, CreatedBy)
            VALUES
                (@pc, @sid, COALESCE(@ca, datetime('now')), @ow, @tr, @br, @r3, @cb)
            RETURNING Id";
        cmd.Parameters.AddWithValue("@pc", row.PcId);
        cmd.Parameters.AddWithValue("@sid", Nullable(row.SessionId));
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(row.CreatedAt) ? (object)DBNull.Value : row.CreatedAt);
        cmd.Parameters.AddWithValue("@ow", Nullable(row.OriginalWording));
        cmd.Parameters.AddWithValue("@tr", Nullable(row.Translation));
        cmd.Parameters.AddWithValue("@br", Nullable(row.Brackets));
        cmd.Parameters.AddWithValue("@r3", Nullable(row.R3RA));
        cmd.Parameters.AddWithValue("@cb", row.CreatedBy);
        var v = cmd.ExecuteScalar();
        return v is long l ? (int)l : Convert.ToInt32(v);
    }

    public void UpdateSerfac(SerfacRow row)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_serfacs SET
                SessionId       = @sid,
                CreatedAt       = COALESCE(@ca, CreatedAt),
                OriginalWording = @ow,
                Translation     = @tr,
                Brackets        = @br,
                R3RA            = @r3,
                CreatedBy       = @cb
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@sid", Nullable(row.SessionId));
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(row.CreatedAt) ? (object)DBNull.Value : row.CreatedAt);
        cmd.Parameters.AddWithValue("@ow", Nullable(row.OriginalWording));
        cmd.Parameters.AddWithValue("@tr", Nullable(row.Translation));
        cmd.Parameters.AddWithValue("@br", Nullable(row.Brackets));
        cmd.Parameters.AddWithValue("@r3", Nullable(row.R3RA));
        cmd.Parameters.AddWithValue("@cb", row.CreatedBy);
        cmd.ExecuteNonQuery();
    }

    public void DeleteSerfac(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sess_serfacs WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    // ── PTS Handling ─────────────────────────────────────────────

    public List<PtsHandlingRow> GetPtsHandling(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT t.Id, t.PcId, t.SessionId, t.CreatedAt,
                   t.Action, t.Item, t.QuadRuds, t.PU, t.PtsRd, t.Ls,
                   t.CreatedBy, {ByNameExpr} AS ByName
            FROM sess_pts_handling t
            LEFT JOIN core_users u   ON u.Id = t.CreatedBy
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE t.PcId = @pc
            ORDER BY t.CreatedAt DESC, t.Id DESC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var list = new List<PtsHandlingRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PtsHandlingRow(
                r.GetInt32(0), r.GetInt32(1), ReadNullableInt(r, 2), r.GetString(3),
                ReadNullableString(r, 4), ReadNullableString(r, 5),
                ReadNullableString(r, 6), ReadNullableString(r, 7),
                ReadNullableString(r, 8), ReadNullableString(r, 9),
                r.GetInt32(10), r.IsDBNull(11) ? "" : r.GetString(11)));
        }
        return list;
    }

    public int InsertPtsHandling(PtsHandlingRow row)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_pts_handling
                (PcId, SessionId, CreatedAt, Action, Item, QuadRuds, PU, PtsRd, Ls, CreatedBy)
            VALUES
                (@pc, @sid, COALESCE(@ca, datetime('now')), @ac, @it, @qr, @pu, @pr, @ls, @cb)
            RETURNING Id";
        cmd.Parameters.AddWithValue("@pc", row.PcId);
        cmd.Parameters.AddWithValue("@sid", Nullable(row.SessionId));
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(row.CreatedAt) ? (object)DBNull.Value : row.CreatedAt);
        cmd.Parameters.AddWithValue("@ac", Nullable(row.Action));
        cmd.Parameters.AddWithValue("@it", Nullable(row.Item));
        cmd.Parameters.AddWithValue("@qr", Nullable(row.QuadRuds));
        cmd.Parameters.AddWithValue("@pu", Nullable(row.PU));
        cmd.Parameters.AddWithValue("@pr", Nullable(row.PtsRd));
        cmd.Parameters.AddWithValue("@ls", Nullable(row.Ls));
        cmd.Parameters.AddWithValue("@cb", row.CreatedBy);
        var v = cmd.ExecuteScalar();
        return v is long l ? (int)l : Convert.ToInt32(v);
    }

    public void UpdatePtsHandling(PtsHandlingRow row)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_pts_handling SET
                SessionId = @sid,
                CreatedAt = COALESCE(@ca, CreatedAt),
                Action    = @ac,
                Item      = @it,
                QuadRuds  = @qr,
                PU        = @pu,
                PtsRd     = @pr,
                Ls        = @ls,
                CreatedBy = @cb
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@sid", Nullable(row.SessionId));
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(row.CreatedAt) ? (object)DBNull.Value : row.CreatedAt);
        cmd.Parameters.AddWithValue("@ac", Nullable(row.Action));
        cmd.Parameters.AddWithValue("@it", Nullable(row.Item));
        cmd.Parameters.AddWithValue("@qr", Nullable(row.QuadRuds));
        cmd.Parameters.AddWithValue("@pu", Nullable(row.PU));
        cmd.Parameters.AddWithValue("@pr", Nullable(row.PtsRd));
        cmd.Parameters.AddWithValue("@ls", Nullable(row.Ls));
        cmd.Parameters.AddWithValue("@cb", row.CreatedBy);
        cmd.ExecuteNonQuery();
    }

    public void DeletePtsHandling(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sess_pts_handling WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    // ── Case Data ────────────────────────────────────────────────

    public List<CaseDataRow> GetCaseData(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT c.Id, c.PcId, c.SessionId, c.CreatedAt, c.Text,
                   c.CreatedBy, {ByNameExpr} AS ByName
            FROM sess_case_data c
            LEFT JOIN core_users u   ON u.Id = c.CreatedBy
            LEFT JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE c.PcId = @pc
            ORDER BY c.CreatedAt DESC, c.Id DESC";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var list = new List<CaseDataRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new CaseDataRow(
                r.GetInt32(0), r.GetInt32(1), ReadNullableInt(r, 2), r.GetString(3),
                ReadNullableString(r, 4),
                r.GetInt32(5), r.IsDBNull(6) ? "" : r.GetString(6)));
        }
        return list;
    }

    public int InsertCaseData(CaseDataRow row)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_case_data
                (PcId, SessionId, CreatedAt, Text, CreatedBy)
            VALUES
                (@pc, @sid, COALESCE(@ca, datetime('now')), @tx, @cb)
            RETURNING Id";
        cmd.Parameters.AddWithValue("@pc", row.PcId);
        cmd.Parameters.AddWithValue("@sid", Nullable(row.SessionId));
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(row.CreatedAt) ? (object)DBNull.Value : row.CreatedAt);
        cmd.Parameters.AddWithValue("@tx", Nullable(row.Text));
        cmd.Parameters.AddWithValue("@cb", row.CreatedBy);
        var v = cmd.ExecuteScalar();
        return v is long l ? (int)l : Convert.ToInt32(v);
    }

    public void UpdateCaseData(CaseDataRow row)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_case_data SET
                SessionId = @sid,
                CreatedAt = COALESCE(@ca, CreatedAt),
                Text      = @tx,
                CreatedBy = @cb
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@sid", Nullable(row.SessionId));
        cmd.Parameters.AddWithValue("@ca", string.IsNullOrWhiteSpace(row.CreatedAt) ? (object)DBNull.Value : row.CreatedAt);
        cmd.Parameters.AddWithValue("@tx", Nullable(row.Text));
        cmd.Parameters.AddWithValue("@cb", row.CreatedBy);
        cmd.ExecuteNonQuery();
    }

    public void DeleteCaseData(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sess_case_data WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }
}
