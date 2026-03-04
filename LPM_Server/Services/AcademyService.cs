using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LPM.Services;

public record PersonItem(int PersonId, string FullName);
public record VisitRecord(int VisitId, int PersonId, string FullName);
public record TopStudent(string FullName, int VisitCount);
public record WeekVisitCount(string WeekLabel, int TotalVisits, List<TopStudent> TopStudents);

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
        AddPersonColumns(conn);
        EnsureStudentsTable(conn);
    }

    private static void AddPersonColumns(SqliteConnection conn)
    {
        var cols = GetColumnNames(conn, "Persons");
        foreach (var (col, type) in new[] { ("Phone", "TEXT"), ("Email", "TEXT"), ("Age", "INTEGER"), ("Sex", "TEXT") })
            if (!cols.Contains(col))
                Execute(conn, $"ALTER TABLE Persons ADD COLUMN {col} {type}");
    }

    private static void EnsureStudentsTable(SqliteConnection conn)
    {
        var tables = GetTableNames(conn);

        if (!tables.Contains("Students"))
        {
            Execute(conn, @"
                CREATE TABLE Students (
                    StudentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    PersonId  INTEGER NOT NULL,
                    VisitDate TEXT    NOT NULL,
                    UNIQUE (PersonId, VisitDate)
                )");
            return;
        }

        var cols = GetColumnNames(conn, "Students");
        if (cols.Contains("VisitDate")) return; // already migrated

        // Migrate old Students(StudentId,Notes,CreatedAt) + AcademyVisits → new Students
        using var txn = conn.BeginTransaction();
        Execute(conn, @"
            CREATE TABLE StudentsNew (
                StudentId INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId  INTEGER NOT NULL,
                VisitDate TEXT    NOT NULL,
                UNIQUE (PersonId, VisitDate)
            )");
        if (tables.Contains("AcademyVisits"))
        {
            Execute(conn, "INSERT OR IGNORE INTO StudentsNew (PersonId, VisitDate) SELECT StudentId, VisitDate FROM AcademyVisits");
            Execute(conn, "DROP TABLE AcademyVisits");
        }
        Execute(conn, "DROP TABLE Students");
        Execute(conn, "ALTER TABLE StudentsNew RENAME TO Students");
        txn.Commit();
    }

    // ── Persons ─────────────────────────────────────────────────────────────

    public List<PersonItem> GetAllPersons()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT PersonId,
                   TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''), '')) AS FullName
            FROM Persons
            ORDER BY FirstName, LastName";
        var list = new List<PersonItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PersonItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    /// <summary>Creates a new Person record and returns the new PersonId.</summary>
    public int AddPersonForAcademy(string firstName, string lastName,
        string phone, string email, int? age, string sex)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Persons (FirstName, LastName, Phone, Email, Age, Sex)
            VALUES (@fn, @ln, @ph, @em, @age, @sex)";
        cmd.Parameters.AddWithValue("@fn",  firstName.Trim());
        cmd.Parameters.AddWithValue("@ln",  lastName.Trim());
        cmd.Parameters.AddWithValue("@ph",  string.IsNullOrWhiteSpace(phone) ? DBNull.Value : (object)phone.Trim());
        cmd.Parameters.AddWithValue("@em",  string.IsNullOrWhiteSpace(email) ? DBNull.Value : (object)email.Trim());
        cmd.Parameters.AddWithValue("@age", age.HasValue ? (object)age.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@sex", string.IsNullOrWhiteSpace(sex)  ? DBNull.Value : (object)sex);
        cmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return (int)(long)idCmd.ExecuteScalar()!;
    }

    // ── Visits ───────────────────────────────────────────────────────────────

    public List<VisitRecord> GetVisitsForDay(DateOnly date)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.StudentId, s.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName
            FROM Students s
            JOIN Persons p ON p.PersonId = s.PersonId
            WHERE s.VisitDate = @date
            ORDER BY p.FirstName, p.LastName";
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        var list = new List<VisitRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new VisitRecord(r.GetInt32(0), r.GetInt32(1), r.GetString(2)));
        return list;
    }

    /// <summary>Adds a visit row. Silently ignored if already visited that day.</summary>
    public void AddVisit(int personId, DateOnly date)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO Students (PersonId, VisitDate)
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
        cmd.CommandText = "DELETE FROM Students WHERE StudentId = @id";
        cmd.Parameters.AddWithValue("@id", visitId);
        cmd.ExecuteNonQuery();
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
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName
            FROM Students s
            JOIN Persons p ON p.PersonId = s.PersonId
            WHERE s.VisitDate >= @start AND s.VisitDate < @end";
        cmd.Parameters.AddWithValue("@start", rangeStart.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@end",   rangeEnd.ToString("yyyy-MM-dd"));

        var totalCounts  = weekStarts.ToDictionary(ws => ws, _ => 0);
        var personCounts = weekStarts.ToDictionary(ws => ws, _ => new Dictionary<string, int>());

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var visitDate = DateOnly.Parse(r.GetString(0));
            var name      = r.GetString(1);
            foreach (var ws in weekStarts)
            {
                if (visitDate >= ws && visitDate < ws.AddDays(7))
                {
                    totalCounts[ws]++;
                    personCounts[ws].TryGetValue(name, out var c);
                    personCounts[ws][name] = c + 1;
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
                    .Select(kv => new TopStudent(kv.Key, kv.Value))
                    .ToList()))
            .ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HashSet<string> GetColumnNames(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1));
        return cols;
    }

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
