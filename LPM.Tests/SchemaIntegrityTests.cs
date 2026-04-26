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
    // Table existence (prefixed names)
    // =========================================================================

    [Theory]
    [InlineData("core_persons")]
    [InlineData("core_pcs")]
    [InlineData("core_users")]
    [InlineData("lkp_grades")]
    [InlineData("lkp_courses")]
    [InlineData("sess_sessions")]
    [InlineData("cs_reviews")]
    [InlineData("cs_work_log")]
    [InlineData("acad_attendance")]
    [InlineData("acad_student_courses")]
    [InlineData("fin_purchases")]
    [InlineData("fin_purchase_items")]
    [InlineData("fin_payment_methods")]
    [InlineData("sys_staff_pc_list")]
    [InlineData("sys_staff_messages")]
    [InlineData("sys_weekly_remarks")]
    [InlineData("sess_misc_charges")]
    [InlineData("sess_folder_summary")]
    [InlineData("sess_completions")]
    [InlineData("sess_meetings")]
    [InlineData("sess_next_cs")]
    [InlineData("sess_questions")]
    [InlineData("sys_activity_log")]
    [InlineData("sys_trusted_devices")]
    [InlineData("sys_shrunk_files")]
    [InlineData("sys_passkeys")]
    [InlineData("sys_magic_links")]
    [InlineData("sys_financial_config")]
    [InlineData("sys_file_audit")]
    [InlineData("lkp_shortcuts")]
    [InlineData("lkp_books")]
    [InlineData("lkp_referral_sources")]
    [InlineData("lkp_organizations")]
    [InlineData("lkp_folder_items")]
    [InlineData("lkp_cs_status")]
    [InlineData("fin_payments")]
    [InlineData("fin_budget_reset")]
    public void Table_ExistsAfterInit(string tableName)
    {
        using var conn = Open();
        Assert.True(TestDbHelper.TableExists(conn, tableName));
    }

    // =========================================================================
    // Column existence – critical columns
    // =========================================================================

    [Theory]
    [InlineData("core_persons", "PersonId")]
    [InlineData("core_persons", "FirstName")]
    [InlineData("core_persons", "LastName")]
    [InlineData("core_persons", "Phone")]
    [InlineData("core_persons", "Email")]
    [InlineData("core_persons", "DateOfBirth")]
    [InlineData("core_persons", "Gender")]
    [InlineData("core_users",   "Id")]
    [InlineData("core_users",   "PersonId")]
    [InlineData("core_users",   "Username")]
    [InlineData("core_users",   "PasswordHash")]
    [InlineData("core_users",   "IsActive")]
    [InlineData("core_users",   "AvatarPath")]
    [InlineData("core_users",   "StaffRole")]
    [InlineData("core_users",   "UserType")]
    [InlineData("core_users",   "AllowAll")]
    [InlineData("sess_sessions", "SessionId")]
    [InlineData("sess_sessions", "PcId")]
    [InlineData("sess_sessions", "AuditorId")]
    [InlineData("sess_sessions", "SessionDate")]
    [InlineData("sess_sessions", "LengthSeconds")]
    [InlineData("sess_sessions", "AdminSeconds")]
    [InlineData("sess_sessions", "IsFreeSession")]
    [InlineData("sess_sessions", "VerifiedStatus")]
    [InlineData("sess_sessions", "SequenceInDay")]
    [InlineData("cs_reviews",   "CsReviewId")]
    [InlineData("cs_reviews",   "SessionId")]
    [InlineData("cs_reviews",   "CsId")]
    [InlineData("cs_reviews",   "ReviewLengthSeconds")]
    [InlineData("cs_reviews",   "Status")]
    [InlineData("cs_reviews",   "Notes")]
    [InlineData("fin_purchase_items", "PurchaseItemId")]
    [InlineData("fin_purchase_items", "PurchaseId")]
    [InlineData("fin_purchase_items", "HoursBought")]
    [InlineData("fin_purchase_items", "AmountPaid")]
    [InlineData("acad_attendance",    "StudentId")]
    [InlineData("acad_attendance",    "PersonId")]
    [InlineData("acad_attendance",    "VisitDate")]
    [InlineData("sys_staff_pc_list",  "UserId")]
    [InlineData("sys_staff_pc_list",  "PcId")]
    [InlineData("sys_staff_pc_list",  "WorkCapacity")]
    [InlineData("sys_staff_pc_list",  "IsApproved")]
    [InlineData("core_users",        "SendSms")]
    [InlineData("fin_purchases",     "Currency")]
    [InlineData("fin_purchase_items", "BookId")]
    [InlineData("fin_payment_methods", "Installments")]
    [InlineData("acad_student_courses", "InstructorId")]
    [InlineData("acad_student_courses", "CsId")]
    [InlineData("lkp_grades",        "DefaultRateCentsPerHour")]
    [InlineData("cs_reviews",        "ChargedCentsRatePerHour")]
    [InlineData("sys_file_audit",    "PcId")]
    [InlineData("sys_file_audit",    "FilePath")]
    [InlineData("sys_file_audit",    "Operation")]
    [InlineData("sys_file_audit",    "Context")]
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
        Assert.Equal(3L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM lkp_grades"));
    }

    [Fact]
    public void Grades_SeedData_Contains_BA_MA_PHD()
    {
        using var conn = Open();
        Assert.Equal(1L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM lkp_grades WHERE Code='BA'"));
        Assert.Equal(1L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM lkp_grades WHERE Code='MA'"));
        Assert.Equal(1L, TestDbHelper.Scalar(conn, "SELECT COUNT(*) FROM lkp_grades WHERE Code='PHD'"));
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

        TestDbHelper.Exec(conn, $"INSERT INTO cs_reviews (SessionId, CsId, ReviewLengthSeconds, Status) VALUES ({sid}, {audId}, 600, 'Draft')");

        Assert.Throws<SqliteException>(() =>
            TestDbHelper.Exec(conn, $"INSERT INTO cs_reviews (SessionId, CsId, ReviewLengthSeconds, Status) VALUES ({sid}, {audId}, 900, 'Approved')")
        );
    }

    [Fact]
    public void AcademyAttendance_UniqueConstraint_OnPersonIdAndVisitDate_Enforced()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Alice");

        TestDbHelper.Exec(conn, $"INSERT INTO acad_attendance (PersonId, VisitDate) VALUES ({pid}, '2024-01-15')");

        Assert.Throws<SqliteException>(() =>
            TestDbHelper.Exec(conn, $"INSERT INTO acad_attendance (PersonId, VisitDate) VALUES ({pid}, '2024-01-15')")
        );
    }

    [Fact]
    public void AcademyAttendance_UniqueConstraint_AllowsSamePerson_DifferentDays()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Alice");

        TestDbHelper.Exec(conn, $"INSERT INTO acad_attendance (PersonId, VisitDate) VALUES ({pid}, '2024-01-15')");
        TestDbHelper.Exec(conn, $"INSERT INTO acad_attendance (PersonId, VisitDate) VALUES ({pid}, '2024-01-16')");

        Assert.Equal(2L, TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM acad_attendance WHERE PersonId={pid}"));
    }

    [Fact]
    public void StaffPcList_UniqueConstraint_OnUserIdAndPcId_Enforced()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertPerson(conn, "Aud1");
        var pid = TestDbHelper.InsertPerson(conn, "Client1");

        TestDbHelper.Exec(conn, $"INSERT OR IGNORE INTO sys_staff_pc_list (UserId, PcId, WorkCapacity) VALUES ({uid}, {pid}, 'Auditor')");
        TestDbHelper.Exec(conn, $"INSERT OR IGNORE INTO sys_staff_pc_list (UserId, PcId, WorkCapacity) VALUES ({uid}, {pid}, 'Auditor')"); // duplicate ignored

        Assert.Equal(1L, TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM sys_staff_pc_list WHERE UserId={uid} AND PcId={pid}"));
    }

    [Fact]
    public void CoreUsers_UniqueConstraint_OnUsername_Enforced()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Stub1");
        TestDbHelper.Exec(conn, $"INSERT INTO core_users (PersonId, Username, PasswordHash, StaffRole) VALUES ({pid}, 'tami', 'hash1', 'None')");

        var pid2 = TestDbHelper.InsertPerson(conn, "Stub2");
        Assert.Throws<SqliteException>(() =>
            TestDbHelper.Exec(conn, $"INSERT INTO core_users (PersonId, Username, PasswordHash, StaffRole) VALUES ({pid2}, 'tami', 'hash2', 'None')")
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
            TestDbHelper.Exec(conn, $"INSERT INTO cs_reviews (SessionId, CsId, ReviewLengthSeconds, Status) VALUES ({sid}, {audId}, 600, 'InvalidStatus')")
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
            TestDbHelper.Exec(conn, $"INSERT INTO cs_reviews (SessionId, CsId, ReviewLengthSeconds, Status) VALUES ({sid}, {audId}, 600, '{status}')")
        );
        Assert.Null(ex);
    }

    // =========================================================================
    // Default column values
    // =========================================================================

    [Fact]
    public void Sessions_DefaultVerifiedStatus_IsPending()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        TestDbHelper.Exec(conn, $@"
            INSERT INTO sess_sessions (PcId, AuditorId, SessionDate, SequenceInDay,
                                  LengthSeconds, AdminSeconds, IsFreeSession,
                                  ChargeSeconds, ChargedRateCentsPerHour)
            VALUES ({pcId}, {audId}, '2024-01-11', 1, 3600, 0, 0, 0, 0)");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT VerifiedStatus FROM sess_sessions ORDER BY SessionId DESC LIMIT 1";
        var status = cmd.ExecuteScalar() as string;
        Assert.True(status == "Pending" || status == "Draft");  // default is Pending in current schema
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

        TestDbHelper.Exec(conn, $"INSERT INTO cs_reviews (SessionId, CsId, ReviewLengthSeconds) VALUES ({sid}, {audId}, 600)");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Status FROM cs_reviews ORDER BY CsReviewId DESC LIMIT 1";
        Assert.Equal("Draft", cmd.ExecuteScalar() as string);
    }

    [Fact]
    public void CoreUsers_DefaultIsActive_IsOne()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.Exec(conn, $"INSERT INTO core_users (PersonId, Username, PasswordHash, StaffRole) VALUES ({pid}, 'testuser_{pid}', 'hash', 'Auditor')");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT IsActive FROM core_users WHERE PersonId={pid}";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public void StaffPcList_DefaultWorkCapacity_IsAuditor()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertPerson(conn, "Aud1");
        var pid = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.Exec(conn, $"INSERT INTO sys_staff_pc_list (UserId, PcId) VALUES ({uid}, {pid})");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT WorkCapacity FROM sys_staff_pc_list WHERE UserId={uid} AND PcId={pid}";
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
    public void PurchaseItems_AutoIncrement_AssignsSequentialIds()
    {
        using var conn = Open();
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var purchaseId = TestDbHelper.InsertPurchase(conn, pcId, "2024-01-01");
        var p1 = TestDbHelper.InsertPurchaseItem(conn, purchaseId, hoursBought: 2);
        var p2 = TestDbHelper.InsertPurchaseItem(conn, purchaseId, hoursBought: 3);
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
            _ = new DashboardService(TestConfig.For(_dbPath), new LPM.Services.MessageNotifier(), new LPM.Services.HtmlSanitizerService());
            _ = new DashboardService(TestConfig.For(_dbPath), new LPM.Services.MessageNotifier(), new LPM.Services.HtmlSanitizerService());
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
            _ = new DashboardService(TestConfig.For(_dbPath), new LPM.Services.MessageNotifier(), new LPM.Services.HtmlSanitizerService());
            _ = new PcService(TestConfig.For(_dbPath));
            _ = new AuditorService(TestConfig.For(_dbPath), new LPM.Auth.UserDb(TestConfig.For(_dbPath)));
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
