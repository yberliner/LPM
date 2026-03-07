using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record CourseItem(int CourseId, string Name);
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
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var c1 = conn.CreateCommand();
        c1.CommandText = @"
            CREATE TABLE IF NOT EXISTS Courses (
                CourseId INTEGER PRIMARY KEY AUTOINCREMENT,
                Name     TEXT    NOT NULL
            )";
        c1.ExecuteNonQuery();

        using var c2 = conn.CreateCommand();
        c2.CommandText = @"
            CREATE TABLE IF NOT EXISTS StudentCourses (
                StudentCourseId INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId        INTEGER NOT NULL,
                CourseId        INTEGER NOT NULL,
                DateStarted     TEXT    NOT NULL,
                DateFinished    TEXT    NULL
            )";
        c2.ExecuteNonQuery();

        // Ensure Payments table exists (PcService also creates this; whichever singleton
        // initialises first wins; CREATE IF NOT EXISTS is idempotent).
        // We need it here so the backfill below always works.
        using var c3 = conn.CreateCommand();
        c3.CommandText = @"
            CREATE TABLE IF NOT EXISTS Payments (
                PaymentId   INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId        INTEGER NOT NULL,
                PaymentDate TEXT    NOT NULL,
                HoursBought INTEGER NOT NULL DEFAULT 0,
                AmountPaid  INTEGER NOT NULL DEFAULT 0,
                Notes       TEXT,
                PaymentType TEXT    DEFAULT 'Auditing',
                CourseId    INTEGER,
                CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now'))
            )";
        c3.ExecuteNonQuery();

        // Backfill: any course payment that has no open StudentCourses row gets one.
        // This runs on every startup (idempotent — the NOT EXISTS guard prevents duplication).
        using var bf = conn.CreateCommand();
        bf.CommandText = @"
            INSERT INTO StudentCourses (PersonId, CourseId, DateStarted)
            SELECT p.PcId, p.CourseId, p.PaymentDate
            FROM Payments p
            WHERE COALESCE(p.PaymentType, 'Auditing') = 'Course'
              AND p.CourseId IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM StudentCourses sc
                  WHERE sc.PersonId = p.PcId
                    AND sc.CourseId = p.CourseId
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
        cmd.CommandText = "SELECT CourseId, Name FROM Courses ORDER BY Name";
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
        cmd.CommandText = "INSERT INTO Courses (Name) VALUES (@name)";
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
        cmd.CommandText = "UPDATE Courses SET Name=@name WHERE CourseId=@id";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@id", courseId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteCourse(int courseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Courses WHERE CourseId=@id";
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
            FROM StudentCourses sc
            JOIN Courses c ON c.CourseId = sc.CourseId
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
                   (SELECT COUNT(*) FROM Students s
                    WHERE s.PersonId = sc.PersonId AND s.VisitDate >= sc.DateStarted) AS VisitCount
            FROM StudentCourses sc
            JOIN Courses c ON c.CourseId = sc.CourseId
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
            INSERT INTO StudentCourses (PersonId, CourseId, DateStarted)
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
        cmd.CommandText = "UPDATE StudentCourses SET DateFinished=@df WHERE StudentCourseId=@id";
        cmd.Parameters.AddWithValue("@df", dateFinished);
        cmd.Parameters.AddWithValue("@id", studentCourseId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteStudentCourse(int studentCourseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM StudentCourses WHERE StudentCourseId=@id";
        cmd.Parameters.AddWithValue("@id", studentCourseId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Count of academy visits for this person on or after sinceDate.</summary>
    public int GetVisitCountSince(int personId, string sinceDate)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Students WHERE PersonId=@pid AND VisitDate >= @since";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@since", sinceDate);
        return (int)(long)cmd.ExecuteScalar()!;
    }
}
