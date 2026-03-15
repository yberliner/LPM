using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record AuditorListItem(int AuditorId, string FullName, int Type, bool IsActive, string? GradeCode);
public record GradeItem(int GradeId, string Code);
public record AuditorDetail(int AuditorId, string FirstName, string LastName,
    int Type, bool IsActive, int? CurrentGradeId, string? GradeCode);
public record AuditorStats(int TotalSessions, int FreeSessions, long TotalSec, string? LastSessionDate);

public class AuditorService(IConfiguration config)
{
    private readonly string _connectionString =
        $"Data Source={config["Database:Path"] ?? "lifepower.db"}";

    public List<AuditorListItem> GetAllAuditors()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT a.AuditorId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   a.Type, a.IsActive, g.Code
            FROM sess_auditors a
            JOIN core_persons p ON p.PersonId = a.AuditorId
            LEFT JOIN lkp_grades g ON g.GradeId = a.CurrentGradeId
            ORDER BY p.FirstName";
        var list = new List<AuditorListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AuditorListItem(
                r.GetInt32(0), r.GetString(1), r.GetInt32(2),
                r.GetInt32(3) != 0, r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    public List<GradeItem> GetAllGrades()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT GradeId, Code FROM lkp_grades ORDER BY SortOrder";
        var list = new List<GradeItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new GradeItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public AuditorDetail? GetAuditorDetail(int auditorId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT a.AuditorId, p.FirstName, COALESCE(p.LastName,''),
                   a.Type, a.IsActive, a.CurrentGradeId, g.Code
            FROM sess_auditors a
            JOIN core_persons p ON p.PersonId = a.AuditorId
            LEFT JOIN lkp_grades g ON g.GradeId = a.CurrentGradeId
            WHERE a.AuditorId = @id";
        cmd.Parameters.AddWithValue("@id", auditorId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new AuditorDetail(
            r.GetInt32(0), r.GetString(1), r.GetString(2),
            r.GetInt32(3), r.GetInt32(4) != 0,
            r.IsDBNull(5) ? null : r.GetInt32(5),
            r.IsDBNull(6) ? null : r.GetString(6));
    }

    public void UpdateAuditor(int auditorId, string firstName, string lastName,
        int? gradeId, int type, bool isActive)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = "UPDATE core_persons SET FirstName=@fn, LastName=@ln WHERE PersonId=@id";
        pCmd.Parameters.AddWithValue("@fn", firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln", lastName.Trim());
        pCmd.Parameters.AddWithValue("@id", auditorId);
        pCmd.ExecuteNonQuery();

        using var aCmd = conn.CreateCommand();
        aCmd.CommandText = @"
            UPDATE sess_auditors SET CurrentGradeId=@gid, Type=@type, IsActive=@active
            WHERE AuditorId=@id";
        aCmd.Parameters.AddWithValue("@gid", gradeId.HasValue ? (object)gradeId.Value : DBNull.Value);
        aCmd.Parameters.AddWithValue("@type", type);
        aCmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        aCmd.Parameters.AddWithValue("@id", auditorId);
        aCmd.ExecuteNonQuery();
        Console.WriteLine($"[AuditorService] Updated auditor {auditorId}");
    }

    public int AddAuditor(string firstName, string lastName, int? gradeId, int type)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = "INSERT INTO core_persons (FirstName, LastName) VALUES (@fn, @ln)";
        pCmd.Parameters.AddWithValue("@fn", firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln", lastName.Trim());
        pCmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var personId = (int)(long)idCmd.ExecuteScalar()!;

        using var aCmd = conn.CreateCommand();
        aCmd.CommandText = @"
            INSERT INTO sess_auditors (AuditorId, CurrentGradeId, IsActive, Type)
            VALUES (@id, @gid, 1, @type)";
        aCmd.Parameters.AddWithValue("@id", personId);
        aCmd.Parameters.AddWithValue("@gid", gradeId.HasValue ? (object)gradeId.Value : DBNull.Value);
        aCmd.Parameters.AddWithValue("@type", type);
        aCmd.ExecuteNonQuery();

        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = "INSERT OR IGNORE INTO core_pcs (PcId) VALUES (@id)";
        pcCmd.Parameters.AddWithValue("@id", personId);
        pcCmd.ExecuteNonQuery();

        Console.WriteLine($"[AuditorService] Added auditor: '{firstName.Trim()} {lastName.Trim()}'");
        return personId;
    }

    public AuditorStats GetAuditorStats(int auditorId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN IsFreeSession=1 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(LengthSeconds), 0),
                   MAX(SessionDate)
            FROM sess_sessions
            WHERE AuditorId=@id AND PcId != AuditorId";
        cmd.Parameters.AddWithValue("@id", auditorId);
        using var r = cmd.ExecuteReader();
        r.Read();
        return new AuditorStats(
            r.GetInt32(0), r.GetInt32(1), r.GetInt64(2),
            r.IsDBNull(3) ? null : r.GetString(3));
    }
}
