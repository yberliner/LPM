using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record CourseItem(int CourseId, string Name, string Book, int BookPrice, string CourseType);
public record BookItem(int BookId, string Name, int Price);
public record CourseEnrollmentItem(
    int StudentCourseId, int PersonId, string PCFullName,
    int CourseId, string CourseName, string DateStarted, string? DateFinished,
    int PaidAmount, int? RegistrarId, string? RegistrarName,
    int? ReferralId, string? ReferralName, int VisitCount,
    string CourseType, int? InstructorId, string? InstructorName, int? CsId, string? CsName);
public record StudentCourseItem(int StudentCourseId, int PersonId, int CourseId, string CourseName,
    string DateStarted, string? DateFinished,
    string CourseType, int? InstructorId, string? InstructorName, int? CsId, string? CsName);
public record OpenCourseEntry(string Name, int VisitCount, string CourseType);

public record FinancialConfig(
    double VatPct, double CcCommissionPct,
    double AuditRegistrarPct, double CourseRegistrarPct,
    double AuditReferralPct, double CourseReferralPct,
    double ReserveDeductPct, string AcademyInstructorIds);

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
            INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted, InstructorId, CsId)
            SELECT pu.PcId, pi.CourseId, pu.PurchaseDate,
              CASE WHEN c.CourseType='OT'
                   THEN (SELECT PersonId FROM core_users WHERE Username='aviv' AND IsActive=1 LIMIT 1) END,
              CASE WHEN c.CourseType='OT'
                   THEN (SELECT PersonId FROM core_users WHERE Username='tami' AND IsActive=1 LIMIT 1) END
            FROM fin_purchase_items pi
            JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
            JOIN lkp_courses c ON c.CourseId = pi.CourseId
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
        cmd.CommandText = "SELECT CourseId, Name, COALESCE(Book,'') AS Book, COALESCE(BookPrice,0) AS BookPrice, COALESCE(CourseType,'PC') FROM lkp_courses ORDER BY Name";
        var list = new List<CourseItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CourseItem(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4)));
        return list;
    }

    public int AddCourse(string name, string book = "", int bookPrice = 0, string courseType = "PC")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO lkp_courses (Name, Book, BookPrice, CourseType) VALUES (@name, @book, @bookPrice, @ct)";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@book", book.Trim());
        cmd.Parameters.AddWithValue("@bookPrice", bookPrice);
        cmd.Parameters.AddWithValue("@ct", courseType);
        cmd.ExecuteNonQuery();
        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var courseId = (int)(long)idCmd.ExecuteScalar()!;
        Console.WriteLine($"[CourseService] Added course {courseId}: '{name.Trim()}'");
        return courseId;
    }

    public void UpdateCourse(int courseId, string name, string book = "", int bookPrice = 0, string courseType = "PC")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE lkp_courses SET Name=@name, Book=@book, BookPrice=@bookPrice, CourseType=@ct WHERE CourseId=@id";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@book", book.Trim());
        cmd.Parameters.AddWithValue("@bookPrice", bookPrice);
        cmd.Parameters.AddWithValue("@ct", courseType);
        cmd.Parameters.AddWithValue("@id", courseId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[CourseService] Updated course {courseId}: '{name.Trim()}'");
    }

    public void DeleteCourse(int courseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM lkp_courses WHERE CourseId=@id";
        cmd.Parameters.AddWithValue("@id", courseId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[CourseService] Deleted course {courseId}");
    }

    // ── Books CRUD ───────────────────────────────────────────────────────────

    public List<BookItem> GetAllBooks()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT BookId, Name, COALESCE(Price,0) FROM lkp_books ORDER BY Name";
        var list = new List<BookItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BookItem(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));
        return list;
    }

    public void AddBook(string name, int price)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO lkp_books (Name, Price) VALUES (@name, @price)";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@price", price);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[CourseService] Added book: '{name.Trim()}' price={price}");
    }

    public void UpdateBook(int bookId, string name, int price)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE lkp_books SET Name=@name, Price=@price WHERE BookId=@id";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@price", price);
        cmd.Parameters.AddWithValue("@id", bookId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[CourseService] Updated book {bookId}: '{name.Trim()}' price={price}");
    }

    public void DeleteBook(int bookId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM lkp_books WHERE BookId=@id";
        cmd.Parameters.AddWithValue("@id", bookId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[CourseService] Deleted book {bookId}");
    }

    // ── Student Courses ───────────────────────────────────────────────────────

    public List<StudentCourseItem> GetStudentCourses(int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT sc.StudentCourseId, sc.PersonId, sc.CourseId, c.Name, sc.DateStarted, sc.DateFinished,
                   COALESCE(c.CourseType,'PC'),
                   sc.InstructorId,
                   TRIM(ip.FirstName || ' ' || COALESCE(NULLIF(ip.LastName,''),'')),
                   sc.CsId,
                   TRIM(cp.FirstName || ' ' || COALESCE(NULLIF(cp.LastName,''),''))
            FROM acad_student_courses sc
            JOIN lkp_courses c ON c.CourseId = sc.CourseId
            LEFT JOIN core_persons ip ON ip.PersonId = sc.InstructorId
            LEFT JOIN core_persons cp ON cp.PersonId = sc.CsId
            WHERE sc.PersonId = @pid
            ORDER BY sc.DateStarted DESC";
        cmd.Parameters.AddWithValue("@pid", personId);
        var list = new List<StudentCourseItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StudentCourseItem(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.GetString(6),
                r.IsDBNull(7) ? null : r.GetInt32(7),
                r.IsDBNull(8) ? null : r.GetString(8).Trim(),
                r.IsDBNull(9) ? null : r.GetInt32(9),
                r.IsDBNull(10) ? null : r.GetString(10).Trim()));
        return list;
    }

    /// <summary>Batch-load open courses for all given person IDs.
    /// Includes both explicit StudentCourses enrollments AND course purchases in Payments.
    /// Returns dict: PersonId → comma-joined course names.</summary>
    public Dictionary<int, List<OpenCourseEntry>> GetOpenCoursesForPersons(IEnumerable<int> personIds)
    {
        var ids = personIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var paramNames = ids.Select((_, i) => $"@p{i}").ToList();
        cmd.CommandText = $@"
            SELECT sc.PersonId, c.Name,
                   (SELECT COUNT(*) FROM acad_attendance s
                    WHERE s.PersonId = sc.PersonId AND s.VisitDate >= sc.DateStarted) AS VisitCount,
                   COALESCE(c.CourseType,'PC')
            FROM acad_student_courses sc
            JOIN lkp_courses c ON c.CourseId = sc.CourseId
            WHERE sc.PersonId IN ({string.Join(",", paramNames)}) AND sc.DateFinished IS NULL
            ORDER BY sc.PersonId, sc.DateStarted";
        for (int i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue($"@p{i}", ids[i]);
        var dict = new Dictionary<int, List<OpenCourseEntry>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var pid = r.GetInt32(0);
            if (!dict.TryGetValue(pid, out var lst)) dict[pid] = lst = [];
            var entry = new OpenCourseEntry(r.GetString(1), r.GetInt32(2), r.GetString(3));
            if (!lst.Any(e => e.Name == entry.Name)) lst.Add(entry);
        }
        return dict;
    }

    public int AddStudentCourse(int personId, int courseId, string dateStarted,
        int? instructorId = null, int? csId = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // For OT courses, auto-fill defaults if not provided
        using (var chk = conn.CreateCommand())
        {
            chk.CommandText = "SELECT COALESCE(CourseType,'PC') FROM lkp_courses WHERE CourseId=@cid";
            chk.Parameters.AddWithValue("@cid", courseId);
            var ct = chk.ExecuteScalar() as string ?? "PC";
            if (ct == "OT")
            {
                if (!instructorId.HasValue)
                {
                    using var q = conn.CreateCommand();
                    q.CommandText = "SELECT PersonId FROM core_users WHERE Username='aviv' AND IsActive=1 LIMIT 1";
                    var val = q.ExecuteScalar();
                    if (val != null) instructorId = (int)(long)val;
                }
                if (!csId.HasValue)
                {
                    using var q = conn.CreateCommand();
                    q.CommandText = "SELECT PersonId FROM core_users WHERE Username='tami' AND IsActive=1 LIMIT 1";
                    var val = q.ExecuteScalar();
                    if (val != null) csId = (int)(long)val;
                }
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted, InstructorId, CsId)
            VALUES (@pid, @cid, @ds, @iid, @csid)";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@cid", courseId);
        cmd.Parameters.AddWithValue("@ds", dateStarted);
        cmd.Parameters.AddWithValue("@iid", instructorId.HasValue ? (object)instructorId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@csid", csId.HasValue ? (object)csId.Value : DBNull.Value);
        cmd.ExecuteNonQuery();
        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var scId = (int)(long)idCmd.ExecuteScalar()!;
        Console.WriteLine($"[CourseService] Enrolled student {personId} in course {courseId}");
        return scId;
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
        Console.WriteLine($"[CourseService] Finished enrollment {studentCourseId}");
    }

    public void ReopenStudentCourse(int studentCourseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE acad_student_courses SET DateFinished = NULL WHERE StudentCourseId = @id";
        cmd.Parameters.AddWithValue("@id", studentCourseId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[CourseService] Reopened enrollment {studentCourseId}");
    }

    public void DeleteStudentCourse(int studentCourseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM acad_student_courses WHERE StudentCourseId=@id";
        cmd.Parameters.AddWithValue("@id", studentCourseId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[CourseService] Removed enrollment {studentCourseId}");
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
                   CASE WHEN pu.ReferralId = -1 THEN 'Other'
                        ELSE TRIM(COALESCE(rf.FirstName,'') || ' ' || COALESCE(NULLIF(rf.LastName,''),''))
                   END AS ReferralName,
                   (SELECT COUNT(*) FROM acad_attendance s
                    WHERE s.PersonId = sc.PersonId AND s.VisitDate >= sc.DateStarted) AS VisitCount,
                   COALESCE(c.CourseType,'PC'),
                   sc.InstructorId,
                   TRIM(COALESCE(insp.FirstName,'') || ' ' || COALESCE(NULLIF(insp.LastName,''),'')),
                   sc.CsId,
                   TRIM(COALESCE(csp.FirstName,'') || ' ' || COALESCE(NULLIF(csp.LastName,''),''))
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
            LEFT JOIN core_persons reg  ON reg.PersonId  = pu.RegistrarId
            LEFT JOIN core_persons rf   ON rf.PersonId   = pu.ReferralId
            LEFT JOIN core_persons insp ON insp.PersonId = sc.InstructorId
            LEFT JOIN core_persons csp  ON csp.PersonId  = sc.CsId
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
                r.GetInt32(12),
                r.GetString(13),
                r.IsDBNull(14) ? null : r.GetInt32(14),
                r.IsDBNull(15) ? null : r.GetString(15).Trim(),
                r.IsDBNull(16) ? null : r.GetInt32(16),
                r.IsDBNull(17) ? null : r.GetString(17).Trim()));
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

    // ── Financial Config ─────────────────────────────────────────────────────

    public FinancialConfig GetFinancialConfig()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT VatPct, CcCommissionPct, AuditRegistrarPct, CourseRegistrarPct,
                   AuditReferralPct, CourseReferralPct, ReserveDeductPct,
                   COALESCE(AcademyInstructorIds,'')
            FROM sys_financial_config WHERE Id = 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return new FinancialConfig(17, 2.5, 10, 10, 5, 5, 0.1, "");
        return new FinancialConfig(
            r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3),
            r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetString(7));
    }

    public void UpdateFinancialConfig(double vatPct, double ccCommissionPct,
        double auditRegistrarPct, double courseRegistrarPct,
        double auditReferralPct, double courseReferralPct,
        double reserveDeductPct, string academyInstructorIds)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sys_financial_config SET
                VatPct = @vat, CcCommissionPct = @cc,
                AuditRegistrarPct = @auditReg, CourseRegistrarPct = @courseReg,
                AuditReferralPct = @auditRef, CourseReferralPct = @courseRef,
                ReserveDeductPct = @reserve, AcademyInstructorIds = @instructors
            WHERE Id = 1";
        cmd.Parameters.AddWithValue("@vat", vatPct);
        cmd.Parameters.AddWithValue("@cc", ccCommissionPct);
        cmd.Parameters.AddWithValue("@auditReg", auditRegistrarPct);
        cmd.Parameters.AddWithValue("@courseReg", courseRegistrarPct);
        cmd.Parameters.AddWithValue("@auditRef", auditReferralPct);
        cmd.Parameters.AddWithValue("@courseRef", courseReferralPct);
        cmd.Parameters.AddWithValue("@reserve", reserveDeductPct);
        cmd.Parameters.AddWithValue("@instructors", academyInstructorIds.Trim());
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[CourseService] Updated financial config: VAT={vatPct} CC={ccCommissionPct} AuditReg={auditRegistrarPct} CourseReg={courseRegistrarPct} AuditRef={auditReferralPct} CourseRef={courseReferralPct} Reserve={reserveDeductPct} Instructors={academyInstructorIds}");
    }

    public List<(int Id, string FullName)> GetCoreUsers()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.PersonId, TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),''))
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.IsActive = 1 AND u.StaffRole != 'Solo'
            ORDER BY p.FirstName, p.LastName";
        var list = new List<(int, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1).Trim()));
        return list;
    }

    public List<(int Id, string FullName)> GetCsUsers()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.PersonId, TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),''))
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.IsActive = 1 AND u.StaffRole IN ('CS','SeniorCS')
            ORDER BY p.FirstName, p.LastName";
        var list = new List<(int, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1).Trim()));
        return list;
    }

    public void UpdateStudentCourseStaff(int studentCourseId, int? instructorId, int? csId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE acad_student_courses SET InstructorId=@iid, CsId=@cid WHERE StudentCourseId=@id";
        cmd.Parameters.AddWithValue("@iid", instructorId.HasValue ? (object)instructorId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@cid", csId.HasValue ? (object)csId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@id", studentCourseId);
        cmd.ExecuteNonQuery();
    }

    public int? ResolveDefaultInstructorId()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PersonId FROM core_users WHERE Username='aviv' AND IsActive=1 LIMIT 1";
        var val = cmd.ExecuteScalar();
        return val != null ? (int)(long)val : null;
    }

    public int? ResolveDefaultCsId()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PersonId FROM core_users WHERE Username='tami' AND IsActive=1 LIMIT 1";
        var val = cmd.ExecuteScalar();
        return val != null ? (int)(long)val : null;
    }

    public Dictionary<int, string> GetPersonNamesByIds(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return new();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var paramNames = idList.Select((_, i) => $"@p{i}").ToList();
        cmd.CommandText = $@"
            SELECT PersonId, TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''),''))
            FROM core_persons WHERE PersonId IN ({string.Join(",", paramNames)})";
        for (int i = 0; i < idList.Count; i++)
            cmd.Parameters.AddWithValue($"@p{i}", idList[i]);
        var dict = new Dictionary<int, string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            dict[r.GetInt32(0)] = r.GetString(1).Trim();
        return dict;
    }
}
