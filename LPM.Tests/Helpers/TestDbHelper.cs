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
    // Full schema
    // -------------------------------------------------------------------------

    private static void InitSchema(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Persons (
                PersonId    INTEGER PRIMARY KEY AUTOINCREMENT,
                FirstName   TEXT NOT NULL,
                LastName    TEXT,
                Phone       TEXT,
                Email       TEXT,
                DateOfBirth TEXT,
                Gender      TEXT,
                IsActive    INTEGER NOT NULL DEFAULT 1,
                ExternalId  TEXT,
                Notes       TEXT,
                Origin      TEXT,
                Org         TEXT,
                Referral    TEXT,
                CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Grades (
                GradeId   INTEGER PRIMARY KEY AUTOINCREMENT,
                Code      TEXT NOT NULL UNIQUE,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                DefaultRateCentsPerHour INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT,
                UpdatedByUserId INTEGER
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Auditors (
                AuditorId      INTEGER PRIMARY KEY,
                CurrentGradeId INTEGER,
                IsActive       INTEGER NOT NULL DEFAULT 1,
                Type           INTEGER NOT NULL DEFAULT 1,
                AllowAll       INTEGER NOT NULL DEFAULT 0
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS CaseSupervisors (
                CsId     INTEGER PRIMARY KEY,
                GradeId  INTEGER,
                IsActive INTEGER NOT NULL DEFAULT 1
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS PCs (
                PcId INTEGER NOT NULL PRIMARY KEY
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Sessions (
                SessionId               INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId                    INTEGER NOT NULL,
                AuditorId               INTEGER NOT NULL,
                SessionDate             TEXT    NOT NULL,
                SequenceInDay           INTEGER NOT NULL DEFAULT 1,
                LengthSeconds           INTEGER NOT NULL DEFAULT 0,
                AdminSeconds            INTEGER NOT NULL DEFAULT 0,
                IsFreeSession           INTEGER NOT NULL DEFAULT 0,
                ChargeSeconds           INTEGER NOT NULL DEFAULT 0,
                ChargedRateCentsPerHour INTEGER NOT NULL DEFAULT 0,
                SessionSummaryHtml      TEXT,
                CreatedAt               TEXT    NOT NULL DEFAULT (datetime('now')),
                VerifiedStatus          TEXT    NOT NULL DEFAULT 'Draft',
                AuditorSalaryCentsPerHour INTEGER NOT NULL DEFAULT 0
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS CsReviews (
                CsReviewId          INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId           INTEGER NOT NULL UNIQUE,
                CsId                INTEGER NOT NULL,
                ReviewLengthSeconds INTEGER NOT NULL DEFAULT 0,
                ReviewedAt          TEXT,
                Status              TEXT    NOT NULL DEFAULT 'Draft'
                                    CHECK(Status IN ('Draft','Approved','NeedsCorrection','Rejected')),
                Notes               TEXT,
                CsSalaryCentsPerHour INTEGER NOT NULL DEFAULT 0
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS CsWorkLog (
                CsWorkLogId   INTEGER PRIMARY KEY AUTOINCREMENT,
                CsId          INTEGER NOT NULL,
                PcId          INTEGER NOT NULL,
                WorkDate      TEXT    NOT NULL,
                LengthSeconds INTEGER NOT NULL DEFAULT 0,
                Notes         TEXT,
                CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Payments (
                PaymentId   INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId        INTEGER NOT NULL,
                PaymentDate TEXT    NOT NULL,
                HoursBought INTEGER NOT NULL DEFAULT 0,
                AmountPaid  INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
                PaymentType TEXT    DEFAULT 'Auditing',
                CourseId    INTEGER,
                RegistrarId INTEGER,
                ReferralId  INTEGER,
                PurchaseId  INTEGER
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS StaffPcList (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId       INTEGER NOT NULL,
                PcId         INTEGER NOT NULL,
                WorkCapacity TEXT    NOT NULL DEFAULT 'Auditor',
                UNIQUE(UserId, PcId, WorkCapacity)
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS MiscCharge (
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

        // UNIQUE(PersonId, VisitDate) matches the real schema in AcademyService
        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS AcademyAttendance (
                StudentId INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId  INTEGER NOT NULL,
                VisitDate TEXT    NOT NULL,
                UNIQUE(PersonId, VisitDate)
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Users (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Username     TEXT    NOT NULL UNIQUE,
                PasswordHash TEXT    NOT NULL,
                IsActive     INTEGER NOT NULL DEFAULT 1,
                AvatarPath   TEXT
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Roles (
                RoleId INTEGER PRIMARY KEY AUTOINCREMENT,
                Code   TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL DEFAULT ''
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS UserRoles (
                UserId INTEGER NOT NULL,
                RoleId INTEGER NOT NULL,
                PRIMARY KEY (UserId, RoleId)
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Courses (
                CourseId INTEGER PRIMARY KEY AUTOINCREMENT,
                Name     TEXT    NOT NULL
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS StudentCourses (
                StudentCourseId INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId        INTEGER NOT NULL,
                CourseId        INTEGER NOT NULL,
                DateStarted     TEXT    NOT NULL,
                DateFinished    TEXT    NULL
            )");

        Exec(conn, @"
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
                CreatedAt          TEXT NOT NULL DEFAULT (datetime('now')),
                IsDeleted          INTEGER NOT NULL DEFAULT 0
            )");

        Exec(conn, @"
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
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS PurchasePaymentMethods (
                PaymentMethodId INTEGER PRIMARY KEY AUTOINCREMENT,
                PurchaseId      INTEGER NOT NULL,
                MethodType      TEXT NOT NULL DEFAULT 'Cash',
                Amount          INTEGER NOT NULL DEFAULT 0,
                PaymentDate     TEXT,
                IsMoneyInBank   INTEGER NOT NULL DEFAULT 0,
                MoneyInBankDate TEXT,
                FOREIGN KEY (PurchaseId) REFERENCES Purchases(PurchaseId)
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS StaffMessages (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                FromStaffId     INTEGER NOT NULL,
                ToStaffId       INTEGER NOT NULL,
                MsgText         TEXT    NOT NULL,
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                AcknowledgedAt  TEXT    NULL
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS AuditorPcPermissions (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                AuditorId   INTEGER NOT NULL,
                PcId        INTEGER NOT NULL,
                IsApproved  INTEGER NOT NULL DEFAULT 0,
                RequestedAt TEXT NOT NULL DEFAULT (date('now')),
                UNIQUE(AuditorId, PcId)
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS WeeklyRemarks (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                AuditorId   INTEGER NOT NULL,
                WeekDate    TEXT    NOT NULL,
                Remarks     TEXT    NOT NULL DEFAULT '',
                SubmittedAt TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE(AuditorId, WeekDate)
            )");

        // Seed standard grades (BA, MA, PHD)
        Exec(conn, "INSERT INTO Grades (Code, SortOrder) VALUES ('BA',1),('MA',2),('PHD',3)");

        // Seed standard roles
        Exec(conn, "INSERT INTO Roles (Code, DisplayName) VALUES ('CaseWorker','Case Worker (Auditor/CS)'),('Admin','Administrator'),('Viewer','Viewer (Read Only)')");
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

    /// <summary>Inserts a Person and returns the new PersonId.</summary>
    public static int InsertPerson(SqliteConnection conn,
        string firstName, string lastName = "",
        string phone = "", string email = "",
        string dateOfBirth = "", string gender = "")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Persons (FirstName, LastName, Phone, Email, DateOfBirth, Gender)
            VALUES (@fn, @ln, @ph, @em, @dob, @gender)";
        cmd.Parameters.AddWithValue("@fn",  firstName);
        cmd.Parameters.AddWithValue("@ln",  lastName);
        cmd.Parameters.AddWithValue("@ph",  Nv(phone));
        cmd.Parameters.AddWithValue("@em",  Nv(email));
        cmd.Parameters.AddWithValue("@dob", Nv(dateOfBirth));
        cmd.Parameters.AddWithValue("@gender", Nv(gender));
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    /// <summary>Inserts an Auditor row (assumes PersonId already exists).</summary>
    public static void InsertAuditor(SqliteConnection conn,
        int personId, int type = 1, bool isActive = true, int? gradeId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Auditors (AuditorId, CurrentGradeId, IsActive, Type)
            VALUES (@id, @gid, @active, @type)";
        cmd.Parameters.AddWithValue("@id",     personId);
        cmd.Parameters.AddWithValue("@gid",    gradeId.HasValue ? (object)gradeId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@type",   type);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Inserts a CaseSupervisor row (assumes PersonId already exists).</summary>
    public static void InsertCS(SqliteConnection conn, int personId, bool isActive = true)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO CaseSupervisors (CsId, IsActive) VALUES (@id, @active)";
        cmd.Parameters.AddWithValue("@id",     personId);
        cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Inserts a PC row (assumes PersonId already exists).</summary>
    public static void InsertPC(SqliteConnection conn, int personId, string? externalId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO PCs (PcId) VALUES (@id)";
        cmd.Parameters.AddWithValue("@id", personId);
        cmd.ExecuteNonQuery();

        if (!string.IsNullOrEmpty(externalId))
        {
            using var upd = conn.CreateCommand();
            upd.CommandText = "UPDATE Persons SET ExternalId=@ext WHERE PersonId=@id";
            upd.Parameters.AddWithValue("@ext", externalId);
            upd.Parameters.AddWithValue("@id", personId);
            upd.ExecuteNonQuery();
        }
    }

    /// <summary>Inserts a Session and returns the new SessionId.</summary>
    public static int InsertSession(SqliteConnection conn,
        int pcId, int auditorId, string date,
        int lengthSec, int adminSec = 0,
        bool isFree = false,
        int seqInDay = 1, string verifiedStatus = "Draft")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Sessions
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
        cmd.Parameters.AddWithValue("@audId", auditorId);
        cmd.Parameters.AddWithValue("@date",  date);
        cmd.Parameters.AddWithValue("@seq",   seqInDay);
        cmd.Parameters.AddWithValue("@len",   lengthSec);
        cmd.Parameters.AddWithValue("@adm",   adminSec);
        cmd.Parameters.AddWithValue("@free",  isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@vs",    verifiedStatus);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    /// <summary>Inserts a Payment and returns the new PaymentId.</summary>
    public static int InsertPayment(SqliteConnection conn,
        int pcId, string date, int hoursBought, int amountPaid = 0)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Payments (PcId, PaymentDate, HoursBought, AmountPaid)
            VALUES (@pcId, @date, @hrs, @amt)";
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@date",  date);
        cmd.Parameters.AddWithValue("@hrs",   hoursBought);
        cmd.Parameters.AddWithValue("@amt",   amountPaid);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    /// <summary>Inserts an academy visit (AcademyAttendance row).</summary>
    public static void InsertVisit(SqliteConnection conn, int personId, string date)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO AcademyAttendance (PersonId, VisitDate) VALUES (@pid, @date)";
        cmd.Parameters.AddWithValue("@pid",  personId);
        cmd.Parameters.AddWithValue("@date", date);
        cmd.ExecuteNonQuery();
    }

    // -------------------------------------------------------------------------
    // Auth helpers
    // -------------------------------------------------------------------------

    /// <summary>Inserts a User with PBKDF2-SHA256 hashed password. Returns the new UserId.</summary>
    public static int InsertUser(SqliteConnection conn,
        string username, string password, bool isActive = true)
    {
        var hash = HashPassword(password);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Users (Username, PasswordHash, IsActive)
            VALUES (@u, @h, @a)";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    /// <summary>Returns the RoleId for the given role code (pre-seeded in test DB).</summary>
    public static int GetRoleId(SqliteConnection conn, string code)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT RoleId FROM Roles WHERE Code=@c";
        cmd.Parameters.AddWithValue("@c", code);
        return (int)(long)(cmd.ExecuteScalar()!);
    }

    /// <summary>Assigns a role to a user.</summary>
    public static void AssignRole(SqliteConnection conn, int userId, string roleCode)
    {
        var roleId = GetRoleId(conn, roleCode);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO UserRoles (UserId, RoleId) VALUES (@uid, @rid)";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@rid", roleId);
        cmd.ExecuteNonQuery();
    }

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
    // CsWorkLog helper
    // -------------------------------------------------------------------------

    public static int InsertCsWork(SqliteConnection conn,
        int csId, int pcId, string date, int lengthSec, string? notes = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO CsWorkLog (CsId, PcId, WorkDate, LengthSeconds, Notes, CreatedAt)
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
    // Purchase helpers
    // -------------------------------------------------------------------------

    public static int InsertPurchase(SqliteConnection conn,
        int pcId, string date, string notes = "", string status = "Pending",
        int? createdBy = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Purchases (PcId, PurchaseDate, Notes, ApprovedStatus, CreatedByPersonId)
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
            INSERT INTO PurchaseItems (PurchaseId, ItemType, CourseId, HoursBought, AmountPaid)
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
            INSERT INTO PurchasePaymentMethods (PurchaseId, MethodType, Amount, PaymentDate)
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
        cmd.CommandText = "INSERT INTO Courses (Name) VALUES (@name)";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    public static int InsertStudentCourse(SqliteConnection conn,
        int personId, int courseId, string dateStarted, string? dateFinished = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO StudentCourses (PersonId, CourseId, DateStarted, DateFinished)
            VALUES (@pid, @cid, @start, @finish)";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@cid", courseId);
        cmd.Parameters.AddWithValue("@start", dateStarted);
        cmd.Parameters.AddWithValue("@finish", (object?)dateFinished ?? DBNull.Value);
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
            INSERT INTO StaffMessages (FromStaffId, ToStaffId, MsgText)
            VALUES (@from, @to, @msg)";
        cmd.Parameters.AddWithValue("@from", fromId);
        cmd.Parameters.AddWithValue("@to", toId);
        cmd.Parameters.AddWithValue("@msg", text);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private static object Nv(string? s) =>
        string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
}
