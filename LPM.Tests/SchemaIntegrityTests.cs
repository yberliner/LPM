using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Auth;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Tests that verify the database schema itself:
/// - All expected tables and columns exist after initialization
/// - UNIQUE, NOT NULL, CHECK, and AUTOINCREMENT constraints are enforced
/// - Service schema migrations are idempotent (safe to run twice)
/// - Default column values are correct
/// </summary>
public class SchemaIntegrityTests : IDisposable
{
    private readonly string _dbPath;

    public SchemaIntegrityTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // Table existence
    // =========================================================================

    [Theory]
    [InlineData("Persons")]
    [InlineData("Grades")]
    [InlineData("Auditors")]
    [InlineData("CaseSupervisors")]
    [InlineData("PCs")]
    [InlineData("Sessions")]
    [InlineData("CsReviews")]
    [InlineData("CsWorkLog")]
    [InlineData("Payments")]
    [InlineData("StaffPcList")]
    [InlineData("MiscCharge")]
    [InlineData("AcademyAttendance")]
    [InlineData("Users")]
    [InlineData("Roles")]
    [InlineData("UserRoles")]
    public void Table_ExistsAfterInit(string tableName)
    {
        using var conn = Open();
        Assert.True(TestDbHelper.TableExists(conn, tableName));
    }

    // =========================================================================
    // Column existence – critical columns
    // =========================================================================

    [Theory]
    [InlineData("Persons",   "PersonId")]
    [InlineData("Persons",   "FirstName")]
    [InlineData("Persons",   "LastName")]
    [InlineData("Persons",   "Phone")]
    [InlineData("Persons",   "Email")]
    [InlineData("Persons",   "DateOfBirth")]
    [InlineData("Persons",   "Sex")]
    [InlineData("Auditors",  "AuditorId")]
    [InlineData("Auditors",  "IsActive")]
    [InlineData("Auditors",  "Type")]
    [InlineData("Auditors",  "CurrentGradeId")]
    [InlineData("Sessions",  "SessionId")]
    [InlineData("Sessions",  "PcId")]
    [InlineData("Sessions",  "AuditorId")]
    [InlineData("Sessions",  "SessionDate")]
    [InlineData("Sessions",  "LengthSeconds")]
    [InlineData("Sessions",  "AdminSeconds")]
    [InlineData("Sessions",  "IsFreeSession")]
    [InlineData("Sessions",  "VerifiedStatus")]
    [InlineData("Sessions",  "SequenceInDay")]
    [InlineData("CsReviews", "CsReviewId")]
    [InlineData("CsReviews", "SessionId")]
    [InlineData("CsReviews", "CsId")]
    [InlineData("CsReviews", "ReviewLengthSeconds")]
    [InlineData("CsReviews", "Status")]
    [InlineData("CsReviews", "Notes")]
    [InlineData("Payments",  "PaymentId")]
    [InlineData("Payments",  "PcId")]
    [InlineData("Payments",  "HoursBought")]
    [InlineData("Payments",  "AmountPaid")]
    [InlineData("AcademyAttendance",  "StudentId")]
    [InlineData("AcademyAttendance",  "PersonId")]
    [InlineData("AcademyAttendance",  "VisitDate")]
    [InlineData("Users",     "Id")]
    [InlineData("Users",     "Username")]
    [InlineData("Users",     "PasswordHash")]
    [InlineData("Users",     "IsActive")]
    [InlineData("Users",     "AvatarPath")]
    [InlineData("Roles",     "RoleId")]
    [InlineData("Roles",     "Code")]
    [InlineData("StaffPcList","UserId")]
    [InlineData("StaffPcList","PcId")]
    [InlineData("StaffPcList","WorkCapacity")]
    public void Column_ExistsAfterInit(string table, string column)
    {
        using var conn = Open();
        Assert.True(TestDbHelper.ColumnExists(conn, table, column));
    }

    // =========================================================================
    // Seed data
    // =========================================================================

    [Fact]
    public void Grades_SeedData_HasThreeRows()
    {
        using var conn = Open();
        Assert.Equal(3L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM Grades"));
    }

    [Fact]
    public void Grades_SeedData_Contains_BA_MA_PHD()
    {
        using var conn = Open();
        Assert.Equal(1L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM Grades WHERE Code='BA'"));
        Assert.Equal(1L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM Grades WHERE Code='MA'"));
        Assert.Equal(1L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM Grades WHERE Code='PHD'"));
    }

    [Fact]
    public void Roles_SeedData_Contains_CaseWorker_Admin_Viewer()
    {
        using var conn = Open();
        Assert.Equal(1L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM Roles WHERE Code='CaseWorker'"));
        Assert.Equal(1L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM Roles WHERE Code='Admin'"));
        Assert.Equal(1L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM Roles WHERE Code='Viewer'"));
    }

    // =========================================================================
    // UNIQUE constraints
    // =========================================================================

    [Fact]
    public void CsReviews_UniqueConstraint_OnSessionId_Enforced()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);
        var sid   = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);

        TestDbHelper.Exec(conn, $"INSERT INTO CsReviews (SessionId, CsId, ReviewLengthSeconds, Status) VALUES ({sid}, {audId}, 600, 'Draft')");

        Assert.Throws<SqliteException>(() =>
            TestDbHelper.Exec(conn, $"INSERT INTO CsReviews (SessionId, CsId, ReviewLengthSeconds, Status) VALUES ({sid}, {audId}, 900, 'Approved')")
        );
    }

    [Fact]
    public void AcademyAttendance_UniqueConstraint_OnPersonIdAndVisitDate_Enforced()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Alice");

        TestDbHelper.Exec(conn, $"INSERT INTO AcademyAttendance (PersonId, VisitDate) VALUES ({pid}, '2024-01-15')");

        Assert.Throws<SqliteException>(() =>
            TestDbHelper.Exec(conn, $"INSERT INTO AcademyAttendance (PersonId, VisitDate) VALUES ({pid}, '2024-01-15')")
        );
    }

    [Fact]
    public void AcademyAttendance_UniqueConstraint_AllowsSamePerson_DifferentDays()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Alice");

        TestDbHelper.Exec(conn, $"INSERT INTO AcademyAttendance (PersonId, VisitDate) VALUES ({pid}, '2024-01-15')");
        TestDbHelper.Exec(conn, $"INSERT INTO AcademyAttendance (PersonId, VisitDate) VALUES ({pid}, '2024-01-16')");

        Assert.Equal(2L, TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM AcademyAttendance WHERE PersonId={pid}"));
    }

    [Fact]
    public void StaffPcList_UniqueConstraint_OnUserIdPcIdWorkCapacity_Enforced()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertPerson(conn, "Aud1");
        var pid = TestDbHelper.InsertPerson(conn, "Client1");

        TestDbHelper.Exec(conn, $"INSERT OR IGNORE INTO StaffPcList (UserId, PcId, WorkCapacity) VALUES ({uid}, {pid}, 'Auditor')");
        TestDbHelper.Exec(conn, $"INSERT OR IGNORE INTO StaffPcList (UserId, PcId, WorkCapacity) VALUES ({uid}, {pid}, 'Auditor')"); // duplicate ignored

        Assert.Equal(1L, TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM StaffPcList WHERE UserId={uid} AND PcId={pid}"));
    }

    [Fact]
    public void Users_UniqueConstraint_OnUsername_Enforced()
    {
        using var conn = Open();
        TestDbHelper.Exec(conn, "INSERT INTO Users (Username, PasswordHash, IsActive) VALUES ('tami', 'hash1', 1)");

        Assert.Throws<SqliteException>(() =>
            TestDbHelper.Exec(conn, "INSERT INTO Users (Username, PasswordHash, IsActive) VALUES ('tami', 'hash2', 1)")
        );
    }

    // =========================================================================
    // CHECK constraints
    // =========================================================================

    [Fact]
    public void CsReviews_CheckConstraint_RejectsInvalidStatus()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);
        var sid   = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);

        Assert.Throws<SqliteException>(() =>
            TestDbHelper.Exec(conn, $"INSERT INTO CsReviews (SessionId, CsId, ReviewLengthSeconds, Status) VALUES ({sid}, {audId}, 600, 'InvalidStatus')")
        );
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Approved")]
    [InlineData("NeedsCorrection")]
    [InlineData("Rejected")]
    public void CsReviews_CheckConstraint_AcceptsValidStatuses(string status)
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud" + status);
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client" + status);
        TestDbHelper.InsertPC(conn, pcId);
        var sid   = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);

        var ex = Record.Exception(() =>
            TestDbHelper.Exec(conn, $"INSERT INTO CsReviews (SessionId, CsId, ReviewLengthSeconds, Status) VALUES ({sid}, {audId}, 600, '{status}')")
        );
        Assert.Null(ex);
    }

    // =========================================================================
    // Default column values
    // =========================================================================

    [Fact]
    public void Sessions_DefaultVerifiedStatus_IsDraft()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        TestDbHelper.Exec(conn, $@"
            INSERT INTO Sessions (PcId, AuditorId, SessionDate, SequenceInDay,
                                  LengthSeconds, AdminSeconds, IsFreeSession,
                                  ChargeSeconds, ChargedRateCentsPerHour)
            VALUES ({pcId}, {audId}, '2024-01-11', 1, 3600, 0, 0, 0, 0)");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT VerifiedStatus FROM Sessions ORDER BY SessionId DESC LIMIT 1";
        Assert.Equal("Draft", cmd.ExecuteScalar() as string);
    }

    [Fact]
    public void CsReviews_DefaultStatus_IsDraft()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);
        var sid   = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);

        TestDbHelper.Exec(conn, $"INSERT INTO CsReviews (SessionId, CsId, ReviewLengthSeconds) VALUES ({sid}, {audId}, 600)");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Status FROM CsReviews ORDER BY CsReviewId DESC LIMIT 1";
        Assert.Equal("Draft", cmd.ExecuteScalar() as string);
    }

    [Fact]
    public void Auditors_DefaultIsActive_IsOne()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.Exec(conn, $"INSERT INTO Auditors (AuditorId) VALUES ({pid})");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT IsActive FROM Auditors WHERE AuditorId={pid}";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public void Auditors_DefaultType_IsOne()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.Exec(conn, $"INSERT INTO Auditors (AuditorId) VALUES ({pid})");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT Type FROM Auditors WHERE AuditorId={pid}";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public void Payments_DefaultHoursBought_IsZero()
    {
        using var conn = Open();
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);
        TestDbHelper.Exec(conn, $"INSERT INTO Payments (PcId, PaymentDate, AmountPaid) VALUES ({pcId}, '2024-01-01', 500)");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT HoursBought FROM Payments ORDER BY PaymentId DESC LIMIT 1";
        Assert.Equal(0L, (long)(cmd.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public void StaffPcList_DefaultWorkCapacity_IsAuditor()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertPerson(conn, "Aud1");
        var pid = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.Exec(conn, $"INSERT INTO StaffPcList (UserId, PcId) VALUES ({uid}, {pid})");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT WorkCapacity FROM StaffPcList WHERE UserId={uid} AND PcId={pid}";
        Assert.Equal("Auditor", cmd.ExecuteScalar() as string);
    }

    // =========================================================================
    // AUTOINCREMENT behavior
    // =========================================================================

    [Fact]
    public void Persons_AutoIncrement_AssignsSequentialIds()
    {
        using var conn = Open();
        var id1 = TestDbHelper.InsertPerson(conn, "A");
        var id2 = TestDbHelper.InsertPerson(conn, "B");
        var id3 = TestDbHelper.InsertPerson(conn, "C");
        Assert.True(id1 < id2);
        Assert.True(id2 < id3);
    }

    [Fact]
    public void Sessions_AutoIncrement_AssignsSequentialIds()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid1 = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600, seqInDay: 1);
        var sid2 = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-12", 1800, seqInDay: 1);
        Assert.True(sid1 < sid2);
    }

    [Fact]
    public void Payments_AutoIncrement_AssignsSequentialIds()
    {
        using var conn = Open();
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var p1 = TestDbHelper.InsertPayment(conn, pcId, "2024-01-01", 2);
        var p2 = TestDbHelper.InsertPayment(conn, pcId, "2024-02-01", 3);
        Assert.True(p1 < p2);
    }

    // =========================================================================
    // Service schema migration idempotency
    // =========================================================================

    [Fact]
    public void DashboardService_RunMigrations_IsIdempotent()
    {
        // Two instantiations should not throw (migrations check-before-alter)
        var ex = Record.Exception(() =>
        {
            _ = new DashboardService(TestConfig.For(_dbPath));
            _ = new DashboardService(TestConfig.For(_dbPath));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void PcService_EnsureSchema_IsIdempotent()
    {
        var ex = Record.Exception(() =>
        {
            _ = new PcService(TestConfig.For(_dbPath));
            _ = new PcService(TestConfig.For(_dbPath));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void AcademyService_EnsureSchema_IsIdempotent()
    {
        var ex = Record.Exception(() =>
        {
            _ = new AcademyService(TestConfig.For(_dbPath));
            _ = new AcademyService(TestConfig.For(_dbPath));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void UserDb_EnsureSchema_IsIdempotent()
    {
        var ex = Record.Exception(() =>
        {
            _ = new UserDb(TestConfig.For(_dbPath));
            _ = new UserDb(TestConfig.For(_dbPath));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void AllServices_CanBeInstantiatedTogether_NoConflicts()
    {
        var ex = Record.Exception(() =>
        {
            _ = new DashboardService(TestConfig.For(_dbPath));
            _ = new PcService(TestConfig.For(_dbPath));
            _ = new AuditorService(TestConfig.For(_dbPath));
            _ = new AcademyService(TestConfig.For(_dbPath));
            _ = new StatisticsService(TestConfig.For(_dbPath));
            _ = new UserDb(TestConfig.For(_dbPath));
        });
        Assert.Null(ex);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }
}
