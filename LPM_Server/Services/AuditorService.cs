using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;
using LPM.Auth;

namespace LPM.Services;

public record AuditorListItem(int AuditorId, string FullName, string StaffRole, bool IsActive, string? GradeCode, string Username = "");
public record GradeItem(int GradeId, string Code);
public record AuditorDetail(int AuditorId, string FirstName, string LastName,
    string StaffRole, bool IsActive, int? CurrentGradeId, string? GradeCode, bool IsAdmin, string Username = "", bool AllowAll = false, bool SendSms = false);
public record AuditorSmsInfo(bool SendSms, string Phone, string AuditorName);
public record AuditorStats(int TotalSessions, int FreeSessions, long TotalSec, string? LastSessionDate);
public record SoloPermissionViolation(string FullName, string Username, bool BadAllowAll, bool BadUserType);
public record SecuritySummaryItem(string FullName, string Username, string StaffRole);
public record SecuritySummary(List<SecuritySummaryItem> AdminUsers, List<SecuritySummaryItem> AllowAllUsers, List<SecuritySummaryItem> DefaultPasswordUsers);
public record DuplicateStaffUser(string FullName, string Roles);
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
        cmd.CommandText = $@"
            SELECT u.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   u.StaffRole, u.IsActive, g.Code, u.Username
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            LEFT JOIN lkp_grades g ON g.GradeId = u.GradeId
            WHERE u.StaffRole IN {StaffRoles.SqlInAuditorCS()}
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
        cmd.CommandText = $@"
            SELECT u.PersonId, p.FirstName, COALESCE(p.LastName,''),
                   u.StaffRole, u.IsActive, u.GradeId, g.Code, u.UserType, u.Username, u.AllowAll, u.SendSms
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            LEFT JOIN lkp_grades g ON g.GradeId = u.GradeId
            WHERE u.PersonId = @id AND u.StaffRole IN {StaffRoles.SqlInAuditorCS()}
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
            r.IsDBNull(8) ? "" : r.GetString(8),
            r.IsDBNull(9) ? false : r.GetInt32(9) != 0,
            r.IsDBNull(10) ? false : r.GetInt32(10) != 0);
    }

    public void UpdateAuditor(int auditorId, string firstName, string lastName,
        int? gradeId, string staffRole, bool isActive, bool isAdmin, bool allowAll, bool sendSms = false)
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
        uCmd.CommandText = $@"
            UPDATE core_users SET GradeId=@gid, StaffRole=@role, IsActive=@active, UserType=@ut, AllowAll=@aa, SendSms=@sms
            WHERE PersonId=@id AND StaffRole IN {StaffRoles.SqlInAuditorCS()}";
        uCmd.Parameters.AddWithValue("@gid",    gradeId.HasValue ? (object)gradeId.Value : DBNull.Value);
        uCmd.Parameters.AddWithValue("@role",   staffRole);
        uCmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        uCmd.Parameters.AddWithValue("@ut",     isAdmin ? "Admin" : "Standard");
        uCmd.Parameters.AddWithValue("@aa",     allowAll ? 1 : 0);
        uCmd.Parameters.AddWithValue("@sms",    sendSms ? 1 : 0);
        uCmd.Parameters.AddWithValue("@id",     auditorId);
        uCmd.ExecuteNonQuery();
        Console.WriteLine($"[AuditorService] Updated auditor {auditorId} isAdmin={isAdmin} allowAll={allowAll} sendSms={sendSms}");
    }

    public void DeleteAuditor(int auditorId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM core_users WHERE PersonId=@id AND StaffRole IN {StaffRoles.SqlInAuditorCS()}";
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

    /// Returns Solo users that violate the expected permissions (AllowAll=0, UserType='Standard').
    public List<SoloPermissionViolation> CheckSoloUserIntegrity()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   u.Username, COALESCE(u.AllowAll, 0), COALESCE(u.UserType, 'Standard')
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.StaffRole = 'Solo'
              AND (COALESCE(u.AllowAll, 0) != 0 OR COALESCE(u.UserType, 'Standard') != 'Standard')
            ORDER BY p.FirstName";
        var list = new List<SoloPermissionViolation>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SoloPermissionViolation(
                r.GetString(0), r.GetString(1),
                r.GetInt32(2) != 0,
                r.GetString(3) != "Standard"));
        return list;
    }

    /// Returns PersonIds that have more than one Auditor/CS row in core_users (should never happen).
    public List<DuplicateStaffUser> GetDuplicateStaffUsers()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   GROUP_CONCAT(u.Username || ' (' || u.StaffRole || ')', ', ') AS Roles
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.StaffRole IN {StaffRoles.SqlInAuditorCS()}
            GROUP BY u.PersonId
            HAVING COUNT(*) > 1
            ORDER BY p.FirstName";
        var list = new List<DuplicateStaffUser>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DuplicateStaffUser(r.GetString(0), r.GetString(1)));
        return list;
    }

    /// Returns security summary: admin users, AllowAll users, and users still on their default password.
    /// NOTE: Default password check uses hash verification (slow — run on a background thread).
    public SecuritySummary GetSecuritySummary()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        static List<SecuritySummaryItem> Query(SqliteConnection c, string where)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $@"
                SELECT TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                       u.Username, COALESCE(u.StaffRole, '')
                FROM core_users u
                JOIN core_persons p ON p.PersonId = u.PersonId
                WHERE {where}
                ORDER BY p.FirstName";
            var list = new List<SecuritySummaryItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new SecuritySummaryItem(r.GetString(0), r.GetString(1), r.GetString(2)));
            return list;
        }

        // Default password check: compute {First}1992 from the person's first name and verify against hash
        var defaultPwdUsers = new List<SecuritySummaryItem>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                       u.Username, COALESCE(u.StaffRole, ''), u.PasswordHash, p.FirstName
                FROM core_users u
                JOIN core_persons p ON p.PersonId = u.PersonId
                WHERE u.IsActive = 1
                ORDER BY p.FirstName";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var fullName = r.GetString(0);
                var username = r.GetString(1);
                var staffRole = r.GetString(2);
                var hash = r.IsDBNull(3) ? "" : r.GetString(3);
                var firstName = r.GetString(4);
                if (string.IsNullOrEmpty(hash)) continue;

                var normFn = NormalizeToAscii(firstName).ToLower().Trim();
                if (normFn.Length == 0) continue;
                var expectedPwd = char.ToUpper(normFn[0]) + (normFn.Length > 1 ? normFn[1..] : "") + "1992";

                if (UserDb.VerifyPassword(hash, expectedPwd))
                    defaultPwdUsers.Add(new SecuritySummaryItem(fullName, username, staffRole));
            }
        }

        return new SecuritySummary(
            Query(conn, "u.UserType = 'Admin'"),
            Query(conn, "COALESCE(u.AllowAll, 0) = 1"),
            defaultPwdUsers);
    }

    /// Returns PCs that have no active core_users row with StaffRole in Auditor/CS/Solo.
    public List<AvailablePcItem> GetPcsAvailableForStaff()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT cp.PersonId,
                   TRIM(cp.FirstName || ' ' || COALESCE(NULLIF(cp.LastName,''), '')) AS FullName
            FROM core_persons cp
            JOIN core_pcs pc ON pc.PcId = cp.PersonId
            WHERE NOT EXISTS (
                SELECT 1 FROM core_users cu
                WHERE cu.PersonId = cp.PersonId
                  AND cu.StaffRole IN {StaffRoles.SqlInAuditorCS()}
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
    public string CreateStaffUser(int pcId, string staffRole, bool isAdmin, bool allowAll)
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
            isAdmin ? "Admin" : "Standard", null, allowAll);
        Console.WriteLine($"[AuditorService] Created staff user '{username}' for PC {pcId}, role={staffRole}, admin={isAdmin}, allowAll={allowAll}");
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

    /// Returns SMS settings for the auditor of a given session.
    /// Returns null if the session has no assigned auditor (NULL or -1).
    public AuditorSmsInfo? GetAuditorSmsInfo(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Use AuditorId if set, otherwise fall back to PcId (e.g. Solo PCs with no assigned auditor)
        cmd.CommandText = @"
            SELECT u.SendSms, COALESCE(p.Phone,''),
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), ''))
            FROM sess_sessions s
            JOIN core_users u ON u.PersonId = COALESCE(NULLIF(s.AuditorId, 0), s.PcId)
            JOIN core_persons p ON p.PersonId = COALESCE(NULLIF(s.AuditorId, 0), s.PcId)
            WHERE s.SessionId = @sid
            LIMIT 1";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new AuditorSmsInfo(r.GetInt32(0) != 0, r.GetString(1), r.GetString(2));
    }

    /// Returns true if phone is a valid Israeli mobile number:
    ///   +972XXXXXXXXX (13 chars total) OR 05XXXXXXXX (10 digits).
    public static bool IsValidIsraeliPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var p = phone.Trim().Replace("-", "").Replace(" ", "");
        if (p.StartsWith("+972") && p.Length == 13) return true;
        if (p.StartsWith("05")   && p.Length == 10 && p.All(char.IsDigit)) return true;
        return false;
    }
}
