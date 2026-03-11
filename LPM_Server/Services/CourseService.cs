using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record CourseItem(int CourseId, string Name);
public record CourseEnrollmentItem(
    int StudentCourseId, int PersonId, string PCFullName,
    int CourseId, string CourseName, string DateStarted, string? DateFinished,
    int PaidAmount, int? RegistrarId, string? RegistrarName,
    int? ReferralId, string? ReferralName, int VisitCount);
public record StudentCourseItem(int StudentCourseId, int PersonId, int CourseId, string CourseName,
    string DateStarted, string? DateFinished);

public class CourseService
{
    private readonly string _connectionString;

    public CourseService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        // Schema managed directly in DB — no CREATE TABLE statements here.

        // Backfill: any course purchase item that has no open StudentCourses row gets one.
        // This runs on every startup (idempotent — the NOT EXISTS guard prevents duplication).
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var bf = conn.CreateCommand();
        bf.CommandText = @"
            INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted)
            SELECT pu.PcId, pi.CourseId, pu.PurchaseDate
            FROM fin_purchase_items pi
            JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
            WHERE pi.ItemType = 'Course'
              AND pi.CourseId IS NOT NULL
              AND pu.IsDeleted = 0
              AND NOT EXISTS (
                  SELECT 1 FROM acad_student_courses sc
                  WHERE sc.PersonId = pu.PcId
                    AND sc.CourseId = pi.CourseId
                    AND sc.DateFinished IS NULL
              )";
        bf.ExecuteNonQuery();
    }

    // ── Courses CRUD ─────────────────────────────────────────────────────────

    public List<CourseItem> GetAllCourses()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CourseId, Name FROM lkp_courses ORDER BY Name";
        var list = new List<CourseItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CourseItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public int AddCourse(string name)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO lkp_courses (Name) VALUES (@name)";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.ExecuteNonQuery();
        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return (int)(long)idCmd.ExecuteScalar()!;
    }

    public void UpdateCourse(int courseId, string name)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE lkp_courses SET Name=@name WHERE CourseId=@id";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@id", courseId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteCourse(int courseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM lkp_courses WHERE CourseId=@id";
        cmd.Parameters.AddWithValue("@id", courseId);
        cmd.ExecuteNonQuery();
    }

    // ── Student Courses ───────────────────────────────────────────────────────

    public List<StudentCourseItem> GetStudentCourses(int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT sc.StudentCourseId, sc.PersonId, sc.CourseId, c.Name, sc.DateStarted, sc.DateFinished
            FROM acad_student_courses sc
            JOIN lkp_courses c ON c.CourseId = sc.CourseId
            WHERE sc.PersonId = @pid
            ORDER BY sc.DateStarted DESC";
        cmd.Parameters.AddWithValue("@pid", personId);
        var list = new List<StudentCourseItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StudentCourseItem(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    /// <summary>Batch-load open courses for all given person IDs.
    /// Includes both explicit StudentCourses enrollments AND course purchases in Payments.
    /// Returns dict: PersonId → comma-joined course names.</summary>
    public Dictionary<int, string> GetOpenCoursesForPersons(IEnumerable<int> personIds)
    {
        var ids = personIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var inClause = string.Join(",", ids);
        cmd.CommandText = $@"
            SELECT sc.PersonId, c.Name,
                   (SELECT COUNT(*) FROM acad_attendance s
                    WHERE s.PersonId = sc.PersonId AND s.VisitDate >= sc.DateStarted) AS VisitCount
            FROM acad_student_courses sc
            JOIN lkp_courses c ON c.CourseId = sc.CourseId
            WHERE sc.PersonId IN ({inClause}) AND sc.DateFinished IS NULL
            ORDER BY sc.PersonId, sc.DateStarted";
        var dict = new Dictionary<int, List<string>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var pid = r.GetInt32(0);
            if (!dict.TryGetValue(pid, out var lst)) dict[pid] = lst = [];
            var name = r.GetString(1);
            var visits = r.GetInt32(2);
            var entry = $"{name} ({visits})";
            if (!lst.Contains(entry)) lst.Add(entry);
        }
        return dict.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value));
    }

    public int AddStudentCourse(int personId, int courseId, string dateStarted)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted)
            VALUES (@pid, @cid, @ds)";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@cid", courseId);
        cmd.Parameters.AddWithValue("@ds", dateStarted);
        cmd.ExecuteNonQuery();
        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return (int)(long)idCmd.ExecuteScalar()!;
    }

    public void FinishStudentCourse(int studentCourseId, string dateFinished)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE acad_student_courses SET DateFinished=@df WHERE StudentCourseId=@id";
        cmd.Parameters.AddWithValue("@df", dateFinished);
        cmd.Parameters.AddWithValue("@id", studentCourseId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteStudentCourse(int studentCourseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM acad_student_courses WHERE StudentCourseId=@id";
        cmd.Parameters.AddWithValue("@id", studentCourseId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>All course enrollments with payment/registrar/referral/visit info for admin report.</summary>
    public List<CourseEnrollmentItem> GetCourseEnrollmentReport()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT sc.StudentCourseId, sc.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS PCFullName,
                   c.CourseId, c.Name AS CourseName,
                   sc.DateStarted, sc.DateFinished,
                   COALESCE(pi.AmountPaid, 0),
                   pu.RegistrarId,
                   TRIM(COALESCE(reg.FirstName,'') || ' ' || COALESCE(NULLIF(reg.LastName,''),'')) AS RegistrarName,
                   pu.ReferralId,
                   TRIM(COALESCE(rf.FirstName,'') || ' ' || COALESCE(NULLIF(rf.LastName,''),'')) AS ReferralName,
                   (SELECT COUNT(*) FROM acad_attendance s
                    WHERE s.PersonId = sc.PersonId AND s.VisitDate >= sc.DateStarted) AS VisitCount
            FROM acad_student_courses sc
            JOIN core_persons p ON p.PersonId = sc.PersonId
            JOIN lkp_courses c ON c.CourseId = sc.CourseId
            LEFT JOIN (
                SELECT pu2.PcId, pi2.CourseId, MAX(pi2.PurchaseItemId) AS MaxItemId
                FROM fin_purchase_items pi2
                JOIN fin_purchases pu2 ON pu2.PurchaseId = pi2.PurchaseId
                WHERE pi2.ItemType = 'Course' AND pu2.IsDeleted = 0
                GROUP BY pu2.PcId, pi2.CourseId
            ) latest ON latest.PcId = sc.PersonId AND latest.CourseId = sc.CourseId
            LEFT JOIN fin_purchase_items pi ON pi.PurchaseItemId = latest.MaxItemId
            LEFT JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
            LEFT JOIN core_persons reg ON reg.PersonId = pu.RegistrarId
            LEFT JOIN core_persons rf  ON rf.PersonId  = pu.ReferralId
            ORDER BY c.Name, p.FirstName, p.LastName";
        var list = new List<CourseEnrollmentItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CourseEnrollmentItem(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2).Trim(),
                r.GetInt32(3), r.GetString(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.GetInt32(7),
                r.IsDBNull(8)  ? null : r.GetInt32(8),
                r.IsDBNull(9)  ? null : r.GetString(9).Trim(),
                r.IsDBNull(10) ? null : r.GetInt32(10),
                r.IsDBNull(11) ? null : r.GetString(11).Trim(),
                r.GetInt32(12)));
        return list;
    }

    /// <summary>Count of academy visits for this person on or after sinceDate.</summary>
    public int GetVisitCountSince(int personId, string sinceDate)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM acad_attendance WHERE PersonId=@pid AND VisitDate >= @since";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@since", sinceDate);
        return (int)(long)cmd.ExecuteScalar()!;
    }
}
