using Microsoft.Data.Sqlite;

namespace LPM.Tests.Helpers;

/// <summary>
/// Creates a temporary SQLite database file with the full lifepower.db schema.
/// Each test class calls CreateTempDb() to get an isolated database path,
/// then calls Cleanup() in Dispose() to remove the file.
/// </summary>
public static class TestDbHelper
{
    /// <summary>
    /// Creates a new temporary SQLite file with all required tables and seed data.
    /// Returns the absolute path to the file.
    /// </summary>
    public static string CreateTempDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lpm_test_{Guid.NewGuid():N}.db");
        InitSchema(path);
        return path;
    }

    /// <summary>
    /// Flushes the SQLite connection pool for the given file and then deletes it.
    /// Clearing the pool is required on Windows because SQLite keeps file handles
    /// in its pool even after individual connections are closed.
    /// </summary>
    public static void Cleanup(string path)
    {
        // Force all pooled connections for this DB to be released before deletion
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                // Also delete any WAL/SHM side-files
                foreach (var ext in new[] { "-wal", "-shm" })
                {
                    var side = path + ext;
                    if (File.Exists(side)) File.Delete(side);
                }
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }
        // If still locked after retries, ignore – the OS will clean up temp files eventually
    }

    // -------------------------------------------------------------------------
    // Schema initialisation
    // -------------------------------------------------------------------------

    private static void InitSchema(string dbPath)
    {
        var cs = $"Data Source={dbPath}";
        using var conn = new SqliteConnection(cs);
        conn.Open();

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Persons (
                PersonId    INTEGER PRIMARY KEY AUTOINCREMENT,
                FirstName   TEXT NOT NULL,
                LastName    TEXT,
                Phone       TEXT,
                Email       TEXT,
                Age         INTEGER,
                DateOfBirth TEXT,
                Sex         TEXT
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
                PcId       INTEGER NOT NULL,
                ExternalId TEXT,
                Notes      TEXT,
                StartDate  TEXT,
                Phone      TEXT,
                Email      TEXT
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Sessions (
                SessionId                INTEGER PRIMARY KEY AUTOINCREMENT,
                PcId                     INTEGER NOT NULL,
                AuditorId                INTEGER NOT NULL,
                SessionDate              TEXT    NOT NULL,
                SequenceInDay            INTEGER NOT NULL DEFAULT 1,
                LengthSeconds            INTEGER NOT NULL DEFAULT 0,
                AdminSeconds             INTEGER NOT NULL DEFAULT 0,
                IsFreeSession            INTEGER NOT NULL DEFAULT 0,
                ChargeSeconds            INTEGER NOT NULL DEFAULT 0,
                ChargedRateCentsPerHour  INTEGER NOT NULL DEFAULT 0,
                SessionSummaryHtml       TEXT,
                CreatedAt                TEXT    NOT NULL DEFAULT (datetime('now')),
                VerifiedStatus           TEXT    NOT NULL DEFAULT 'Draft',
                IsSolo                   INTEGER NOT NULL DEFAULT 0
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
                Notes       TEXT,
                CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS StaffPcList (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId       INTEGER NOT NULL,
                PcId         INTEGER NOT NULL,
                WorkCapacity TEXT    NOT NULL DEFAULT 'Auditor',
                IsSolo       INTEGER NOT NULL DEFAULT 0,
                UNIQUE(UserId, PcId, IsSolo)
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

        Exec(conn, @"
            CREATE TABLE IF NOT EXISTS Students (
                StudentId INTEGER PRIMARY KEY AUTOINCREMENT,
                PersonId  INTEGER NOT NULL,
                VisitDate TEXT    NOT NULL
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

        // Seed standard grades
        Exec(conn, "INSERT INTO Grades (Code, SortOrder) VALUES ('BA',1),('MA',2),('PHD',3)");
    }

    // -------------------------------------------------------------------------
    // Helpers
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

    /// <summary>
    /// Inserts a Person and returns the new PersonId.
    /// </summary>
    public static int InsertPerson(SqliteConnection conn,
        string firstName, string lastName = "")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Persons (FirstName, LastName) VALUES (@fn, @ln)";
        cmd.Parameters.AddWithValue("@fn", firstName);
        cmd.Parameters.AddWithValue("@ln", lastName);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    /// <summary>
    /// Inserts an Auditor row (assumes PersonId already exists).
    /// </summary>
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

    /// <summary>
    /// Inserts a CaseSupervisor row (assumes PersonId already exists).
    /// </summary>
    public static void InsertCS(SqliteConnection conn,
        int personId, bool isActive = true)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO CaseSupervisors (CsId, IsActive) VALUES (@id, @active)";
        cmd.Parameters.AddWithValue("@id",     personId);
        cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts a PC row (assumes PersonId already exists).
    /// </summary>
    public static void InsertPC(SqliteConnection conn, int personId, string? externalId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO PCs (PcId, ExternalId) VALUES (@id, @ext)";
        cmd.Parameters.AddWithValue("@id",  personId);
        cmd.Parameters.AddWithValue("@ext", (object?)externalId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts a Session and returns the new SessionId.
    /// </summary>
    public static int InsertSession(SqliteConnection conn,
        int pcId, int auditorId, string date,
        int lengthSec, int adminSec = 0,
        bool isFree = false, bool isSolo = false,
        int seqInDay = 1, string verifiedStatus = "Draft")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Sessions
              (PcId, AuditorId, SessionDate, SequenceInDay,
               LengthSeconds, AdminSeconds, IsFreeSession,
               ChargeSeconds, ChargedRateCentsPerHour,
               CreatedAt, VerifiedStatus, IsSolo)
            VALUES
              (@pcId, @audId, @date, @seq,
               @len, @adm, @free,
               0, 0,
               datetime('now'), @vs, @solo)";
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@audId", auditorId);
        cmd.Parameters.AddWithValue("@date",  date);
        cmd.Parameters.AddWithValue("@seq",   seqInDay);
        cmd.Parameters.AddWithValue("@len",   lengthSec);
        cmd.Parameters.AddWithValue("@adm",   adminSec);
        cmd.Parameters.AddWithValue("@free",  isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@vs",    verifiedStatus);
        cmd.Parameters.AddWithValue("@solo",  isSolo ? 1 : 0);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }

    /// <summary>
    /// Inserts a Payment and returns the new PaymentId.
    /// </summary>
    public static int InsertPayment(SqliteConnection conn,
        int pcId, string date, int hoursBought, int amountPaid = 0, string? notes = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Payments (PcId, PaymentDate, HoursBought, AmountPaid, Notes)
            VALUES (@pcId, @date, @hrs, @amt, @notes)";
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@date",  date);
        cmd.Parameters.AddWithValue("@hrs",   hoursBought);
        cmd.Parameters.AddWithValue("@amt",   amountPaid);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return (int)Scalar(conn, "SELECT last_insert_rowid()");
    }
}
