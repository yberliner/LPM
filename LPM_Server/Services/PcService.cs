using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record PcListItem(int PcId, string FullName, string ExternalId, long RemainSec);
public record PcDetailInfo(int PcId, string FirstName, string LastName, string ExternalId,
    string Phone, string Email, string Notes, string StartDate, string DateOfBirth, string Sex, string Origin)
{
    public string FullName => string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";
}
public record PcSessionInfo(int SessionId, string Date, string AuditorName,
    int LengthSec, int AdminSec, bool IsFree, string VerifiedStatus);
public record PcPayment(int PaymentId, string Date, int HoursBought, int AmountPaid, string? Notes,
    string PaymentType, int? CourseId, string? CourseName, int VisitCount,
    int? RegistrarId, string? RegistrarName, int? ReferralId, string? ReferralName);
public record PcStats(int TotalSessions, int FreeSessions, long UsedSec,
    int TotalHoursPurchased, int TotalAmountPaid, string? LastSessionDate);

public class PcService
{
    private readonly string _connectionString;

    public PcService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Payments table
        using var c1 = conn.CreateCommand();
        c1.CommandText = @"
            CREATE TABLE IF NOT EXISTS Payments (
                PaymentId   INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId        INTEGER NOT NULL,
                PaymentDate TEXT    NOT NULL,
                HoursBought INTEGER NOT NULL DEFAULT 0,
                AmountPaid  INTEGER NOT NULL DEFAULT 0,
                Notes       TEXT,
                CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
            )";
        c1.ExecuteNonQuery();

        // Extra columns on Payments
        foreach (var (col, def) in new[] {
            ("PaymentType",  "TEXT DEFAULT 'Auditing'"),
            ("CourseId",     "INTEGER"),
            ("RegistrarId",  "INTEGER"),
            ("ReferralId",   "INTEGER") })
        {
            using var ck = conn.CreateCommand();
            ck.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Payments') WHERE name='{col}'";
            if ((long)(ck.ExecuteScalar() ?? 0L) == 0)
            {
                using var alt = conn.CreateCommand();
                alt.CommandText = $"ALTER TABLE Payments ADD COLUMN {col} {def}";
                alt.ExecuteNonQuery();
            }
        }

        // Extra columns on PCs
        foreach (var (col, type) in new[] {
            ("Phone",     "TEXT"),   // kept for legacy read; new writes go to Persons
            ("Email",     "TEXT"),
            ("Notes",     "TEXT"),
            ("StartDate", "TEXT"),
            ("Origin",    "TEXT") })
        {
            using var ck = conn.CreateCommand();
            ck.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('PCs') WHERE name='{col}'";
            if ((long)(ck.ExecuteScalar() ?? 0L) == 0)
            {
                using var alt = conn.CreateCommand();
                alt.CommandText = $"ALTER TABLE PCs ADD COLUMN {col} {type}";
                alt.ExecuteNonQuery();
            }
        }

        // Ensure Persons has Phone/Email/DateOfBirth/Sex columns
        foreach (var (col, type) in new[] {
            ("Phone", "TEXT"), ("Email", "TEXT"), ("Age", "INTEGER"), ("DateOfBirth", "TEXT"), ("Sex", "TEXT") })
        {
            using var ck = conn.CreateCommand();
            ck.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Persons') WHERE name='{col}'";
            if ((long)(ck.ExecuteScalar() ?? 0L) == 0)
            {
                using var alt = conn.CreateCommand();
                alt.CommandText = $"ALTER TABLE Persons ADD COLUMN {col} {type}";
                alt.ExecuteNonQuery();
            }
        }

        // One-time migration: copy legacy Phone/Email from PCs → Persons
        using var migPhone = conn.CreateCommand();
        migPhone.CommandText = @"
            UPDATE Persons SET Phone = (SELECT Phone FROM PCs WHERE PCs.PcId = Persons.PersonId)
            WHERE PersonId IN (SELECT PcId FROM PCs WHERE Phone IS NOT NULL AND Phone != '')
              AND (Phone IS NULL OR Phone = '')";
        migPhone.ExecuteNonQuery();

        using var migEmail = conn.CreateCommand();
        migEmail.CommandText = @"
            UPDATE Persons SET Email = (SELECT Email FROM PCs WHERE PCs.PcId = Persons.PersonId)
            WHERE PersonId IN (SELECT PcId FROM PCs WHERE Email IS NOT NULL AND Email != '')
              AND (Email IS NULL OR Email = '')";
        migEmail.ExecuteNonQuery();
    }

    public List<PcListItem> GetAllPcs()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   COALESCE(pc.ExternalId, '') AS ExternalId,
                   (COALESCE(pay.TotalHours, 0) * 3600 - COALESCE(sess.UsedSec, 0)) AS RemainSec
            FROM PCs pc
            JOIN Persons p ON p.PersonId = pc.PcId
            LEFT JOIN (
                SELECT PcId, SUM(HoursBought) AS TotalHours
                FROM Payments GROUP BY PcId
            ) pay ON pay.PcId = pc.PcId
            LEFT JOIN (
                SELECT PcId, SUM(LengthSeconds) AS UsedSec
                FROM Sessions WHERE IsFreeSession = 0 GROUP BY PcId
            ) sess ON sess.PcId = pc.PcId
            ORDER BY RemainSec ASC, p.FirstName, p.LastName";
        var list = new List<PcListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcListItem(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt64(3)));
        return list;
    }

    public int AddPcWithPerson(string firstName, string lastName,
        string phone, string email, string dateOfBirth, string sex, string origin = "")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            INSERT INTO Persons (FirstName, LastName, Phone, Email, DateOfBirth, Sex)
            VALUES (@fn, @ln, @ph, @em, @dob, @sex)";
        pCmd.Parameters.AddWithValue("@fn",  firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln",  lastName.Trim());
        pCmd.Parameters.AddWithValue("@ph",  Nv(phone));
        pCmd.Parameters.AddWithValue("@em",  Nv(email));
        pCmd.Parameters.AddWithValue("@dob", Nv(dateOfBirth));
        pCmd.Parameters.AddWithValue("@sex", Nv(sex));
        pCmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var personId = (int)(long)idCmd.ExecuteScalar()!;

        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = "INSERT INTO PCs (PcId, Origin) VALUES (@id, @orig)";
        pcCmd.Parameters.AddWithValue("@id",   personId);
        pcCmd.Parameters.AddWithValue("@orig", Nv(origin));
        pcCmd.ExecuteNonQuery();

        return personId;
    }

    public PcDetailInfo? GetPcDetail(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.FirstName,             COALESCE(p.LastName,''),
                   COALESCE(pc.ExternalId,''), COALESCE(p.Phone,''),
                   COALESCE(p.Email,''),        COALESCE(pc.Notes,''),
                   COALESCE(pc.StartDate,''),   COALESCE(p.DateOfBirth,''), COALESCE(p.Sex,''),
                   COALESCE(pc.Origin,'')
            FROM PCs pc
            JOIN Persons p ON p.PersonId = pc.PcId
            WHERE pc.PcId = @id";
        cmd.Parameters.AddWithValue("@id", pcId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PcDetailInfo(pcId,
            r.GetString(0), r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
            r.GetString(7), r.GetString(8), r.GetString(9));
    }

    public void UpdatePcDetail(int pcId, string firstName, string lastName,
        string externalId, string phone, string email, string startDate, string notes,
        string dateOfBirth, string sex, string origin = "")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            UPDATE Persons SET FirstName=@fn, LastName=@ln,
                               Phone=@ph, Email=@em, DateOfBirth=@dob, Sex=@sex
            WHERE PersonId=@id";
        pCmd.Parameters.AddWithValue("@fn",  firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln",  lastName.Trim());
        pCmd.Parameters.AddWithValue("@ph",  Nv(phone));
        pCmd.Parameters.AddWithValue("@em",  Nv(email));
        pCmd.Parameters.AddWithValue("@dob", Nv(dateOfBirth));
        pCmd.Parameters.AddWithValue("@sex", Nv(sex));
        pCmd.Parameters.AddWithValue("@id",  pcId);
        pCmd.ExecuteNonQuery();

        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = @"
            UPDATE PCs SET ExternalId=@ext, StartDate=@sd, Notes=@nt, Origin=@orig
            WHERE PcId=@id";
        pcCmd.Parameters.AddWithValue("@ext",  Nv(externalId));
        pcCmd.Parameters.AddWithValue("@sd",   Nv(startDate));
        pcCmd.Parameters.AddWithValue("@nt",   Nv(notes));
        pcCmd.Parameters.AddWithValue("@orig", Nv(origin));
        pcCmd.Parameters.AddWithValue("@id",   pcId);
        pcCmd.ExecuteNonQuery();
    }

    public PcStats GetPcStats(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var sCmd = conn.CreateCommand();
        sCmd.CommandText = @"
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN IsFreeSession=1 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN IsFreeSession=0 THEN LengthSeconds ELSE 0 END), 0),
                   MAX(SessionDate)
            FROM Sessions WHERE PcId=@id";
        sCmd.Parameters.AddWithValue("@id", pcId);
        using var sr = sCmd.ExecuteReader();
        sr.Read();
        int    total     = sr.GetInt32(0);
        int    free      = sr.GetInt32(1);
        long   usedSec   = sr.GetInt64(2);
        string? lastDate = sr.IsDBNull(3) ? null : sr.GetString(3);

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            SELECT COALESCE(SUM(HoursBought),0), COALESCE(SUM(AmountPaid),0)
            FROM Payments WHERE PcId=@id";
        pCmd.Parameters.AddWithValue("@id", pcId);
        using var pr = pCmd.ExecuteReader();
        pr.Read();
        int hours  = pr.GetInt32(0);
        int amount = pr.GetInt32(1);

        return new PcStats(total, free, usedSec, hours, amount, lastDate);
    }

    public List<PcSessionInfo> GetPcSessions(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.SessionId, s.SessionDate, p.FirstName,
                   s.LengthSeconds, s.AdminSeconds, s.IsFreeSession,
                   COALESCE(s.VerifiedStatus,'Draft')
            FROM Sessions s
            JOIN Persons p ON p.PersonId = s.AuditorId
            WHERE s.PcId=@id
            ORDER BY s.SessionDate DESC, s.SequenceInDay DESC";
        cmd.Parameters.AddWithValue("@id", pcId);
        var list = new List<PcSessionInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcSessionInfo(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.GetInt32(3), r.GetInt32(4),
                r.GetInt32(5) == 1, r.GetString(6)));
        return list;
    }

    public List<PcPayment> GetPayments(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PaymentId, p.PaymentDate, p.HoursBought, p.AmountPaid, p.Notes,
                   COALESCE(p.PaymentType,'Auditing'), p.CourseId, c.Name,
                   CASE WHEN COALESCE(p.PaymentType,'Auditing') = 'Course' AND p.CourseId IS NOT NULL
                        THEN (SELECT COUNT(*) FROM Students s
                              WHERE s.PersonId = p.PcId AND s.VisitDate >= p.PaymentDate)
                        ELSE 0 END AS VisitCount,
                   p.RegistrarId,
                   TRIM(COALESCE(reg.FirstName,'') || ' ' || COALESCE(NULLIF(reg.LastName,''),'')) AS RegistrarName,
                   p.ReferralId,
                   TRIM(COALESCE(rf.FirstName,'') || ' ' || COALESCE(NULLIF(rf.LastName,''),'')) AS ReferralName
            FROM Payments p
            LEFT JOIN Courses c   ON c.CourseId    = p.CourseId
            LEFT JOIN Persons reg ON reg.PersonId  = p.RegistrarId
            LEFT JOIN Persons rf  ON rf.PersonId   = p.ReferralId
            WHERE p.PcId=@id
            ORDER BY p.PaymentDate DESC";
        cmd.Parameters.AddWithValue("@id", pcId);
        var list = new List<PcPayment>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcPayment(
                r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetInt32(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.GetInt32(8),
                r.IsDBNull(9)  ? null : r.GetInt32(9),
                r.IsDBNull(10) ? null : r.GetString(10).Trim(),
                r.IsDBNull(11) ? null : r.GetInt32(11),
                r.IsDBNull(12) ? null : r.GetString(12).Trim()));
        return list;
    }

    public void AddPayment(int pcId, string date, int hoursBought, int amountPaid, string? notes,
        string paymentType = "Auditing", int? courseId = null, int? registrarId = null, int? referralId = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Payments (PcId, PaymentDate, HoursBought, AmountPaid, Notes, PaymentType, CourseId, RegistrarId, ReferralId)
            VALUES (@pcId, @date, @hrs, @amt, @notes, @type, @cid, @regId, @refId)";
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@date",  date);
        cmd.Parameters.AddWithValue("@hrs",   hoursBought);
        cmd.Parameters.AddWithValue("@amt",   amountPaid);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type",  paymentType);
        cmd.Parameters.AddWithValue("@cid",   courseId.HasValue    ? (object)courseId.Value    : DBNull.Value);
        cmd.Parameters.AddWithValue("@regId", registrarId.HasValue ? (object)registrarId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@refId", referralId.HasValue  ? (object)referralId.Value  : DBNull.Value);
        cmd.ExecuteNonQuery();

        // Auto-enroll in Academy StudentCourses when a course is purchased
        if (paymentType == "Course" && courseId.HasValue)
        {
            using var scCmd = conn.CreateCommand();
            scCmd.CommandText = @"
                INSERT INTO StudentCourses (PersonId, CourseId, DateStarted)
                SELECT @pid, @cid, @date
                WHERE NOT EXISTS (
                    SELECT 1 FROM StudentCourses
                    WHERE PersonId=@pid AND CourseId=@cid AND DateFinished IS NULL
                )";
            scCmd.Parameters.AddWithValue("@pid",  pcId);
            scCmd.Parameters.AddWithValue("@cid",  courseId.Value);
            scCmd.Parameters.AddWithValue("@date", date);
            scCmd.ExecuteNonQuery();
        }
    }

    public void DeletePayment(int paymentId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Payments WHERE PaymentId=@id";
        cmd.Parameters.AddWithValue("@id", paymentId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>All active staff (Auditors + CaseSupervisors), ordered by first name.</summary>
    public List<(int PersonId, string FullName)> GetStaff()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS FullName
            FROM Persons p
            WHERE p.PersonId IN (
                SELECT AuditorId FROM Auditors WHERE COALESCE(Type,1) != 0
                UNION
                SELECT CsId FROM CaseSupervisors WHERE COALESCE(IsActive,1) = 1
            )
            ORDER BY p.FirstName";
        var list = new List<(int, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1).Trim()));
        return list;
    }

    /// <summary>All persons for referral lookup.</summary>
    public List<(int PersonId, string FullName)> GetAllPersons()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT PersonId, TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''),''))
            FROM Persons ORDER BY FirstName, LastName";
        var list = new List<(int, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1).Trim()));
        return list;
    }

    /// <summary>Look up PersonId by username (matched on FirstName, case-insensitive).</summary>
    public int? GetPersonIdByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PersonId FROM Persons WHERE LOWER(FirstName) = LOWER(@u) LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username.Trim());
        var result = cmd.ExecuteScalar();
        return result == null ? null : (int)(long)result;
    }

    private static object Nv(string? s) =>
        string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
}
