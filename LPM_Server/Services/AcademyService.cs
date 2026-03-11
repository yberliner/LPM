using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LPM.Services;

public record PersonItem(int PersonId, string FullName, string Referral, string Org);
public record VisitRecord(int VisitId, int PersonId, string FullName, string Referral, string Org);
public record TopStudent(string FullName, int VisitCount, string Org);
public record WeekVisitCount(string WeekLabel, int TotalVisits, List<TopStudent> TopStudents,
    int DonCount, int FriendCount, int SocialCount, int OtherCount);
public record MemberAdminItem(int PersonId, string FullName, string Phone,
    bool IsActive, bool IsPC, bool IsAcademyStudent, bool IsStaff,
    string FirstName, string LastName, string? ExternalId, string? LastVisitDate);

public class AcademyService
{
    private readonly string _connectionString;

    public AcademyService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    // ── Schema / Migration ──────────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        if (!GetTableNames(conn).Contains("acad_attendance"))
        {
            Execute(conn, @"
                CREATE TABLE acad_attendance (
                    StudentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    PersonId  INTEGER NOT NULL,
                    VisitDate TEXT    NOT NULL,
                    UNIQUE (PersonId, VisitDate)
                )");
        }
    }

    // ── Persons ─────────────────────────────────────────────────────────────

    public List<PersonItem> GetAllPersons()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT PersonId,
                   TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''), '')) AS FullName,
                   COALESCE(Referral,'') AS Referral,
                   COALESCE(Org,'') AS Org
            FROM core_persons
            WHERE COALESCE(IsActive, 1) = 1
            ORDER BY FirstName, LastName";
        var list = new List<PersonItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PersonItem(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    /// <summary>Creates a new Person record and returns the new PersonId.</summary>
    public int AddPersonForAcademy(string firstName, string lastName,
        string phone, string email, string dateOfBirth, string gender,
        string org = "", string referral = "")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO core_persons (FirstName, LastName, Phone, Email, DateOfBirth, Gender, Org, Referral)
            VALUES (@fn, @ln, @ph, @em, @dob, @gender, @org, @ref)";
        cmd.Parameters.AddWithValue("@fn",  firstName.Trim());
        cmd.Parameters.AddWithValue("@ln",  lastName.Trim());
        cmd.Parameters.AddWithValue("@ph",  string.IsNullOrWhiteSpace(phone)       ? DBNull.Value : (object)phone.Trim());
        cmd.Parameters.AddWithValue("@em",  string.IsNullOrWhiteSpace(email)       ? DBNull.Value : (object)email.Trim());
        cmd.Parameters.AddWithValue("@dob", string.IsNullOrWhiteSpace(dateOfBirth) ? DBNull.Value : (object)dateOfBirth);
        cmd.Parameters.AddWithValue("@gender", string.IsNullOrWhiteSpace(gender)   ? DBNull.Value : (object)gender);
        cmd.Parameters.AddWithValue("@org", string.IsNullOrWhiteSpace(org)         ? DBNull.Value : (object)org);
        cmd.Parameters.AddWithValue("@ref", string.IsNullOrWhiteSpace(referral)    ? DBNull.Value : (object)referral);
        cmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var personId = (int)(long)idCmd.ExecuteScalar()!;

        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = "INSERT OR IGNORE INTO core_pcs (PcId) VALUES (@id)";
        pcCmd.Parameters.AddWithValue("@id", personId);
        pcCmd.ExecuteNonQuery();

        return personId;
    }

    // ── Visits ───────────────────────────────────────────────────────────────

    public List<VisitRecord> GetVisitsForDay(DateOnly date)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.StudentId, s.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   COALESCE(p.Referral,'') AS Referral,
                   COALESCE(p.Org,'')      AS Org
            FROM acad_attendance s
            JOIN core_persons p ON p.PersonId = s.PersonId
            WHERE s.VisitDate = @date AND COALESCE(p.IsActive, 1) = 1
            ORDER BY p.FirstName, p.LastName";
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        var list = new List<VisitRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new VisitRecord(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }

    /// <summary>Adds a visit row. Silently ignored if already visited that day.</summary>
    public void AddVisit(int personId, DateOnly date)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO acad_attendance (PersonId, VisitDate)
            VALUES (@pid, @date)";
        cmd.Parameters.AddWithValue("@pid",  personId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        cmd.ExecuteNonQuery();
    }

    public void RemoveVisit(int visitId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM acad_attendance WHERE StudentId = @id";
        cmd.Parameters.AddWithValue("@id", visitId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns all students who visited during the week, with visit count, referral, and org.</summary>
    public List<(int PersonId, string FullName, int VisitCount, string Referral, string Org)> GetStudentVisitsForWeek(DateOnly weekStart)
    {
        var weekEnd = weekStart.AddDays(6);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS FullName,
                   COUNT(*) AS VisitCount,
                   COALESCE(p.Referral,'') AS Referral,
                   COALESCE(p.Org,'')      AS Org
            FROM acad_attendance s
            JOIN core_persons p ON p.PersonId = s.PersonId
            WHERE s.VisitDate >= @start AND s.VisitDate <= @end
              AND COALESCE(p.IsActive, 1) = 1
            GROUP BY s.PersonId
            ORDER BY VisitCount DESC, FullName ASC";
        cmd.Parameters.AddWithValue("@start", weekStart.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@end",   weekEnd.ToString("yyyy-MM-dd"));
        var list = new List<(int, string, int, string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4)));
        return list;
    }

    // ── Weekly Breakdown ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns counts of unique students for the week, grouped by Referral and by Org.
    /// Empty/null Referral is grouped as "Don"; empty/null Org is omitted.
    /// </summary>
    public (Dictionary<string, int> ByReferral, Dictionary<string, int> ByOrg)
        GetWeeklyStudentBreakdown(DateOnly weekStart)
    {
        var weekEnd = weekStart.AddDays(6);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(p.Referral,'') AS Referral,
                   COALESCE(p.Org,'')      AS Org
            FROM acad_attendance s
            JOIN core_persons p ON p.PersonId = s.PersonId
            WHERE s.VisitDate >= @start AND s.VisitDate <= @end
            GROUP BY s.PersonId";
        cmd.Parameters.AddWithValue("@start", weekStart.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@end",   weekEnd.ToString("yyyy-MM-dd"));

        var byReferral = new Dictionary<string, int>(StringComparer.Ordinal);
        var byOrg      = new Dictionary<string, int>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var refKey = r.GetString(0) is { Length: > 0 } rf ? rf : "Don";
            byReferral[refKey] = byReferral.GetValueOrDefault(refKey) + 1;

            var org = r.GetString(1);
            if (org.Length > 0)
                byOrg[org] = byOrg.GetValueOrDefault(org) + 1;
        }
        return (byReferral, byOrg);
    }

    // ── Statistics ───────────────────────────────────────────────────────────

    public List<WeekVisitCount> GetWeeklyVisitCounts(DateOnly latestWeekStart, int numWeeks = 20)
    {
        var weekStarts = new List<DateOnly>(numWeeks);
        for (int i = numWeeks - 1; i >= 0; i--)
            weekStarts.Add(latestWeekStart.AddDays(-7 * i));

        var rangeStart = weekStarts[0];
        var rangeEnd   = latestWeekStart.AddDays(7);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.VisitDate,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   COALESCE(p.Referral,'') AS Referral,
                   COALESCE(p.Org,'')      AS Org
            FROM acad_attendance s
            JOIN core_persons p ON p.PersonId = s.PersonId
            WHERE s.VisitDate >= @start AND s.VisitDate < @end";
        cmd.Parameters.AddWithValue("@start", rangeStart.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@end",   rangeEnd.ToString("yyyy-MM-dd"));

        var totalCounts  = weekStarts.ToDictionary(ws => ws, _ => 0);
        var personCounts = weekStarts.ToDictionary(ws => ws, _ => new Dictionary<string, int>());
        var personOrgs   = weekStarts.ToDictionary(ws => ws, _ => new Dictionary<string, string>());
        var donCounts    = weekStarts.ToDictionary(ws => ws, _ => 0);
        var friendCounts = weekStarts.ToDictionary(ws => ws, _ => 0);
        var socialCounts = weekStarts.ToDictionary(ws => ws, _ => 0);
        var otherCounts  = weekStarts.ToDictionary(ws => ws, _ => 0);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var visitDate = DateOnly.Parse(r.GetString(0));
            var name      = r.GetString(1);
            var referral  = r.GetString(2);
            var org       = r.GetString(3);
            foreach (var ws in weekStarts)
            {
                if (visitDate >= ws && visitDate < ws.AddDays(7))
                {
                    totalCounts[ws]++;
                    personCounts[ws].TryGetValue(name, out var c);
                    personCounts[ws][name] = c + 1;
                    personOrgs[ws][name]   = org;
                    switch (referral)
                    {
                        case "Friend":          friendCounts[ws]++; break;
                        case "Social Networks": socialCounts[ws]++; break;
                        case "Other":           otherCounts[ws]++;  break;
                        default:                donCounts[ws]++;    break; // "Don" or empty → green
                    }
                    break;
                }
            }
        }

        return weekStarts
            .Select(ws => new WeekVisitCount(
                ws.ToString("dd/MM", CultureInfo.InvariantCulture),
                totalCounts[ws],
                personCounts[ws]
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key)
                    .Select(kv => new TopStudent(kv.Key, kv.Value,
                        personOrgs[ws].GetValueOrDefault(kv.Key, "")))
                    .ToList(),
                donCounts[ws],
                friendCounts[ws],
                socialCounts[ws],
                otherCounts[ws]))
            .ToList();
    }

    // ── Members admin ────────────────────────────────────────────────────────

    public List<MemberAdminItem> GetAllMembersForAdmin()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS FullName,
                   COALESCE(p.Phone,'') AS Phone,
                   COALESCE(p.IsActive, 1) AS IsActive,
                   CASE WHEN pc.PcId IS NOT NULL THEN 1 ELSE 0 END AS IsPC,
                   CASE WHEN vis.PersonId IS NOT NULL THEN 1 ELSE 0 END AS IsAcademy,
                   CASE WHEN aud.AuditorId IS NOT NULL OR cs.CsId IS NOT NULL THEN 1 ELSE 0 END AS IsStaff,
                   COALESCE(p.FirstName,'') AS FirstName,
                   COALESCE(p.LastName,'') AS LastName,
                   p.ExternalId,
                   MAX(COALESCE(sess.LastSession, ''), COALESCE(acad.LastAcademy, '')) AS LastVisitDate
            FROM core_persons p
            LEFT JOIN core_pcs pc ON pc.PcId = p.PersonId
            LEFT JOIN (SELECT DISTINCT PersonId FROM acad_attendance) vis ON vis.PersonId = p.PersonId
            LEFT JOIN sess_auditors aud ON aud.AuditorId = p.PersonId
            LEFT JOIN cs_case_supervisors cs ON cs.CsId = p.PersonId
            LEFT JOIN (SELECT PcId, MAX(SessionDate) AS LastSession FROM sess_sessions GROUP BY PcId) sess ON sess.PcId = p.PersonId
            LEFT JOIN (SELECT PersonId, MAX(VisitDate) AS LastAcademy FROM acad_attendance GROUP BY PersonId) acad ON acad.PersonId = p.PersonId
            ORDER BY COALESCE(p.IsActive,1) DESC, p.FirstName, p.LastName";
        var list = new List<MemberAdminItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var lastVisit = r.IsDBNull(10) ? null : r.GetString(10);
            if (string.IsNullOrEmpty(lastVisit)) lastVisit = null;
            list.Add(new MemberAdminItem(
                r.GetInt32(0), r.GetString(1).Trim(), r.GetString(2),
                r.GetInt32(3) == 1,
                r.GetInt32(4) == 1,
                r.GetInt32(5) == 1,
                r.GetInt32(6) == 1,
                r.GetString(7), r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                lastVisit));
        }
        return list;
    }

    public void UpdateAndActivatePerson(int personId, string firstName, string lastName, string? externalId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_persons SET FirstName=@fn, LastName=@ln, ExternalId=@eid, IsActive=1 WHERE PersonId=@id";
        cmd.Parameters.AddWithValue("@fn", firstName.Trim());
        cmd.Parameters.AddWithValue("@ln", lastName.Trim());
        cmd.Parameters.AddWithValue("@eid", (object?)externalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", personId);
        cmd.ExecuteNonQuery();
    }

    public void SetPersonActive(int personId, bool active)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_persons SET IsActive = @v WHERE PersonId = @id";
        cmd.Parameters.AddWithValue("@v",  active ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", personId);
        cmd.ExecuteNonQuery();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HashSet<string> GetTableNames(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.GetString(0));
        return names;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
