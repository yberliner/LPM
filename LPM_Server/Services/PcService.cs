using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record PcListItem(int PcId, string FullName, string Nick, long RemainSec);
public record PcDetailInfo(int PcId, string FirstName, string LastName, string Nick,
    string Phone, string Email, string Notes, string DateOfBirth, string Gender,
    string Org, string Source, int OrgId = 0)
{
    public string FullName => string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";
}
public record PcSessionInfo(int SessionId, string Date, string AuditorName,
    int LengthSec, int AdminSec, bool IsFree, string VerifiedStatus);
public record PcPayment(int PaymentId, string Date, int HoursBought, int AmountPaid,
    string PaymentType, int? CourseId, string? CourseName, int VisitCount,
    int? RegistrarId, string? RegistrarName, int? ReferralId, string? ReferralName);
public record PcStats(int TotalSessions, int FreeSessions, long UsedSec,
    int TotalHoursPurchased, int TotalAmountPaid, string? LastSessionDate);
public record PcListItemEx(int PcId, string FullName, string Nick, long RemainSec,
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
            CREATE TABLE IF NOT EXISTS fin_payments (
                PaymentId   INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId        INTEGER NOT NULL,
                PaymentDate TEXT    NOT NULL,
                HoursBought INTEGER NOT NULL DEFAULT 0,
                AmountPaid  INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT NOT NULL DEFAULT (datetime('now')),
                PaymentType TEXT DEFAULT 'Auditing',
                CourseId    INTEGER,
                RegistrarId INTEGER,
                ReferralId  INTEGER,
                PurchaseId  INTEGER
            )";
        c1.ExecuteNonQuery();

        // Purchases & PurchaseItems tables
        using var c2 = conn.CreateCommand();
        c2.CommandText = @"
            CREATE TABLE IF NOT EXISTS fin_purchases (
                PurchaseId         INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId               INTEGER NOT NULL,
                PurchaseDate       TEXT NOT NULL,
                Notes              TEXT,
                SignatureData      TEXT,
                ApprovedStatus     TEXT NOT NULL DEFAULT 'Pending',
                ApprovedByPersonId INTEGER,
                ApprovedAt         TEXT,
                CreatedByPersonId  INTEGER,
                CreatedAt          TEXT NOT NULL DEFAULT (datetime('now')),
                IsDeleted          INTEGER NOT NULL DEFAULT 0
            )";
        c2.ExecuteNonQuery();

        using var c3 = conn.CreateCommand();
        c3.CommandText = @"
            CREATE TABLE IF NOT EXISTS fin_purchase_items (
                PurchaseItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                PurchaseId     INTEGER NOT NULL,
                ItemType       TEXT NOT NULL DEFAULT 'Auditing',
                CourseId       INTEGER,
                HoursBought    INTEGER NOT NULL DEFAULT 0,
                AmountPaid     INTEGER NOT NULL DEFAULT 0,
                RegistrarId    INTEGER,
                ReferralId     INTEGER,
                FOREIGN KEY (PurchaseId) REFERENCES fin_purchases(PurchaseId)
            )";
        c3.ExecuteNonQuery();

        // PurchasePaymentMethods table
        using var c4 = conn.CreateCommand();
        c4.CommandText = @"
            CREATE TABLE IF NOT EXISTS fin_payment_methods (
                PaymentMethodId INTEGER PRIMARY KEY AUTOINCREMENT,
                PurchaseId      INTEGER NOT NULL,
                MethodType      TEXT NOT NULL DEFAULT 'Cash',
                Amount          INTEGER NOT NULL DEFAULT 0,
                PaymentDate     TEXT,
                IsMoneyInBank   INTEGER NOT NULL DEFAULT 0,
                MoneyInBankDate TEXT,
                FOREIGN KEY (PurchaseId) REFERENCES fin_purchases(PurchaseId)
            )";
        c4.ExecuteNonQuery();
    }

    public List<PcListItem> GetAllPcs()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   COALESCE(p.Nick, '') AS Nick,
                   (COALESCE(pay.TotalHours, 0) * 3600 - COALESCE(sess.UsedSec, 0)) AS RemainSec
            FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            LEFT JOIN (
                SELECT PcId, SUM(HoursBought) AS TotalHours
                FROM fin_payments GROUP BY PcId
            ) pay ON pay.PcId = pc.PcId
            LEFT JOIN (
                SELECT PcId, SUM(LengthSeconds) AS UsedSec
                FROM sess_sessions WHERE IsFreeSession = 0 GROUP BY PcId
            ) sess ON sess.PcId = pc.PcId
            WHERE COALESCE(p.IsActive, 1) = 1
            ORDER BY RemainSec ASC, p.FirstName, p.LastName";
        var list = new List<PcListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcListItem(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt64(3)));
        return list;
    }

    /// <summary>
    /// Checks for duplicate PC by FirstName+LastName+Nick.
    /// Returns null if no conflict, or a message string if blocked.
    /// If the duplicate is inactive, auto-renames its nick and allows adding.
    /// </summary>
    public string? CheckDuplicatePc(string firstName, string lastName, string nick)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PersonId, COALESCE(p.IsActive, 1)
            FROM core_persons p
            JOIN core_pcs pc ON pc.PcId = p.PersonId
            WHERE LOWER(TRIM(p.FirstName)) = LOWER(@fn)
              AND LOWER(COALESCE(TRIM(p.LastName),'')) = LOWER(@ln)
              AND LOWER(COALESCE(TRIM(p.Nick),'')) = LOWER(@nick)";
        cmd.Parameters.AddWithValue("@fn", firstName.Trim());
        cmd.Parameters.AddWithValue("@ln", lastName.Trim());
        cmd.Parameters.AddWithValue("@nick", nick?.Trim() ?? "");
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null; // no conflict

        var existingId = r.GetInt32(0);
        var isActive = r.GetInt32(1) == 1;

        if (!isActive)
        {
            // Auto-rename the inactive one's nick
            r.Close();
            using var upd = conn.CreateCommand();
            upd.CommandText = "UPDATE core_persons SET Nick = @newNick WHERE PersonId = @id";
            upd.Parameters.AddWithValue("@newNick", $"_{existingId}");
            upd.Parameters.AddWithValue("@id", existingId);
            upd.ExecuteNonQuery();
            return null; // conflict resolved
        }

        return "ACTIVE_DUPLICATE";
    }

    public int AddPcWithPerson(string firstName, string lastName,
        string phone, string email, string dateOfBirth, string gender,
        int? orgId = null, int? sourceId = null, string notes = "", string nick = "")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            INSERT INTO core_persons (FirstName, LastName, Phone, Email, DateOfBirth, Gender, Org, Source, Notes, Nick)
            VALUES (@fn, @ln, @ph, @em, @dob, @gender, @org, @srcId, @notes, @nick)";
        pCmd.Parameters.AddWithValue("@fn",  firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln",  lastName.Trim());
        pCmd.Parameters.AddWithValue("@ph",  Nv(phone));
        pCmd.Parameters.AddWithValue("@em",  Nv(email));
        pCmd.Parameters.AddWithValue("@dob", Nv(dateOfBirth));
        pCmd.Parameters.AddWithValue("@gender", Nv(gender));
        pCmd.Parameters.AddWithValue("@org", orgId.HasValue ? (object)orgId.Value : DBNull.Value);
        pCmd.Parameters.AddWithValue("@srcId", sourceId.HasValue ? (object)sourceId.Value : DBNull.Value);
        pCmd.Parameters.AddWithValue("@notes", Nv(notes));
        pCmd.Parameters.AddWithValue("@nick", Nv(nick));
        pCmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var personId = (int)(long)idCmd.ExecuteScalar()!;

        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = "INSERT INTO core_pcs (PcId) VALUES (@id)";
        pcCmd.Parameters.AddWithValue("@id", personId);
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
                   COALESCE(p.Nick,''), COALESCE(p.Phone,''),
                   COALESCE(p.Email,''),        COALESCE(p.Notes,''),
                   COALESCE(p.DateOfBirth,''), COALESCE(p.Gender,''),
                   COALESCE(og.Name,''),        COALESCE(rs.Name,''),
                   COALESCE(p.Org, 0)
            FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            LEFT JOIN lkp_referral_sources rs ON rs.ReferralId = p.Source
            LEFT JOIN lkp_organizations og ON og.OrgId = p.Org
            WHERE pc.PcId = @id";
        cmd.Parameters.AddWithValue("@id", pcId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PcDetailInfo(pcId,
            r.GetString(0), r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetString(4), r.GetString(5),
            r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9),
            r.GetInt32(10));
    }

    public void UpdatePcDetail(int pcId, string firstName, string lastName,
        string nick, string phone, string email, string notes,
        string dateOfBirth, string gender, int? orgId = null, int? sourceId = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            UPDATE core_persons SET FirstName=@fn, LastName=@ln,
                               Phone=@ph, Email=@em, DateOfBirth=@dob, Gender=@gender,
                               Nick=@nick, Notes=@nt, Org=@org, Source=@srcId
            WHERE PersonId=@id";
        pCmd.Parameters.AddWithValue("@fn",   firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln",   lastName.Trim());
        pCmd.Parameters.AddWithValue("@ph",   Nv(phone));
        pCmd.Parameters.AddWithValue("@em",   Nv(email));
        pCmd.Parameters.AddWithValue("@dob",  Nv(dateOfBirth));
        pCmd.Parameters.AddWithValue("@gender", Nv(gender));
        pCmd.Parameters.AddWithValue("@nick", Nv(nick));
        pCmd.Parameters.AddWithValue("@nt",   Nv(notes));
        pCmd.Parameters.AddWithValue("@org",  orgId.HasValue ? (object)orgId.Value : DBNull.Value);
        pCmd.Parameters.AddWithValue("@srcId", sourceId.HasValue ? (object)sourceId.Value : DBNull.Value);
        pCmd.Parameters.AddWithValue("@id",   pcId);
        pCmd.ExecuteNonQuery();
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
            FROM sess_sessions WHERE PcId=@id";
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
            FROM fin_payments WHERE PcId=@id";
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
            FROM sess_sessions s
            JOIN core_persons p ON p.PersonId = s.AuditorId
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
            SELECT p.PaymentId, p.PaymentDate, p.HoursBought, p.AmountPaid,
                   COALESCE(p.PaymentType,'Auditing'), p.CourseId, c.Name,
                   CASE WHEN COALESCE(p.PaymentType,'Auditing') = 'Course' AND p.CourseId IS NOT NULL
                        THEN (SELECT COUNT(*) FROM acad_attendance s
                              WHERE s.PersonId = p.PcId AND s.VisitDate >= p.PaymentDate)
                        ELSE 0 END AS VisitCount,
                   p.RegistrarId,
                   TRIM(COALESCE(reg.FirstName,'') || ' ' || COALESCE(NULLIF(reg.LastName,''),'')) AS RegistrarName,
                   p.ReferralId,
                   TRIM(COALESCE(rf.FirstName,'') || ' ' || COALESCE(NULLIF(rf.LastName,''),'')) AS ReferralName
            FROM fin_payments p
            LEFT JOIN lkp_courses c   ON c.CourseId    = p.CourseId
            LEFT JOIN core_persons reg ON reg.PersonId  = p.RegistrarId
            LEFT JOIN core_persons rf  ON rf.PersonId   = p.ReferralId
            WHERE p.PcId=@id
            ORDER BY p.PaymentDate DESC";
        cmd.Parameters.AddWithValue("@id", pcId);
        var list = new List<PcPayment>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcPayment(
                r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3),
                r.GetString(4),
                r.IsDBNull(5) ? null : r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetInt32(7),
                r.IsDBNull(8)  ? null : r.GetInt32(8),
                r.IsDBNull(9)  ? null : r.GetString(9).Trim(),
                r.IsDBNull(10) ? null : r.GetInt32(10),
                r.IsDBNull(11) ? null : r.GetString(11).Trim()));
        return list;
    }

    public void AddPayment(int pcId, string date, int hoursBought, int amountPaid,
        string paymentType = "Auditing", int? courseId = null, int? registrarId = null, int? referralId = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO fin_payments (PcId, PaymentDate, HoursBought, AmountPaid, PaymentType, CourseId, RegistrarId, ReferralId)
            VALUES (@pcId, @date, @hrs, @amt, @type, @cid, @regId, @refId)";
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@date",  date);
        cmd.Parameters.AddWithValue("@hrs",   hoursBought);
        cmd.Parameters.AddWithValue("@amt",   amountPaid);
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
                INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted)
                SELECT @pid, @cid, @date
                WHERE NOT EXISTS (
                    SELECT 1 FROM acad_student_courses
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
        cmd.CommandText = "DELETE FROM fin_payments WHERE PaymentId=@id";
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
            FROM core_persons p
            WHERE p.PersonId IN (
                SELECT AuditorId FROM sess_auditors WHERE COALESCE(Type,1) != 0
                UNION
                SELECT CsId FROM cs_case_supervisors WHERE COALESCE(IsActive,1) = 1
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
            FROM core_persons WHERE COALESCE(IsActive,1) = 1 ORDER BY FirstName, LastName";
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
        cmd.CommandText = "SELECT PersonId FROM core_persons WHERE LOWER(FirstName) = LOWER(@u) LIMIT 1";
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
                   COALESCE(p.Nick, '') AS Nick,
                   (COALESCE(pay.TotalHours, 0) * 3600 - COALESCE(sess.UsedSec, 0)) AS RemainSec,
                   COALESCE(sess.UsedSec, 0) AS TotalSessionSec,
                   COALESCE(sess.SessionCount, 0) AS TotalSessions,
                   COALESCE(acad.VisitCount, 0) AS AcademyVisits,
                   COALESCE(pay.TotalHours, 0) AS HoursPurchased
            FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            LEFT JOIN (
                SELECT PcId, SUM(HoursBought) AS TotalHours
                FROM fin_payments GROUP BY PcId
            ) pay ON pay.PcId = pc.PcId
            LEFT JOIN (
                SELECT PcId, SUM(LengthSeconds) AS UsedSec, COUNT(*) AS SessionCount
                FROM sess_sessions WHERE IsFreeSession = 0 GROUP BY PcId
            ) sess ON sess.PcId = pc.PcId
            LEFT JOIN (
                SELECT PersonId, COUNT(*) AS VisitCount
                FROM acad_attendance GROUP BY PersonId
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
            INSERT INTO fin_purchases (PcId, PurchaseDate, Notes, SignatureData, CreatedByPersonId)
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
                INSERT INTO fin_purchase_items (PurchaseId, ItemType, CourseId, HoursBought, AmountPaid, RegistrarId, ReferralId)
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
                    INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted)
                    SELECT @personId, @courseId, @date
                    WHERE NOT EXISTS (
                        SELECT 1 FROM acad_student_courses
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
                    INSERT INTO fin_payment_methods (PurchaseId, MethodType, Amount, PaymentDate)
                    VALUES (@pid, @type, @amt, @date)";
                pmCmd.Parameters.AddWithValue("@pid", purchaseId);
                pmCmd.Parameters.AddWithValue("@type", pm.methodType);
                pmCmd.Parameters.AddWithValue("@amt", pm.amount);
                pmCmd.Parameters.AddWithValue("@date", (object?)pm.paymentDate ?? DBNull.Value);
                pmCmd.ExecuteNonQuery();
            }
        }

        // Also insert into fin_payments table for hours tracking
        foreach (var item in items)
        {
            using var payCmd = conn.CreateCommand();
            payCmd.Transaction = tx;
            payCmd.CommandText = @"
                INSERT INTO fin_payments (PcId, PaymentDate, HoursBought, AmountPaid, PaymentType, CourseId, RegistrarId, ReferralId, PurchaseId)
                VALUES (@pcId, @date, @hrs, @amt, @type, @cid, @regId, @refId, @purchaseId)";
            payCmd.Parameters.AddWithValue("@pcId", pcId);
            payCmd.Parameters.AddWithValue("@date", date);
            payCmd.Parameters.AddWithValue("@hrs", item.hoursBought);
            payCmd.Parameters.AddWithValue("@amt", item.amountPaid);
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
            FROM fin_purchases p
            JOIN core_persons per ON per.PersonId = p.PcId
            LEFT JOIN core_persons ap ON ap.PersonId = p.ApprovedByPersonId
            LEFT JOIN core_persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN (
                SELECT PurchaseId, SUM(AmountPaid) AS TotalAmount, SUM(HoursBought) AS TotalHours
                FROM fin_purchase_items GROUP BY PurchaseId
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
            FROM fin_purchases p
            JOIN core_persons per ON per.PersonId = p.PcId
            LEFT JOIN core_persons ap ON ap.PersonId = p.ApprovedByPersonId
            LEFT JOIN core_persons cr ON cr.PersonId = p.CreatedByPersonId
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
            FROM fin_purchase_items pi
            LEFT JOIN lkp_courses c ON c.CourseId = pi.CourseId
            LEFT JOIN core_persons reg ON reg.PersonId = pi.RegistrarId
            LEFT JOIN core_persons rf ON rf.PersonId = pi.ReferralId
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
            FROM fin_payment_methods WHERE PurchaseId = @id ORDER BY PaymentMethodId";
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
            FROM fin_purchases p
            JOIN core_persons per ON per.PersonId = p.PcId
            LEFT JOIN core_persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN (
                SELECT PurchaseId, SUM(AmountPaid) AS TotalAmount, SUM(HoursBought) AS TotalHours
                FROM fin_purchase_items GROUP BY PurchaseId
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
            FROM fin_purchases p
            JOIN core_persons per ON per.PersonId = p.PcId
            LEFT JOIN core_persons ap ON ap.PersonId = p.ApprovedByPersonId
            LEFT JOIN core_persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN (
                SELECT PurchaseId, SUM(AmountPaid) AS TotalAmount, SUM(HoursBought) AS TotalHours
                FROM fin_purchase_items GROUP BY PurchaseId
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

        // Get PcId for hours tracking
        int pcId;
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT PcId FROM fin_purchases WHERE PurchaseId = @id";
            q.Parameters.AddWithValue("@id", purchaseId);
            pcId = (int)(long)q.ExecuteScalar()!;
        }

        // Update header + reset status to Draft
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE fin_purchases SET PurchaseDate = @date, Notes = @notes,
                    ApprovedStatus = 'Draft', ApprovedByPersonId = NULL, ApprovedAt = NULL
                WHERE PurchaseId = @id";
            cmd.Parameters.AddWithValue("@id", purchaseId);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        // Delete old items + recreate
        using (var d = conn.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM fin_purchase_items WHERE PurchaseId = @id"; d.Parameters.AddWithValue("@id", purchaseId); d.ExecuteNonQuery(); }

        foreach (var item in items)
        {
            using var iCmd = conn.CreateCommand();
            iCmd.Transaction = tx;
            iCmd.CommandText = @"
                INSERT INTO fin_purchase_items (PurchaseId, ItemType, CourseId, HoursBought, AmountPaid, RegistrarId, ReferralId)
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
        using (var d = conn.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM fin_payment_methods WHERE PurchaseId = @id"; d.Parameters.AddWithValue("@id", purchaseId); d.ExecuteNonQuery(); }

        foreach (var pm in paymentMethods)
        {
            using var pmCmd = conn.CreateCommand();
            pmCmd.Transaction = tx;
            pmCmd.CommandText = @"
                INSERT INTO fin_payment_methods (PurchaseId, MethodType, Amount, PaymentDate)
                VALUES (@pid, @type, @amt, @date)";
            pmCmd.Parameters.AddWithValue("@pid", purchaseId);
            pmCmd.Parameters.AddWithValue("@type", pm.methodType);
            pmCmd.Parameters.AddWithValue("@amt", pm.amount);
            pmCmd.Parameters.AddWithValue("@date", (object?)pm.paymentDate ?? DBNull.Value);
            pmCmd.ExecuteNonQuery();
        }

        // Update fin_payments: delete old linked rows + recreate
        using (var d = conn.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM fin_payments WHERE PurchaseId = @id"; d.Parameters.AddWithValue("@id", purchaseId); d.ExecuteNonQuery(); }

        foreach (var item in items)
        {
            using var payCmd = conn.CreateCommand();
            payCmd.Transaction = tx;
            payCmd.CommandText = @"
                INSERT INTO fin_payments (PcId, PaymentDate, HoursBought, AmountPaid, PaymentType, CourseId, RegistrarId, ReferralId, PurchaseId)
                VALUES (@pcId, @date, @hrs, @amt, @type, @cid, @regId, @refId, @purchaseId)";
            payCmd.Parameters.AddWithValue("@pcId", pcId);
            payCmd.Parameters.AddWithValue("@date", date);
            payCmd.Parameters.AddWithValue("@hrs", item.hoursBought);
            payCmd.Parameters.AddWithValue("@amt", item.amountPaid);
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
            UPDATE fin_payment_methods
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
        cmd.CommandText = "UPDATE fin_purchases SET IsDeleted = 1 WHERE PurchaseId = @id";
        cmd.Parameters.AddWithValue("@id", purchaseId);
        cmd.ExecuteNonQuery();
    }

    public void RestorePurchase(int purchaseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE fin_purchases SET IsDeleted = 0 WHERE PurchaseId = @id";
        cmd.Parameters.AddWithValue("@id", purchaseId);
        cmd.ExecuteNonQuery();
    }

    public void ApprovePurchase(int purchaseId, int approvedByPersonId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE fin_purchases SET ApprovedStatus = 'Approved',
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
