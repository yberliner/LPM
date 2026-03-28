using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace LPM.Tests.Helpers;

/// <summary>
/// Creates a temporary SQLite database file with the full lifepower.db schema.
/// Each test class calls CreateTempDb() to get an isolated database path,
/// then calls Cleanup() in Dispose() to remove the file.
/// </summary>
public static class TestDbHelper
{
    // -------------------------------------------------------------------------
    // DB lifecycle
    // -------------------------------------------------------------------------

    public static string CreateTempDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lpm_test_{Guid.NewGuid():N}.db");
        InitSchema(path);
        return path;
    }

    /// <summary>
    /// Clears the SQLite connection pool for the DB file (required on Windows to
    /// release file handles) then deletes the file with a short retry loop.
    /// </summary>
    public static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int i = 0; i < 8; i++)
        {
            try
            {
                foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                    if (File.Exists(f)) File.Delete(f);
                return;
            }
            catch (IOException) { Thread.Sleep(50); }
        }
        // If still locked, leave it – OS will clean up %TEMP% eventually
    }

    // -------------------------------------------------------------------------
    // Full schema — mirrors the actual lifepower.db schema (prefixed tables)
    // -------------------------------------------------------------------------

    private static void InitSchema(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS core_persons (
                PersonId    INTEGER PRIMARY KEY AUTOINCREMENT,
                FirstName   TEXT NOT NULL,
                LastName    TEXT,
                Nick        TEXT,
                Phone       TEXT,
                Email       TEXT,
                DateOfBirth TEXT,
                Gender      TEXT,
                IsActive    INTEGER NOT NULL DEFAULT 1,
                Notes       TEXT,
                Org         INTEGER,
                Source      INTEGER,
                CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS core_pcs (
                PcId        INTEGER NOT NULL PRIMARY KEY
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS lkp_grades (
                GradeId   INTEGER PRIMARY KEY AUTOINCREMENT,
                Code      TEXT NOT NULL UNIQUE,
                SortOrder INTEGER NOT NULL DEFAULT 0
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS lkp_courses (
                CourseId INTEGER PRIMARY KEY AUTOINCREMENT,
                Name     TEXT    NOT NULL
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS lkp_referral_sources (
                ReferralId INTEGER PRIMARY KEY AUTOINCREMENT,
                Name       TEXT NOT NULL
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS lkp_organizations (
                OrgId INTEGER PRIMARY KEY AUTOINCREMENT,
                Name  TEXT NOT NULL
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS lkp_folder_items (
                ItemId  INTEGER PRIMARY KEY AUTOINCREMENT,
                Name    TEXT NOT NULL,
                Section TEXT NOT NULL DEFAULT 'WorkSheets'
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS lkp_cs_status (
                Code      TEXT NOT NULL PRIMARY KEY,
                Label     TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            )");

        // core_users: replaces old Users/Roles/UserRoles/Auditors/CaseSupervisors
        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS core_users (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId        INTEGER NOT NULL,
                Username        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                PasswordHash    TEXT    NOT NULL,
                StaffRole       TEXT    NOT NULL DEFAULT 'None',
                UserType        TEXT    NOT NULL DEFAULT 'Staff',
                IsActive        INTEGER NOT NULL DEFAULT 1,
                GradeId         INTEGER,
                AllowAll        INTEGER NOT NULL DEFAULT 0,
                MustChangePassword INTEGER NOT NULL DEFAULT 0,
                TotpEnabled     INTEGER NOT NULL DEFAULT 0,
                TotpSecret      TEXT,
                AvatarPath      TEXT,
                ContactConfirmed INTEGER NOT NULL DEFAULT 1,
                Require2FA      INTEGER NOT NULL DEFAULT 0
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS sess_sessions (
                SessionId               INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId                    INTEGER NOT NULL,
                AuditorId               INTEGER,
                SessionDate             TEXT    NOT NULL,
                SequenceInDay           INTEGER NOT NULL DEFAULT 1,
                LengthSeconds           INTEGER NOT NULL DEFAULT 0,
                AdminSeconds            INTEGER NOT NULL DEFAULT 0,
                IsFreeSession           INTEGER NOT NULL DEFAULT 0,
                ChargeSeconds           INTEGER NOT NULL DEFAULT 0,
                ChargedRateCentsPerHour INTEGER NOT NULL DEFAULT 0,
                AuditorSalaryCentsPerHour INTEGER NOT NULL DEFAULT 0,
                Name                    TEXT,
                CreatedByUserId         INTEGER,
                CreatedAt               TEXT    NOT NULL DEFAULT (datetime('now')),
                VerifiedStatus          TEXT    NOT NULL DEFAULT 'Pending',
                VerifiedByUserId        INTEGER,
                VerifiedAt              TEXT,
                IsImported              INTEGER NOT NULL DEFAULT 0
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS cs_reviews (
                CsReviewId          INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId           INTEGER NOT NULL UNIQUE,
                CsId                INTEGER NOT NULL,
                ReviewLengthSeconds INTEGER NOT NULL DEFAULT 0,
                ReviewedAt          TEXT,
                Status              TEXT    NOT NULL DEFAULT 'Draft'
                                    CHECK(Status IN ('Draft','Approved','NeedsCorrection','Rejected','Done')),
                Notes               TEXT,
                CsSalaryCentsPerHour INTEGER NOT NULL DEFAULT 0
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS cs_work_log (
                CsWorkLogId   INTEGER PRIMARY KEY AUTOINCREMENT,
                CsId          INTEGER NOT NULL,
                PcId          INTEGER NOT NULL,
                WorkDate      TEXT    NOT NULL,
                LengthSeconds INTEGER NOT NULL DEFAULT 0,
                Notes         TEXT,
                CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS acad_attendance (
                StudentId    INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId     INTEGER NOT NULL,
                VisitDate    TEXT    NOT NULL,
                VisitsPerDay INTEGER NOT NULL DEFAULT 1,
                UNIQUE(PersonId, VisitDate)
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS acad_student_courses (
                StudentCourseId INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId        INTEGER NOT NULL,
                CourseId        INTEGER NOT NULL,
                DateStarted     TEXT    NOT NULL,
                DateFinished    TEXT    NULL
            )");

        Exec(conn, @"
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
                IsDeleted          INTEGER NOT NULL DEFAULT 0,
                RegistrarId        INTEGER,
                ReferralId         INTEGER
            )");

        Exec(conn, @"
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
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS fin_payment_methods (
                PaymentMethodId INTEGER PRIMARY KEY AUTOINCREMENT,
                PurchaseId      INTEGER NOT NULL,
                MethodType      TEXT NOT NULL DEFAULT 'Cash',
                Amount          INTEGER NOT NULL DEFAULT 0,
                PaymentDate     TEXT,
                IsMoneyInBank   INTEGER NOT NULL DEFAULT 0,
                MoneyInBankDate TEXT,
                FOREIGN KEY (PurchaseId) REFERENCES fin_purchases(PurchaseId)
            )");

        // sys_staff_pc_list: UNIQUE on (UserId, PcId) only — no WorkCapacity in key
        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS sys_staff_pc_list (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId       INTEGER NOT NULL,
                PcId         INTEGER NOT NULL,
                WorkCapacity TEXT    NOT NULL DEFAULT 'Auditor',
                IsApproved   INTEGER NOT NULL DEFAULT 0,
                RequestedAt  TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE(UserId, PcId)
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS sys_staff_messages (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                FromStaffId     INTEGER NOT NULL,
                ToStaffId       INTEGER NOT NULL,
                MsgText         TEXT    NOT NULL,
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                AcknowledgedAt  TEXT    NULL
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS sys_weekly_remarks (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                AuditorId   INTEGER NOT NULL,
                WeekDate    TEXT    NOT NULL,
                Remarks     TEXT    NOT NULL DEFAULT '',
                SubmittedAt TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE(AuditorId, WeekDate)
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS sess_misc_charges (
                MiscChargeId  INTEGER PRIMARY KEY AUTOINCREMENT,
                AuditorId     INTEGER NOT NULL,
                PcId          INTEGER NOT NULL,
                ChargeDate    TEXT    NOT NULL,
                SequenceInDay INTEGER NOT NULL DEFAULT 1,
                LengthSeconds INTEGER NOT NULL DEFAULT 0,
                AdminSeconds  INTEGER NOT NULL DEFAULT 0,
                IsFree        INTEGER NOT NULL DEFAULT 0,
                Summary       TEXT,
                CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS sess_folder_summary (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId   INTEGER,
                PcId        INTEGER NOT NULL,
                AuditorId   INTEGER,
                SummaryHtml TEXT,
                ArfJson     TEXT,
                CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS sess_completions (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId          INTEGER NOT NULL,
                CompleteDate  TEXT,
                CreateDate    TEXT    NOT NULL DEFAULT (datetime('now')),
                FinishedGrade TEXT,
                AuditorId     INTEGER
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS sess_meetings (
                MeetingId     INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId          INTEGER NOT NULL,
                AuditorId     INTEGER,
                MeetingType   TEXT    NOT NULL,
                StartAt       TEXT    NOT NULL,
                LengthSeconds INTEGER NOT NULL DEFAULT 0,
                IsWeekly      INTEGER NOT NULL DEFAULT 0,
                CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now')),
                CreatedBy     INTEGER NOT NULL
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS sys_activity_log (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                Username   TEXT    NOT NULL,
                ActivityAt TEXT    NOT NULL,
                Action     TEXT    NOT NULL,
                Kind       TEXT    NOT NULL
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS lkp_shortcuts (
                KeyChar TEXT NOT NULL PRIMARY KEY,
                Text    TEXT
            )");

        // Seed standard grades
        Exec(conn, "INSERT INTO lkp_grades (Code, SortOrder) VALUES ('BA',1),('MA',2),('PHD',3)");
    }

    // -------------------------------------------------------------------------
    // Generic SQL helpers
    // -------------------------------------------------------------------------

    public static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public static bool TableExists(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n";
        cmd.Parameters.AddWithValue("@n", tableName);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    public static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=@c";
        cmd.Parameters.AddWithValue("@c", column);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    // -------------------------------------------------------------------------
    // Entity insert helpers
    // -------------------------------------------------------------------------

    /// <summary>Inserts a Person into core_persons and returns the new PersonId.</summary>
    public static int InsertPerson(SqliteConnection conn,
        string firstName, string lastName = "",
        string phone = "", string email = "",
        string dateOfBirth = "", string gender = "")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO core_persons (FirstName, LastName, Phone, Email, DateOfBirth, Gender)
            VALUES (@fn, @ln, @ph, @em, @dob, @gender)";
        cmd.Parameters.AddWithValue("@fn",  firstName);
        cmd.Parameters.AddWithValue("@ln",  Nv(lastName));
        cmd.Parameters.AddWithValue("@ph",  Nv(phone));
        cmd.Parameters.AddWithValue("@em",  Nv(email));
        cmd.Parameters.AddWithValue("@dob", Nv(dateOfBirth));
        cmd.Parameters.AddWithValue("@gender", Nv(gender));
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    /// <summary>
    /// Inserts a staff user into core_users. The PersonId must already exist in core_persons.
    /// staffRole: 'Auditor', 'CS', 'Solo', or 'None'. userType: 'Admin' or 'Staff'.
    /// </summary>
    public static int InsertCoreUser(SqliteConnection conn,
        int personId, string username, string password,
        string staffRole = "None", string userType = "Staff",
        bool isActive = true, int? gradeId = null, bool allowAll = false)
    {
        var hash = HashPassword(password);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO core_users (PersonId, Username, PasswordHash, StaffRole, UserType, IsActive, GradeId, AllowAll)
            VALUES (@pid, @u, @h, @role, @utype, @active, @gid, @aa)";
        cmd.Parameters.AddWithValue("@pid",    personId);
        cmd.Parameters.AddWithValue("@u",      username);
        cmd.Parameters.AddWithValue("@h",      hash);
        cmd.Parameters.AddWithValue("@role",   staffRole);
        cmd.Parameters.AddWithValue("@utype",  userType);
        cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@gid",    gradeId.HasValue ? (object)gradeId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@aa",     allowAll ? 1 : 0);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    /// <summary>
    /// Inserts a Person + core_users entry for an Auditor (StaffRole='Auditor').
    /// The personId must already exist in core_persons.
    /// </summary>
    public static void InsertAuditor(SqliteConnection conn,
        int personId, string staffRole = "Auditor", bool isActive = true, int? gradeId = null)
    {
        // Use a username derived from the personId to avoid collisions
        var username = $"auditor_{personId}";
        InsertCoreUser(conn, personId, username, "pass1234",
            staffRole: staffRole, isActive: isActive, gradeId: gradeId);
    }


    /// <summary>
    /// Inserts a Person + core_users entry for a CS (StaffRole='CS').
    /// The personId must already exist in core_persons.
    /// </summary>
    public static void InsertCS(SqliteConnection conn, int personId, bool isActive = true)
    {
        var username = $"cs_{personId}";
        InsertCoreUser(conn, personId, username, "pass1234",
            staffRole: "CS", isActive: isActive);
    }

    /// <summary>Inserts a PC row into core_pcs (assumes PersonId already exists in core_persons).</summary>
    public static void InsertPC(SqliteConnection conn, int personId, string? externalId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO core_pcs (PcId) VALUES (@id)";
        cmd.Parameters.AddWithValue("@id", personId);
        cmd.ExecuteNonQuery();
        // externalId param kept for API compatibility but ignored (no ExternalId column)
    }

    /// <summary>Inserts a Session into sess_sessions and returns the new SessionId.</summary>
    public static int InsertSession(SqliteConnection conn,
        int pcId, int? auditorId, string date,
        int lengthSec, int adminSec = 0,
        bool isFree = false,
        int seqInDay = 1, string verifiedStatus = "Pending")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_sessions
              (PcId, AuditorId, SessionDate, SequenceInDay,
               LengthSeconds, AdminSeconds, IsFreeSession,
               ChargeSeconds, ChargedRateCentsPerHour,
               CreatedAt, VerifiedStatus)
            VALUES
              (@pcId, @audId, @date, @seq,
               @len, @adm, @free,
               0, 0,
               datetime('now'), @vs)";
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@audId", auditorId.HasValue ? (object)auditorId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@date",  date);
        cmd.Parameters.AddWithValue("@seq",   seqInDay);
        cmd.Parameters.AddWithValue("@len",   lengthSec);
        cmd.Parameters.AddWithValue("@adm",   adminSec);
        cmd.Parameters.AddWithValue("@free",  isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@vs",    verifiedStatus);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    /// <summary>Inserts an academy visit (acad_attendance row).</summary>
    public static void InsertVisit(SqliteConnection conn, int personId, string date)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO acad_attendance (PersonId, VisitDate) VALUES (@pid, @date)";
        cmd.Parameters.AddWithValue("@pid",  personId);
        cmd.Parameters.AddWithValue("@date", date);
        cmd.ExecuteNonQuery();
    }

    // -------------------------------------------------------------------------
    // Auth helpers (new schema)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Inserts a user into core_users with the given username/password.
    /// Creates a stub core_persons entry automatically.
    /// Returns the new core_users.Id (NOT PersonId).
    /// For role assignment, call AssignRole(conn, userId, "Admin") afterwards.
    /// </summary>
    public static int InsertUser(SqliteConnection conn,
        string username, string password, bool isActive = true)
    {
        // Create a stub person entry
        var personId = InsertPerson(conn, username);
        return InsertCoreUser(conn, personId, username, password,
            staffRole: "None", userType: "Staff", isActive: isActive);
    }

    /// <summary>
    /// "Assigns" a role by setting UserType on the core_users row.
    /// Only "Admin" is meaningful; all others become "Staff".
    /// userId here is the core_users.Id returned by InsertUser.
    /// </summary>
    public static void AssignRole(SqliteConnection conn, int userId, string roleCode)
    {
        var userType = roleCode == "Admin" ? "Admin" : "Staff";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET UserType = @t WHERE Id = @id";
        cmd.Parameters.AddWithValue("@t",  userType);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }

    // -------------------------------------------------------------------------
    // Purchase helpers
    // -------------------------------------------------------------------------

    public static int InsertPurchase(SqliteConnection conn,
        int pcId, string date, string notes = "", string status = "Pending",
        int? createdBy = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO fin_purchases (PcId, PurchaseDate, Notes, ApprovedStatus, CreatedByPersonId)
            VALUES (@pcId, @date, @notes, @status, @createdBy)";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@date", date);
        cmd.Parameters.AddWithValue("@notes", Nv(notes));
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@createdBy", createdBy.HasValue ? (object)createdBy.Value : DBNull.Value);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    public static int InsertPurchaseItem(SqliteConnection conn,
        int purchaseId, string itemType = "Auditing", int? courseId = null,
        int hoursBought = 0, int amountPaid = 0)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO fin_purchase_items (PurchaseId, ItemType, CourseId, HoursBought, AmountPaid)
            VALUES (@pid, @type, @cid, @hrs, @amt)";
        cmd.Parameters.AddWithValue("@pid", purchaseId);
        cmd.Parameters.AddWithValue("@type", itemType);
        cmd.Parameters.AddWithValue("@cid", courseId.HasValue ? (object)courseId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@hrs", hoursBought);
        cmd.Parameters.AddWithValue("@amt", amountPaid);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    public static int InsertPaymentMethod(SqliteConnection conn,
        int purchaseId, string methodType = "Cash", int amount = 0, string? paymentDate = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO fin_payment_methods (PurchaseId, MethodType, Amount, PaymentDate)
            VALUES (@pid, @type, @amt, @date)";
        cmd.Parameters.AddWithValue("@pid", purchaseId);
        cmd.Parameters.AddWithValue("@type", methodType);
        cmd.Parameters.AddWithValue("@amt", amount);
        cmd.Parameters.AddWithValue("@date", (object?)paymentDate ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    // -------------------------------------------------------------------------
    // Course helpers
    // -------------------------------------------------------------------------

    public static int InsertCourse(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO lkp_courses (Name) VALUES (@name)";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    public static int InsertStudentCourse(SqliteConnection conn,
        int personId, int courseId, string dateStarted, string? dateFinished = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted, DateFinished)
            VALUES (@pid, @cid, @start, @finish)";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@cid", courseId);
        cmd.Parameters.AddWithValue("@start", dateStarted);
        cmd.Parameters.AddWithValue("@finish", (object?)dateFinished ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    // -------------------------------------------------------------------------
    // CsWorkLog helper
    // -------------------------------------------------------------------------

    public static int InsertCsWork(SqliteConnection conn,
        int csId, int pcId, string date, int lengthSec, string? notes = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cs_work_log (CsId, PcId, WorkDate, LengthSeconds, Notes, CreatedAt)
            VALUES (@csId, @pcId, @date, @len, @notes, datetime('now'))";
        cmd.Parameters.AddWithValue("@csId", csId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@date", date);
        cmd.Parameters.AddWithValue("@len",  lengthSec);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    // -------------------------------------------------------------------------
    // Message helpers
    // -------------------------------------------------------------------------

    public static int InsertStaffMessage(SqliteConnection conn,
        int fromId, int toId, string text)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sys_staff_messages (FromStaffId, ToStaffId, MsgText)
            VALUES (@from, @to, @msg)";
        cmd.Parameters.AddWithValue("@from", fromId);
        cmd.Parameters.AddWithValue("@to", toId);
        cmd.Parameters.AddWithValue("@msg", text);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    // -------------------------------------------------------------------------
    // Activity log helpers
    // -------------------------------------------------------------------------

    public static int InsertActivityLog(SqliteConnection conn,
        string username, string activityAt, string action, string kind)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sys_activity_log (Username, ActivityAt, Action, Kind)
            VALUES (@u, @at, @a, @k)";
        cmd.Parameters.AddWithValue("@u",  username);
        cmd.Parameters.AddWithValue("@at", activityAt);
        cmd.Parameters.AddWithValue("@a",  action);
        cmd.Parameters.AddWithValue("@k",  kind);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    // -------------------------------------------------------------------------
    // Password hashing — same algorithm as UserDb.HashPassword
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes a PBKDF2-SHA256 hash in the same format as UserDb.HashPassword.
    /// Used to pre-insert users with known passwords into test databases.
    /// </summary>
    public static string HashPassword(string password)
    {
        const int iterations = 260000;
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        using var kdf = new Rfc2898DeriveBytes(
            password, salt, iterations, HashAlgorithmName.SHA256);
        byte[] hash = kdf.GetBytes(32);
        return $"pbkdf2_sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private static object Nv(string? s) =>
        string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
}
