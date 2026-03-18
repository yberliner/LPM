using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record AuditorListItem(int AuditorId, string FullName, string StaffRole, bool IsActive, string? GradeCode);
public record GradeItem(int GradeId, string Code);
public record AuditorDetail(int AuditorId, string FirstName, string LastName,
    string StaffRole, bool IsActive, int? CurrentGradeId, string? GradeCode);
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
            SELECT u.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   u.StaffRole, u.IsActive, g.Code
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            LEFT JOIN lkp_grades g ON g.GradeId = u.GradeId
            WHERE u.StaffRole IN ('Auditor','CS','Solo')
            ORDER BY p.FirstName";
        var list = new List<AuditorListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AuditorListItem(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
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
            SELECT u.PersonId, p.FirstName, COALESCE(p.LastName,''),
                   u.StaffRole, u.IsActive, u.GradeId, g.Code
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            LEFT JOIN lkp_grades g ON g.GradeId = u.GradeId
            WHERE u.PersonId = @id AND u.StaffRole IN ('Auditor','CS','Solo')
            LIMIT 1";
        cmd.Parameters.AddWithValue("@id", auditorId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new AuditorDetail(
            r.GetInt32(0), r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetInt32(4) != 0,
            r.IsDBNull(5) ? null : r.GetInt32(5),
            r.IsDBNull(6) ? null : r.GetString(6));
    }

    public void UpdateAuditor(int auditorId, string firstName, string lastName,
        int? gradeId, string staffRole, bool isActive)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = "UPDATE core_persons SET FirstName=@fn, LastName=@ln WHERE PersonId=@id";
        pCmd.Parameters.AddWithValue("@fn", firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln", lastName.Trim());
        pCmd.Parameters.AddWithValue("@id", auditorId);
        pCmd.ExecuteNonQuery();

        using var uCmd = conn.CreateCommand();
        uCmd.CommandText = @"
            UPDATE core_users SET GradeId=@gid, StaffRole=@role, IsActive=@active
            WHERE PersonId=@id AND StaffRole IN ('Auditor','CS','Solo')";
        uCmd.Parameters.AddWithValue("@gid",    gradeId.HasValue ? (object)gradeId.Value : DBNull.Value);
        uCmd.Parameters.AddWithValue("@role",   staffRole);
        uCmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        uCmd.Parameters.AddWithValue("@id",     auditorId);
        uCmd.ExecuteNonQuery();
        Console.WriteLine($"[AuditorService] Updated auditor {auditorId}");
    }

    public AuditorStats GetAuditorStats(int auditorId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Regular sessions: AuditorId = auditor's PersonId (non-null)
        // Solo sessions (AuditorId IS NULL) are excluded here; use solo stats separately if needed
        cmd.CommandText = @"
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN IsFreeSession=1 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(LengthSeconds), 0),
                   MAX(SessionDate)
            FROM sess_sessions
            WHERE AuditorId=@id";
        cmd.Parameters.AddWithValue("@id", auditorId);
        using var r = cmd.ExecuteReader();
        r.Read();
        return new AuditorStats(
            r.GetInt32(0), r.GetInt32(1), r.GetInt64(2),
            r.IsDBNull(3) ? null : r.GetString(3));
    }
}
