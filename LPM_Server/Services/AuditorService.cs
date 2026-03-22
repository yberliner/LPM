using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;
using LPM.Auth;

namespace LPM.Services;

public record AuditorListItem(int AuditorId, string FullName, string StaffRole, bool IsActive, string? GradeCode, string Username = "");
public record GradeItem(int GradeId, string Code);
public record AuditorDetail(int AuditorId, string FirstName, string LastName,
    string StaffRole, bool IsActive, int? CurrentGradeId, string? GradeCode, bool IsAdmin, string Username = "");
public record AuditorStats(int TotalSessions, int FreeSessions, long TotalSec, string? LastSessionDate);
public record AvailablePcItem(int PcId, string FullName);

public class AuditorService(IConfiguration config, UserDb userDb)
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
                   u.StaffRole, u.IsActive, g.Code, u.Username
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
                r.GetInt32(3) != 0, r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? "" : r.GetString(5)));
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
                   u.StaffRole, u.IsActive, u.GradeId, g.Code, u.UserType, u.Username
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
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? false : r.GetString(7) == "Admin",
            r.IsDBNull(8) ? "" : r.GetString(8));
    }

    public void UpdateAuditor(int auditorId, string firstName, string lastName,
        int? gradeId, string staffRole, bool isActive, bool isAdmin)
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
            UPDATE core_users SET GradeId=@gid, StaffRole=@role, IsActive=@active, UserType=@ut
            WHERE PersonId=@id AND StaffRole IN ('Auditor','CS','Solo')";
        uCmd.Parameters.AddWithValue("@gid",    gradeId.HasValue ? (object)gradeId.Value : DBNull.Value);
        uCmd.Parameters.AddWithValue("@role",   staffRole);
        uCmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        uCmd.Parameters.AddWithValue("@ut",     isAdmin ? "Admin" : "Standard");
        uCmd.Parameters.AddWithValue("@id",     auditorId);
        uCmd.ExecuteNonQuery();
        Console.WriteLine($"[AuditorService] Updated auditor {auditorId} isAdmin={isAdmin}");
    }

    public void DeleteAuditor(int auditorId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM core_users WHERE PersonId=@id AND StaffRole IN ('Auditor','CS','Solo')";
        cmd.Parameters.AddWithValue("@id", auditorId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[AuditorService] Deleted auditor {auditorId}");
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
            WHERE AuditorId=@id";
        cmd.Parameters.AddWithValue("@id", auditorId);
        using var r = cmd.ExecuteReader();
        r.Read();
        return new AuditorStats(
            r.GetInt32(0), r.GetInt32(1), r.GetInt64(2),
            r.IsDBNull(3) ? null : r.GetString(3));
    }

    /// Returns PCs that have no active core_users row with StaffRole in Auditor/CS/Solo.
    public List<AvailablePcItem> GetPcsAvailableForStaff()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT cp.PersonId,
                   TRIM(cp.FirstName || ' ' || COALESCE(NULLIF(cp.LastName,''), '')) AS FullName
            FROM core_persons cp
            JOIN core_pcs pc ON pc.PcId = cp.PersonId
            WHERE NOT EXISTS (
                SELECT 1 FROM core_users cu
                WHERE cu.PersonId = cp.PersonId
                  AND cu.StaffRole IN ('Auditor','CS')
                  AND cu.IsActive = 1
            )
            ORDER BY cp.FirstName";
        var list = new List<AvailablePcItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AvailablePcItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    /// Computes the would-be username and password for a PC without inserting.
    public (string Username, string Password) PreviewStaffCredentials(int pcId)
    {
        var (fn, ln) = GetPersonNameParts(pcId);
        var normFn = NormalizeToAscii(fn).ToLower().Trim();
        var normLn = NormalizeToAscii(ln).ToLower().Trim();

        string username;
        if (!userDb.UsernameExists(normFn))
        {
            username = normFn;
        }
        else
        {
            var firstLast = string.IsNullOrEmpty(normLn) ? normFn : $"{normFn}.{normLn}";
            username = firstLast;
            int suffix = 2;
            while (userDb.UsernameExists(username))
                username = firstLast + suffix++;
        }

        var password = normFn.Length > 0
            ? char.ToUpper(normFn[0]) + (normFn.Length > 1 ? normFn[1..] : "") + "1992"
            : "Staff1992";
        return (username, password);
    }

    /// Creates a core_users staff row for the given PC. Returns the generated username.
    public string CreateStaffUser(int pcId, string staffRole, bool isAdmin)
    {
        var (fn, ln) = GetPersonNameParts(pcId);
        var normFn = NormalizeToAscii(fn).ToLower().Trim();
        var normLn = NormalizeToAscii(ln).ToLower().Trim();

        // Try first name only; fall back to first.last; then first.last2, first.last3…
        string username;
        if (!userDb.UsernameExists(normFn))
        {
            username = normFn;
        }
        else
        {
            var firstLast = string.IsNullOrEmpty(normLn) ? normFn : $"{normFn}.{normLn}";
            username = firstLast;
            int suffix = 2;
            while (userDb.UsernameExists(username))
                username = firstLast + suffix++;
        }

        var password = normFn.Length > 0
            ? char.ToUpper(normFn[0]) + (normFn.Length > 1 ? normFn[1..] : "") + "1992"
            : "Staff1992";

        userDb.CreateUser(pcId, username, password, staffRole,
            isAdmin ? "Admin" : "Standard", null, false);
        Console.WriteLine($"[AuditorService] Created staff user '{username}' for PC {pcId}, role={staffRole}, admin={isAdmin}");
        return username;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private (string FirstName, string LastName) GetPersonNameParts(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT FirstName, COALESCE(LastName,'') FROM core_persons WHERE PersonId=@id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", pcId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return ("", "");
        return (r.GetString(0), r.GetString(1));
    }

    private string GetLastName(int pcId)
    {
        var (_, ln) = GetPersonNameParts(pcId);
        return ln;
    }

    private static (string Username, string Password) BuildCredentials(string firstName, string lastName)
    {
        var fn = NormalizeToAscii(firstName).ToLower().Trim();
        var ln = NormalizeToAscii(lastName).ToLower().Trim();
        var username = string.IsNullOrEmpty(ln) ? fn : $"{fn}.{ln}";
        if (string.IsNullOrEmpty(username)) username = "staff";
        var password = fn.Length > 0
            ? char.ToUpper(fn[0]) + (fn.Length > 1 ? fn[1..] : "") + "1992"
            : "Staff1992";
        return (username, password);
    }

    private static string NormalizeToAscii(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
