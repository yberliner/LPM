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
                Sex         TEXT,
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
                Code      TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Auditors (
                AuditorId      INTEGER NOT NULL,
                CurrentGradeId INTEGER,
                IsActive       INTEGER NOT NULL DEFAULT 1,
                Type           INTEGER NOT NULL DEFAULT 1
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS CaseSupervisors (
                CsId     INTEGER NOT NULL,
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
                Notes               TEXT
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
                WorkCapacity TEXT    NOT NULL DEFAULT 'Auditor'
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
                Code   TEXT NOT NULL UNIQUE
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS UserRoles (
                UserId INTEGER NOT NULL,
                RoleId INTEGER NOT NULL,
                PRIMARY KEY (UserId, RoleId)
            )");

        // Seed standard grades (BA, MA, PHD)
        Exec(conn, "INSERT INTO Grades (Code, SortOrder) VALUES ('BA',1),('MA',2),('PHD',3)");

        // Seed standard roles
        Exec(conn, "INSERT INTO Roles (Code) VALUES ('CaseWorker'),('Admin'),('Viewer')");
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
        string dateOfBirth = "", string sex = "")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Persons (FirstName, LastName, Phone, Email, DateOfBirth, Sex)
            VALUES (@fn, @ln, @ph, @em, @dob, @sex)";
        cmd.Parameters.AddWithValue("@fn",  firstName);
        cmd.Parameters.AddWithValue("@ln",  lastName);
        cmd.Parameters.AddWithValue("@ph",  Nv(phone));
        cmd.Parameters.AddWithValue("@em",  Nv(email));
        cmd.Parameters.AddWithValue("@dob", Nv(dateOfBirth));
        cmd.Parameters.AddWithValue("@sex", Nv(sex));
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
    // Private
    // -------------------------------------------------------------------------

    private static object Nv(string? s) =>
        string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
}
