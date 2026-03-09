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
public record PcListItemEx(int PcId, string FullName, string ExternalId, long RemainSec,
    long TotalSessionSec, int TotalSessions, int AcademyVisits, int HoursPurchased);
public record PurchaseListItem(int PurchaseId, int PcId, string PcName, string PurchaseDate,
    string? Notes, string ApprovedStatus, string? ApprovedByName, string? ApprovedAt,
    string? CreatedByName, string CreatedAt, int TotalAmount, int TotalHours, bool IsDeleted = false);
public record PurchaseItemInfo(int PurchaseItemId, string ItemType, int? CourseId,
    string? CourseName, int HoursBought, int AmountPaid, int? RegistrarId,
    string? RegistrarName, int? ReferralId, string? ReferralName);
public record PurchaseDetail(int PurchaseId, int PcId, string PcName, string PurchaseDate,
    string? Notes, string? SignatureData, string ApprovedStatus, string? ApprovedByName,
    string? CreatedByName, List<PurchaseItemInfo> Items, List<PurchasePaymentMethodInfo> PaymentMethods);
public record PurchasePaymentMethodInfo(int PaymentMethodId, string MethodType,
    int Amount, string? PaymentDate, bool IsMoneyInBank, string? MoneyInBankDate);

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

        // Ensure Persons has Phone/Email/DateOfBirth/Sex/IsActive/ExternalId columns
        foreach (var (col, type) in new[] {
            ("Phone", "TEXT"), ("Email", "TEXT"), ("Age", "INTEGER"), ("DateOfBirth", "TEXT"), ("Sex", "TEXT"),
            ("IsActive", "INTEGER NOT NULL DEFAULT 1"), ("ExternalId", "TEXT") })
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

        // Purchases & PurchaseItems tables
        using var c2 = conn.CreateCommand();
        c2.CommandText = @"
            CREATE TABLE IF NOT EXISTS Purchases (
                PurchaseId         INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId               INTEGER NOT NULL,
                PurchaseDate       TEXT NOT NULL,
                Notes              TEXT,
                SignatureData      TEXT,
                ApprovedStatus     TEXT NOT NULL DEFAULT 'Pending',
                ApprovedByPersonId INTEGER,
                ApprovedAt         TEXT,
                CreatedByPersonId  INTEGER,
                CreatedAt          TEXT NOT NULL DEFAULT (datetime('now'))
            )";
        c2.ExecuteNonQuery();

        using var c3 = conn.CreateCommand();
        c3.CommandText = @"
            CREATE TABLE IF NOT EXISTS PurchaseItems (
                PurchaseItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                PurchaseId     INTEGER NOT NULL,
                ItemType       TEXT NOT NULL DEFAULT 'Auditing',
                CourseId       INTEGER,
                HoursBought    INTEGER NOT NULL DEFAULT 0,
                AmountPaid     INTEGER NOT NULL DEFAULT 0,
                RegistrarId    INTEGER,
                ReferralId     INTEGER,
                FOREIGN KEY (PurchaseId) REFERENCES Purchases(PurchaseId)
            )";
        c3.ExecuteNonQuery();

        // PurchasePaymentMethods table
        using var c4 = conn.CreateCommand();
        c4.CommandText = @"
            CREATE TABLE IF NOT EXISTS PurchasePaymentMethods (
                PaymentMethodId INTEGER PRIMARY KEY AUTOINCREMENT,
                PurchaseId      INTEGER NOT NULL,
                MethodType      TEXT NOT NULL DEFAULT 'Cash',
                Amount          INTEGER NOT NULL DEFAULT 0,
                PaymentDate     TEXT,
                IsMoneyInBank   INTEGER NOT NULL DEFAULT 0,
                MoneyInBankDate TEXT,
                FOREIGN KEY (PurchaseId) REFERENCES Purchases(PurchaseId)
            )";
        c4.ExecuteNonQuery();

        // Add IsDeleted column to Purchases
        {
            using var ck = conn.CreateCommand();
            ck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Purchases') WHERE name='IsDeleted'";
            if ((long)(ck.ExecuteScalar() ?? 0L) == 0)
            {
                using var alt = conn.CreateCommand();
                alt.CommandText = "ALTER TABLE Purchases ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0";
                alt.ExecuteNonQuery();
            }
        }

        // Add PurchaseId column to Payments for linking
        {
            using var ck = conn.CreateCommand();
            ck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Payments') WHERE name='PurchaseId'";
            if ((long)(ck.ExecuteScalar() ?? 0L) == 0)
            {
                using var alt = conn.CreateCommand();
                alt.CommandText = "ALTER TABLE Payments ADD COLUMN PurchaseId INTEGER";
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
            WHERE COALESCE(p.IsActive, 1) = 1
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
            FROM Persons WHERE COALESCE(IsActive,1) = 1 ORDER BY FirstName, LastName";
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

    // ── Extended PC list with stats for table view ────────────────

    public List<PcListItemEx> GetAllPcsExtended()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   COALESCE(pc.ExternalId, '') AS ExternalId,
                   (COALESCE(pay.TotalHours, 0) * 3600 - COALESCE(sess.UsedSec, 0)) AS RemainSec,
                   COALESCE(sess.UsedSec, 0) AS TotalSessionSec,
                   COALESCE(sess.SessionCount, 0) AS TotalSessions,
                   COALESCE(acad.VisitCount, 0) AS AcademyVisits,
                   COALESCE(pay.TotalHours, 0) AS HoursPurchased
            FROM PCs pc
            JOIN Persons p ON p.PersonId = pc.PcId
            LEFT JOIN (
                SELECT PcId, SUM(HoursBought) AS TotalHours
                FROM Payments GROUP BY PcId
            ) pay ON pay.PcId = pc.PcId
            LEFT JOIN (
                SELECT PcId, SUM(LengthSeconds) AS UsedSec, COUNT(*) AS SessionCount
                FROM Sessions WHERE IsFreeSession = 0 GROUP BY PcId
            ) sess ON sess.PcId = pc.PcId
            LEFT JOIN (
                SELECT PersonId, COUNT(*) AS VisitCount
                FROM Students GROUP BY PersonId
            ) acad ON acad.PersonId = pc.PcId
            WHERE COALESCE(p.IsActive, 1) = 1
            ORDER BY RemainSec ASC, p.FirstName, p.LastName";
        var list = new List<PcListItemEx>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcListItemEx(
                r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt64(3),
                r.GetInt64(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7)));
        return list;
    }

    // ── Purchase methods ────────────────────────────────────────────

    public int CreatePurchase(int pcId, string date, string? notes, string? signatureData,
        int? createdByPersonId,
        List<(string itemType, int? courseId, int hoursBought, int amountPaid, int? registrarId, int? referralId)> items,
        List<(string methodType, int amount, string? paymentDate)>? paymentMethods = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO Purchases (PcId, PurchaseDate, Notes, SignatureData, CreatedByPersonId)
            VALUES (@pcId, @date, @notes, @sig, @createdBy)";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@date", date);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sig", (object?)signatureData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy", createdByPersonId.HasValue ? (object)createdByPersonId.Value : DBNull.Value);
        cmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.Transaction = tx;
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var purchaseId = (int)(long)idCmd.ExecuteScalar()!;

        foreach (var item in items)
        {
            using var iCmd = conn.CreateCommand();
            iCmd.Transaction = tx;
            iCmd.CommandText = @"
                INSERT INTO PurchaseItems (PurchaseId, ItemType, CourseId, HoursBought, AmountPaid, RegistrarId, ReferralId)
                VALUES (@pid, @type, @cid, @hrs, @amt, @regId, @refId)";
            iCmd.Parameters.AddWithValue("@pid", purchaseId);
            iCmd.Parameters.AddWithValue("@type", item.itemType);
            iCmd.Parameters.AddWithValue("@cid", item.courseId.HasValue ? (object)item.courseId.Value : DBNull.Value);
            iCmd.Parameters.AddWithValue("@hrs", item.hoursBought);
            iCmd.Parameters.AddWithValue("@amt", item.amountPaid);
            iCmd.Parameters.AddWithValue("@regId", item.registrarId.HasValue ? (object)item.registrarId.Value : DBNull.Value);
            iCmd.Parameters.AddWithValue("@refId", item.referralId.HasValue ? (object)item.referralId.Value : DBNull.Value);
            iCmd.ExecuteNonQuery();

            // Auto-enroll in Academy StudentCourses when a course is purchased
            if (item.itemType == "Course" && item.courseId.HasValue)
            {
                using var scCmd = conn.CreateCommand();
                scCmd.Transaction = tx;
                scCmd.CommandText = @"
                    INSERT INTO StudentCourses (PersonId, CourseId, DateStarted)
                    SELECT @personId, @courseId, @date
                    WHERE NOT EXISTS (
                        SELECT 1 FROM StudentCourses
                        WHERE PersonId=@personId AND CourseId=@courseId AND DateFinished IS NULL
                    )";
                scCmd.Parameters.AddWithValue("@personId", pcId);
                scCmd.Parameters.AddWithValue("@courseId", item.courseId.Value);
                scCmd.Parameters.AddWithValue("@date", date);
                scCmd.ExecuteNonQuery();
            }
        }

        // Insert payment methods
        if (paymentMethods != null)
        {
            foreach (var pm in paymentMethods)
            {
                using var pmCmd = conn.CreateCommand();
                pmCmd.Transaction = tx;
                pmCmd.CommandText = @"
                    INSERT INTO PurchasePaymentMethods (PurchaseId, MethodType, Amount, PaymentDate)
                    VALUES (@pid, @type, @amt, @date)";
                pmCmd.Parameters.AddWithValue("@pid", purchaseId);
                pmCmd.Parameters.AddWithValue("@type", pm.methodType);
                pmCmd.Parameters.AddWithValue("@amt", pm.amount);
                pmCmd.Parameters.AddWithValue("@date", (object?)pm.paymentDate ?? DBNull.Value);
                pmCmd.ExecuteNonQuery();
            }
        }

        // Also insert into Payments table for backward compatibility (hours tracking)
        foreach (var item in items)
        {
            using var payCmd = conn.CreateCommand();
            payCmd.Transaction = tx;
            payCmd.CommandText = @"
                INSERT INTO Payments (PcId, PaymentDate, HoursBought, AmountPaid, Notes, PaymentType, CourseId, RegistrarId, ReferralId, PurchaseId)
                VALUES (@pcId, @date, @hrs, @amt, @notes, @type, @cid, @regId, @refId, @purchaseId)";
            payCmd.Parameters.AddWithValue("@pcId", pcId);
            payCmd.Parameters.AddWithValue("@date", date);
            payCmd.Parameters.AddWithValue("@hrs", item.hoursBought);
            payCmd.Parameters.AddWithValue("@amt", item.amountPaid);
            payCmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@type", item.itemType);
            payCmd.Parameters.AddWithValue("@cid", item.courseId.HasValue ? (object)item.courseId.Value : DBNull.Value);
            payCmd.Parameters.AddWithValue("@regId", item.registrarId.HasValue ? (object)item.registrarId.Value : DBNull.Value);
            payCmd.Parameters.AddWithValue("@refId", item.referralId.HasValue ? (object)item.referralId.Value : DBNull.Value);
            payCmd.Parameters.AddWithValue("@purchaseId", purchaseId);
            payCmd.ExecuteNonQuery();
        }

        tx.Commit();
        return purchaseId;
    }

    public List<PurchaseListItem> GetPurchases(bool includeApproved, DateOnly? from = null, DateOnly? to = null, bool includeDeleted = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var where = new List<string>();
        if (!includeDeleted)
            where.Add("COALESCE(p.IsDeleted, 0) = 0");
        if (!includeApproved)
            where.Add("p.ApprovedStatus != 'Approved'");
        if (from.HasValue)
        {
            where.Add("p.PurchaseDate >= @from");
            cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd"));
        }
        if (to.HasValue)
        {
            where.Add("p.PurchaseDate <= @to");
            cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd"));
        }
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        cmd.CommandText = $@"
            SELECT p.PurchaseId, p.PcId,
                   TRIM(per.FirstName || ' ' || COALESCE(NULLIF(per.LastName,''), '')) AS PcName,
                   p.PurchaseDate, p.Notes, p.ApprovedStatus,
                   TRIM(COALESCE(ap.FirstName,'') || ' ' || COALESCE(NULLIF(ap.LastName,''),'')) AS ApprovedByName,
                   p.ApprovedAt,
                   TRIM(COALESCE(cr.FirstName,'') || ' ' || COALESCE(NULLIF(cr.LastName,''),'')) AS CreatedByName,
                   p.CreatedAt,
                   COALESCE(items.TotalAmount, 0),
                   COALESCE(items.TotalHours, 0),
                   COALESCE(p.IsDeleted, 0)
            FROM Purchases p
            JOIN Persons per ON per.PersonId = p.PcId
            LEFT JOIN Persons ap ON ap.PersonId = p.ApprovedByPersonId
            LEFT JOIN Persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN (
                SELECT PurchaseId, SUM(AmountPaid) AS TotalAmount, SUM(HoursBought) AS TotalHours
                FROM PurchaseItems GROUP BY PurchaseId
            ) items ON items.PurchaseId = p.PurchaseId
            {whereClause}
            ORDER BY p.PurchaseDate DESC, p.PurchaseId DESC";

        var list = new List<PurchaseListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PurchaseListItem(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2).Trim(), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6).Trim(),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8).Trim(),
                r.GetString(9),
                r.GetInt32(10), r.GetInt32(11),
                r.GetInt32(12) == 1));
        return list;
    }

    public PurchaseDetail? GetPurchaseDetail(int purchaseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PurchaseId, p.PcId,
                   TRIM(per.FirstName || ' ' || COALESCE(NULLIF(per.LastName,''), '')) AS PcName,
                   p.PurchaseDate, p.Notes, p.SignatureData, p.ApprovedStatus,
                   TRIM(COALESCE(ap.FirstName,'') || ' ' || COALESCE(NULLIF(ap.LastName,''),'')) AS ApprovedByName,
                   TRIM(COALESCE(cr.FirstName,'') || ' ' || COALESCE(NULLIF(cr.LastName,''),'')) AS CreatedByName
            FROM Purchases p
            JOIN Persons per ON per.PersonId = p.PcId
            LEFT JOIN Persons ap ON ap.PersonId = p.ApprovedByPersonId
            LEFT JOIN Persons cr ON cr.PersonId = p.CreatedByPersonId
            WHERE p.PurchaseId = @id";
        cmd.Parameters.AddWithValue("@id", purchaseId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var items = new List<PurchaseItemInfo>();
        var paymentMethods = new List<PurchasePaymentMethodInfo>();

        var detail = new PurchaseDetail(
            r.GetInt32(0), r.GetInt32(1), r.GetString(2).Trim(), r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7).Trim(),
            r.IsDBNull(8) ? null : r.GetString(8).Trim(),
            items, paymentMethods);
        r.Close();

        using var iCmd = conn.CreateCommand();
        iCmd.CommandText = @"
            SELECT pi.PurchaseItemId, pi.ItemType, pi.CourseId, c.Name,
                   pi.HoursBought, pi.AmountPaid,
                   pi.RegistrarId,
                   TRIM(COALESCE(reg.FirstName,'') || ' ' || COALESCE(NULLIF(reg.LastName,''),'')) AS RegistrarName,
                   pi.ReferralId,
                   TRIM(COALESCE(rf.FirstName,'') || ' ' || COALESCE(NULLIF(rf.LastName,''),'')) AS ReferralName
            FROM PurchaseItems pi
            LEFT JOIN Courses c ON c.CourseId = pi.CourseId
            LEFT JOIN Persons reg ON reg.PersonId = pi.RegistrarId
            LEFT JOIN Persons rf ON rf.PersonId = pi.ReferralId
            WHERE pi.PurchaseId = @id
            ORDER BY pi.PurchaseItemId";
        iCmd.Parameters.AddWithValue("@id", purchaseId);
        using var ir = iCmd.ExecuteReader();
        while (ir.Read())
            items.Add(new PurchaseItemInfo(
                ir.GetInt32(0), ir.GetString(1),
                ir.IsDBNull(2) ? null : ir.GetInt32(2),
                ir.IsDBNull(3) ? null : ir.GetString(3),
                ir.GetInt32(4), ir.GetInt32(5),
                ir.IsDBNull(6) ? null : ir.GetInt32(6),
                ir.IsDBNull(7) ? null : ir.GetString(7).Trim(),
                ir.IsDBNull(8) ? null : ir.GetInt32(8),
                ir.IsDBNull(9) ? null : ir.GetString(9).Trim()));
        ir.Close();

        // Load payment methods
        using var pmCmd = conn.CreateCommand();
        pmCmd.CommandText = @"
            SELECT PaymentMethodId, MethodType, Amount, PaymentDate, IsMoneyInBank, MoneyInBankDate
            FROM PurchasePaymentMethods WHERE PurchaseId = @id ORDER BY PaymentMethodId";
        pmCmd.Parameters.AddWithValue("@id", purchaseId);
        using var pmr = pmCmd.ExecuteReader();
        while (pmr.Read())
            paymentMethods.Add(new PurchasePaymentMethodInfo(
                pmr.GetInt32(0), pmr.GetString(1), pmr.GetInt32(2),
                pmr.IsDBNull(3) ? null : pmr.GetString(3),
                pmr.GetInt32(4) == 1,
                pmr.IsDBNull(5) ? null : pmr.GetString(5)));

        return detail;
    }

    public List<PurchaseListItem> GetPendingPurchasesForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PurchaseId, p.PcId,
                   TRIM(per.FirstName || ' ' || COALESCE(NULLIF(per.LastName,''), '')) AS PcName,
                   p.PurchaseDate, p.Notes, p.ApprovedStatus,
                   NULL AS ApprovedByName, p.ApprovedAt,
                   TRIM(COALESCE(cr.FirstName,'') || ' ' || COALESCE(NULLIF(cr.LastName,''),'')) AS CreatedByName,
                   p.CreatedAt,
                   COALESCE(items.TotalAmount, 0),
                   COALESCE(items.TotalHours, 0)
            FROM Purchases p
            JOIN Persons per ON per.PersonId = p.PcId
            LEFT JOIN Persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN (
                SELECT PurchaseId, SUM(AmountPaid) AS TotalAmount, SUM(HoursBought) AS TotalHours
                FROM PurchaseItems GROUP BY PurchaseId
            ) items ON items.PurchaseId = p.PurchaseId
            WHERE p.PcId = @pcId AND p.ApprovedStatus != 'Approved' AND COALESCE(p.IsDeleted, 0) = 0
            ORDER BY p.PurchaseDate DESC";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<PurchaseListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PurchaseListItem(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2).Trim(), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6).Trim(),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8).Trim(),
                r.GetString(9),
                r.GetInt32(10), r.GetInt32(11)));
        return list;
    }

    public List<PurchaseListItem> GetAllPurchasesForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PurchaseId, p.PcId,
                   TRIM(per.FirstName || ' ' || COALESCE(NULLIF(per.LastName,''), '')) AS PcName,
                   p.PurchaseDate, p.Notes, p.ApprovedStatus,
                   TRIM(COALESCE(ap.FirstName,'') || ' ' || COALESCE(NULLIF(ap.LastName,''),'')) AS ApprovedByName,
                   p.ApprovedAt,
                   TRIM(COALESCE(cr.FirstName,'') || ' ' || COALESCE(NULLIF(cr.LastName,''),'')) AS CreatedByName,
                   p.CreatedAt,
                   COALESCE(items.TotalAmount, 0),
                   COALESCE(items.TotalHours, 0)
            FROM Purchases p
            JOIN Persons per ON per.PersonId = p.PcId
            LEFT JOIN Persons ap ON ap.PersonId = p.ApprovedByPersonId
            LEFT JOIN Persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN (
                SELECT PurchaseId, SUM(AmountPaid) AS TotalAmount, SUM(HoursBought) AS TotalHours
                FROM PurchaseItems GROUP BY PurchaseId
            ) items ON items.PurchaseId = p.PurchaseId
            WHERE p.PcId = @pcId AND COALESCE(p.IsDeleted, 0) = 0
            ORDER BY p.PurchaseDate DESC, p.PurchaseId DESC";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<PurchaseListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PurchaseListItem(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2).Trim(), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6).Trim(),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8).Trim(),
                r.GetString(9),
                r.GetInt32(10), r.GetInt32(11)));
        return list;
    }

    public void UpdatePurchase(int purchaseId, string date, string? notes,
        List<(string itemType, int? courseId, int hoursBought, int amountPaid, int? registrarId, int? referralId)> items,
        List<(string methodType, int amount, string? paymentDate)> paymentMethods)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        // Get PcId for backward compat Payments
        int pcId;
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT PcId FROM Purchases WHERE PurchaseId = @id";
            q.Parameters.AddWithValue("@id", purchaseId);
            pcId = (int)(long)q.ExecuteScalar()!;
        }

        // Update header + reset status to Draft
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE Purchases SET PurchaseDate = @date, Notes = @notes,
                    ApprovedStatus = 'Draft', ApprovedByPersonId = NULL, ApprovedAt = NULL
                WHERE PurchaseId = @id";
            cmd.Parameters.AddWithValue("@id", purchaseId);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        // Delete old items + recreate
        using (var d = conn.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM PurchaseItems WHERE PurchaseId = @id"; d.Parameters.AddWithValue("@id", purchaseId); d.ExecuteNonQuery(); }

        foreach (var item in items)
        {
            using var iCmd = conn.CreateCommand();
            iCmd.Transaction = tx;
            iCmd.CommandText = @"
                INSERT INTO PurchaseItems (PurchaseId, ItemType, CourseId, HoursBought, AmountPaid, RegistrarId, ReferralId)
                VALUES (@pid, @type, @cid, @hrs, @amt, @regId, @refId)";
            iCmd.Parameters.AddWithValue("@pid", purchaseId);
            iCmd.Parameters.AddWithValue("@type", item.itemType);
            iCmd.Parameters.AddWithValue("@cid", item.courseId.HasValue ? (object)item.courseId.Value : DBNull.Value);
            iCmd.Parameters.AddWithValue("@hrs", item.hoursBought);
            iCmd.Parameters.AddWithValue("@amt", item.amountPaid);
            iCmd.Parameters.AddWithValue("@regId", item.registrarId.HasValue ? (object)item.registrarId.Value : DBNull.Value);
            iCmd.Parameters.AddWithValue("@refId", item.referralId.HasValue ? (object)item.referralId.Value : DBNull.Value);
            iCmd.ExecuteNonQuery();
        }

        // Delete old payment methods + recreate
        using (var d = conn.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM PurchasePaymentMethods WHERE PurchaseId = @id"; d.Parameters.AddWithValue("@id", purchaseId); d.ExecuteNonQuery(); }

        foreach (var pm in paymentMethods)
        {
            using var pmCmd = conn.CreateCommand();
            pmCmd.Transaction = tx;
            pmCmd.CommandText = @"
                INSERT INTO PurchasePaymentMethods (PurchaseId, MethodType, Amount, PaymentDate)
                VALUES (@pid, @type, @amt, @date)";
            pmCmd.Parameters.AddWithValue("@pid", purchaseId);
            pmCmd.Parameters.AddWithValue("@type", pm.methodType);
            pmCmd.Parameters.AddWithValue("@amt", pm.amount);
            pmCmd.Parameters.AddWithValue("@date", (object?)pm.paymentDate ?? DBNull.Value);
            pmCmd.ExecuteNonQuery();
        }

        // Update legacy Payments: delete old linked rows + recreate
        using (var d = conn.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM Payments WHERE PurchaseId = @id"; d.Parameters.AddWithValue("@id", purchaseId); d.ExecuteNonQuery(); }

        foreach (var item in items)
        {
            using var payCmd = conn.CreateCommand();
            payCmd.Transaction = tx;
            payCmd.CommandText = @"
                INSERT INTO Payments (PcId, PaymentDate, HoursBought, AmountPaid, Notes, PaymentType, CourseId, RegistrarId, ReferralId, PurchaseId)
                VALUES (@pcId, @date, @hrs, @amt, @notes, @type, @cid, @regId, @refId, @purchaseId)";
            payCmd.Parameters.AddWithValue("@pcId", pcId);
            payCmd.Parameters.AddWithValue("@date", date);
            payCmd.Parameters.AddWithValue("@hrs", item.hoursBought);
            payCmd.Parameters.AddWithValue("@amt", item.amountPaid);
            payCmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@type", item.itemType);
            payCmd.Parameters.AddWithValue("@cid", item.courseId.HasValue ? (object)item.courseId.Value : DBNull.Value);
            payCmd.Parameters.AddWithValue("@regId", item.registrarId.HasValue ? (object)item.registrarId.Value : DBNull.Value);
            payCmd.Parameters.AddWithValue("@refId", item.referralId.HasValue ? (object)item.referralId.Value : DBNull.Value);
            payCmd.Parameters.AddWithValue("@purchaseId", purchaseId);
            payCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void SetMoneyInBank(int paymentMethodId, bool isInBank)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE PurchasePaymentMethods
            SET IsMoneyInBank = @val, MoneyInBankDate = CASE WHEN @val = 1 THEN datetime('now') ELSE NULL END
            WHERE PaymentMethodId = @id";
        cmd.Parameters.AddWithValue("@id", paymentMethodId);
        cmd.Parameters.AddWithValue("@val", isInBank ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void DeletePurchase(int purchaseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Purchases SET IsDeleted = 1 WHERE PurchaseId = @id";
        cmd.Parameters.AddWithValue("@id", purchaseId);
        cmd.ExecuteNonQuery();
    }

    public void RestorePurchase(int purchaseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Purchases SET IsDeleted = 0 WHERE PurchaseId = @id";
        cmd.Parameters.AddWithValue("@id", purchaseId);
        cmd.ExecuteNonQuery();
    }

    public void ApprovePurchase(int purchaseId, int approvedByPersonId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Purchases SET ApprovedStatus = 'Approved',
                                 ApprovedByPersonId = @by,
                                 ApprovedAt = datetime('now')
            WHERE PurchaseId = @id";
        cmd.Parameters.AddWithValue("@id", purchaseId);
        cmd.Parameters.AddWithValue("@by", approvedByPersonId);
        cmd.ExecuteNonQuery();
    }

    private static object Nv(string? s) =>
        string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
}
