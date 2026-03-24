using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Tests for <see cref="DashboardService"/> – the core weekly-dashboard service.
/// Covers user lookup, auditor/CS role checks, session management, CS reviews,
/// week-grid calculations, solo sessions, and static utility methods.
/// Each test gets a fresh isolated SQLite database.
/// </summary>
public class DashboardServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DashboardService _svc;

    public DashboardServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new DashboardService(TestConfig.For(_dbPath), new LPM.Services.MessageNotifier());
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // GetUserIdByUsername — queries core_users.Username
    // =========================================================================

    [Fact]
    public void GetUserIdByUsername_ReturnsPersonId_WhenFound()
    {
        using var conn = Open();
        // InsertUser creates a stub person + core_users row with Username = "Tami"
        var uid = TestDbHelper.InsertUser(conn, "Tami", "pass");
        // InsertUser returns core_users.Id; we need the PersonId
        var personId = (int)TestDbHelper.Scalar(conn, $"SELECT PersonId FROM core_users WHERE Id={uid}");

        Assert.Equal(personId, _svc.GetUserIdByUsername("Tami"));
    }

    [Fact]
    public void GetUserIdByUsername_IsCaseInsensitive()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "Tami", "pass");
        var personId = (int)TestDbHelper.Scalar(conn, $"SELECT PersonId FROM core_users WHERE Id={uid}");

        Assert.Equal(personId, _svc.GetUserIdByUsername("TAMI"));
        Assert.Equal(personId, _svc.GetUserIdByUsername("tami"));
        Assert.Equal(personId, _svc.GetUserIdByUsername("TaMi"));
    }

    [Fact]
    public void GetUserIdByUsername_ReturnsNull_WhenNotFound()
    {
        Assert.Null(_svc.GetUserIdByUsername("nobody"));
    }

    // =========================================================================
    // IsAuditor — checks core_users WHERE StaffRole IN ('Auditor','CS')
    // =========================================================================

    [Fact]
    public void IsAuditor_ReturnsTrue_ForActiveAuditor()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Tami");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Auditor", isActive: true);

        Assert.True(_svc.IsAuditor(id));
    }

    [Fact]
    public void IsAuditor_ReturnsTrue_ForCS()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "CsUser");
        TestDbHelper.InsertCS(conn, id, isActive: true);

        Assert.True(_svc.IsAuditor(id));
    }

    [Fact]
    public void IsAuditor_ReturnsFalse_ForSoloOnly()
    {
        // type:2 → StaffRole='Solo', which is NOT in ('Auditor','CS')
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "SoloWorker");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Solo", isActive: true);

        Assert.False(_svc.IsAuditor(id));
    }

    [Fact]
    public void IsAuditor_ReturnsFalse_ForInactiveAuditor()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Inactive");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Auditor", isActive: false);

        Assert.False(_svc.IsAuditor(id));
    }

    [Fact]
    public void IsAuditor_ReturnsFalse_ForPersonNotInCoreUsers()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "NotAnAuditor");

        Assert.False(_svc.IsAuditor(id));
    }

    // =========================================================================
    // IsSoloAuditor — checks core_users WHERE StaffRole='Solo'
    // =========================================================================

    [Fact]
    public void IsSoloAuditor_ReturnsTrue_ForSoloRole()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "SoloA");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Solo", isActive: true); // type:2 → Solo

        Assert.True(_svc.IsSoloAuditor(id));
    }

    [Fact]
    public void IsSoloAuditor_ReturnsFalse_ForAuditorRole()
    {
        // type:1 → Auditor, type:3 → Auditor — neither is 'Solo'
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "RegA");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Auditor", isActive: true);

        Assert.False(_svc.IsSoloAuditor(id));
    }

    [Fact]
    public void IsSoloAuditor_ReturnsFalse_WhenInactive()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "InactiveSolo");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Solo", isActive: false);

        Assert.False(_svc.IsSoloAuditor(id));
    }

    // =========================================================================
    // IsCS
    // =========================================================================

    [Fact]
    public void IsCS_ReturnsTrue_ForActiveCS()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Samai");
        TestDbHelper.InsertCS(conn, id, isActive: true);

        Assert.True(_svc.IsCS(id));
    }

    [Fact]
    public void IsCS_ReturnsFalse_ForInactiveCS()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "OldCS");
        TestDbHelper.InsertCS(conn, id, isActive: false);

        Assert.False(_svc.IsCS(id));
    }

    [Fact]
    public void IsCS_ReturnsFalse_ForPersonNotInCoreUsers()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "NotACS");

        Assert.False(_svc.IsCS(id));
    }

    // =========================================================================
    // AddSession
    // =========================================================================

    [Fact]
    public void AddSession_ReturnsPositiveSessionId()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid = _svc.AddSession(audId, pcId, new DateOnly(2024, 1, 10), 3600, 0, false, null);
        Assert.True(sid > 0);
    }

    [Fact]
    public void AddSession_IncrementsSequenceInDay_ForSamePcAndDate()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var date = new DateOnly(2024, 1, 15);
        var sid1 = _svc.AddSession(audId, pcId, date, 3600, 0, false, null);
        var sid2 = _svc.AddSession(audId, pcId, date, 1800, 0, false, null);
        var sid3 = _svc.AddSession(audId, pcId, date, 900,  0, false, null);

        var seq1 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM sess_sessions WHERE SessionId={sid1}");
        var seq2 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM sess_sessions WHERE SessionId={sid2}");
        var seq3 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM sess_sessions WHERE SessionId={sid3}");

        Assert.Equal(1L, seq1);
        Assert.Equal(2L, seq2);
        Assert.Equal(3L, seq3);
    }

    [Fact]
    public void AddSession_SequenceRestartsFor_DifferentDate()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid1 = _svc.AddSession(audId, pcId, new DateOnly(2024, 1, 15), 3600, 0, false, null);
        var sid2 = _svc.AddSession(audId, pcId, new DateOnly(2024, 1, 16), 1800, 0, false, null);

        var seq2 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM sess_sessions WHERE SessionId={sid2}");
        Assert.Equal(1L, seq2);
    }

    [Fact]
    public void AddSession_StoresFreeSessionFlag()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid = _svc.AddSession(audId, pcId, new DateOnly(2024, 1, 10), 3600, 0, isFree: true, null);
        var flag = TestDbHelper.Scalar(conn, $"SELECT IsFreeSession FROM sess_sessions WHERE SessionId={sid}");
        Assert.Equal(1L, flag);
    }

    // =========================================================================
    // UpdateSession
    // =========================================================================

    [Fact]
    public void UpdateSession_ChangesLengthAndFreeFlag()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid = _svc.AddSession(audId, pcId, new DateOnly(2024, 1, 10), 3600, 0, false, null);
        _svc.UpdateSession(sid, 7200, 600, true, "<p>Updated</p>");

        var len  = TestDbHelper.Scalar(conn, $"SELECT LengthSeconds  FROM sess_sessions WHERE SessionId={sid}");
        var adm  = TestDbHelper.Scalar(conn, $"SELECT AdminSeconds   FROM sess_sessions WHERE SessionId={sid}");
        var free = TestDbHelper.Scalar(conn, $"SELECT IsFreeSession  FROM sess_sessions WHERE SessionId={sid}");
        Assert.Equal(7200L, len);
        Assert.Equal(600L,  adm);
        Assert.Equal(1L,    free);
    }

    // =========================================================================
    // AddCsReview
    // =========================================================================

    [Fact]
    public void AddCsReview_ReturnsPositiveId()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid   = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600);
        var revId = _svc.AddCsReview(csId, sid, 1200, "Draft", null);

        Assert.True(revId > 0);
    }

    [Fact]
    public void AddCsReview_DuplicateSessionId_ReturnsExistingId()
    {
        // AddCsReview is idempotent: duplicate call returns the existing CsReviewId
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid   = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600);
        var first = _svc.AddCsReview(csId, sid, 1200, "Draft", null);
        var second = _svc.AddCsReview(csId, sid, 600, "Approved", null);

        Assert.Equal(first, second);
    }

    [Fact]
    public void AddCsReview_AllStatuses_AreAccepted()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var statuses = new[] { "Draft", "Approved", "NeedsCorrection", "Rejected" };
        for (int i = 0; i < statuses.Length; i++)
        {
            var sid   = TestDbHelper.InsertSession(conn, pcId, audId, $"2024-01-{10 + i:D2}", 3600, seqInDay: 1);
            var revId = _svc.AddCsReview(csId, sid, 1200, statuses[i], null);
            Assert.True(revId > 0);
        }
    }

    // =========================================================================
    // UpdateCsReview
    // =========================================================================

    [Fact]
    public void UpdateCsReview_ChangesReviewSeconds_And_Status()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid   = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600);
        var revId = _svc.AddCsReview(csId, sid, 1200, "Draft", null);
        // UpdateCsReview signature: (csReviewId, callerCsId, reviewSec, status, notes)
        _svc.UpdateCsReview(revId, csId, 1800, "Approved", "Looks good");

        var rev = TestDbHelper.Scalar(conn, $"SELECT ReviewLengthSeconds FROM cs_reviews WHERE CsReviewId={revId}");
        Assert.Equal(1800L, rev);
    }

    // =========================================================================
    // GetDayDetail
    // =========================================================================

    [Fact]
    public void GetDayDetail_Auditor_ReturnsOwnSessions()
    {
        using var conn = Open();
        var aud1Id = TestDbHelper.InsertPerson(conn, "Aud1");
        var aud2Id = TestDbHelper.InsertPerson(conn, "Aud2");
        TestDbHelper.InsertAuditor(conn, aud1Id);
        TestDbHelper.InsertAuditor(conn, aud2Id);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        TestDbHelper.InsertSession(conn, pcId, aud1Id, "2024-01-10", 3600, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, aud2Id, "2024-01-10", 1800, seqInDay: 2);

        var detail = _svc.GetDayDetail(aud1Id, pcId, new DateOnly(2024, 1, 10), "Auditor");
        Assert.Single(detail.Sessions);
        Assert.Equal(3600, detail.Sessions[0].LengthSec);
    }

    [Fact]
    public void GetDayDetail_CS_ReturnsAllSessions_ForThatPcAndDate()
    {
        using var conn = Open();
        var aud1Id = TestDbHelper.InsertPerson(conn, "Aud1");
        var aud2Id = TestDbHelper.InsertPerson(conn, "Aud2");
        TestDbHelper.InsertAuditor(conn, aud1Id);
        TestDbHelper.InsertAuditor(conn, aud2Id);
        var csId = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        TestDbHelper.InsertSession(conn, pcId, aud1Id, "2024-01-10", 3600, seqInDay: 1);
        TestDbHelper.InsertSession(conn, pcId, aud2Id, "2024-01-10", 1800, seqInDay: 2);

        var detail = _svc.GetDayDetail(csId, pcId, new DateOnly(2024, 1, 10), "CS");
        Assert.Equal(2, detail.Sessions.Count);
    }

    [Fact]
    public void GetDayDetail_CS_ReturnsReviewsForSessions()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600, seqInDay: 1);
        _svc.AddCsReview(csId, sid, 1200, "Approved", "Looks good");

        var detail = _svc.GetDayDetail(csId, pcId, new DateOnly(2024, 1, 10), "CS");
        Assert.Single(detail.Reviews);
        Assert.Equal(1200, detail.Reviews[0].ReviewSec);
        Assert.Equal("Approved", detail.Reviews[0].Status);
    }

    [Fact]
    public void GetDayDetail_Auditor_ReturnsEmpty_WhenNoSessionsForDate()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var detail = _svc.GetDayDetail(audId, pcId, new DateOnly(2024, 6, 1), "Auditor");
        Assert.Empty(detail.Sessions);
    }

    // =========================================================================
    // AddSoloSession — requires StaffRole='Solo' in core_users
    // =========================================================================

    [Fact]
    public void AddSoloSession_CreatesSoloSession_PcIdEqualsAuditorId()
    {
        // Solo sessions are identified by PcId == AuditorId
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        // type:2 → Solo
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo");

        var sid  = _svc.AddSoloSession(audId, new DateOnly(2024, 1, 10), 3600, 0, false, null);
        var pcId = TestDbHelper.Scalar(conn, $"SELECT PcId FROM sess_sessions WHERE SessionId={sid}");
        Assert.Equal((long)audId, pcId);
    }

    [Fact]
    public void AddSoloSession_UsesSamePersonIdForPcIdAndAuditorId()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo");

        var sid  = _svc.AddSoloSession(audId, new DateOnly(2024, 1, 10), 3600, 0, false, null);
        var pcId = TestDbHelper.Scalar(conn, $"SELECT PcId FROM sess_sessions WHERE SessionId={sid}");
        Assert.Equal((long)audId, pcId);
    }

    [Fact]
    public void AddSoloSession_SequenceIncrements_ForSameDate()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo");

        var sid1 = _svc.AddSoloSession(audId, new DateOnly(2024, 1, 10), 3600, 0, false, null);
        var sid2 = _svc.AddSoloSession(audId, new DateOnly(2024, 1, 10), 1800, 0, false, null);

        var seq1 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM sess_sessions WHERE SessionId={sid1}");
        var seq2 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM sess_sessions WHERE SessionId={sid2}");
        Assert.Equal(1L, seq1);
        Assert.Equal(2L, seq2);
    }

    // =========================================================================
    // StaffPcList – AddUserPc / RemoveUserPc / GetUserPcs
    // =========================================================================

    [Fact]
    public void AddUserPc_AddsEntryToStaffPcList()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        _svc.AddUserPc(audId, pcId);

        var count = TestDbHelper.Scalar(conn,
            $"SELECT COUNT(*) FROM sys_staff_pc_list WHERE UserId={audId} AND PcId={pcId}");
        Assert.Equal(1L, count);
    }

    [Fact]
    public void AddUserPc_CSSolo_SetsCapacityToCSSolo()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        _svc.AddUserPc(audId, pcId, "CSSolo");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT WorkCapacity FROM sys_staff_pc_list WHERE UserId={audId} AND PcId={pcId}";
        var cap = cmd.ExecuteScalar() as string;
        Assert.Equal("CSSolo", cap);
    }

    [Fact]
    public void AddUserPc_IsNotSolo_SetsCapacityToAuditor()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        _svc.AddUserPc(audId, pcId);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT WorkCapacity FROM sys_staff_pc_list WHERE UserId={audId} AND PcId={pcId}";
        var cap = cmd.ExecuteScalar() as string;
        Assert.Equal("Auditor", cap);
    }

    [Fact]
    public void AddUserPc_DuplicateIgnored_UNIQUE_Constraint()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        _svc.AddUserPc(audId, pcId);
        _svc.AddUserPc(audId, pcId);  // should be silently ignored (or update)

        var count = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM sys_staff_pc_list WHERE UserId={audId} AND PcId={pcId}");
        Assert.Equal(1L, count);
    }

    [Fact]
    public void RemoveUserPc_RemovesEntryFromStaffPcList()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        _svc.AddUserPc(audId, pcId);
        _svc.RemoveUserPc(audId, pcId);

        var count = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM sys_staff_pc_list WHERE UserId={audId} AND PcId={pcId}");
        Assert.Equal(0L, count);
    }

    [Fact]
    public void GetUserPcs_ReturnsAssignedPcs()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pc1Id = TestDbHelper.InsertPerson(conn, "Client1");
        var pc2Id = TestDbHelper.InsertPerson(conn, "Client2");
        TestDbHelper.InsertPC(conn, pc1Id);
        TestDbHelper.InsertPC(conn, pc2Id);

        _svc.AddUserPc(audId, pc1Id);
        _svc.AddUserPc(audId, pc2Id);

        var pcs = _svc.GetUserPcs(audId);
        Assert.Equal(2, pcs.Count);
    }

    [Fact]
    public void GetUserPcs_ReturnsEmpty_WhenNoAssignments()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");

        Assert.Empty(_svc.GetUserPcs(audId));
    }

    // =========================================================================
    // GetWeekStart (static utility)
    // =========================================================================

    [Theory]
    [InlineData("2024-01-11", "2024-01-11")]  // Thursday → same day
    [InlineData("2024-01-12", "2024-01-11")]  // Friday   → prior Thursday
    [InlineData("2024-01-13", "2024-01-11")]  // Saturday → prior Thursday
    [InlineData("2024-01-14", "2024-01-11")]  // Sunday   → prior Thursday
    [InlineData("2024-01-15", "2024-01-11")]  // Monday   → prior Thursday
    [InlineData("2024-01-16", "2024-01-11")]  // Tuesday  → prior Thursday
    [InlineData("2024-01-17", "2024-01-11")]  // Wednesday→ prior Thursday
    [InlineData("2024-01-18", "2024-01-18")]  // Next Thursday
    public void GetWeekStart_ReturnsCorrectThursday(string dateStr, string expectedStr)
    {
        var d        = DateOnly.Parse(dateStr);
        var expected = DateOnly.Parse(expectedStr);
        Assert.Equal(expected, DashboardService.GetWeekStart(d));
    }

    // =========================================================================
    // Fmt / FmtOrBlank (static utility)
    // =========================================================================

    [Fact]
    public void Fmt_ZeroSeconds_ReturnsDash()
    {
        Assert.Equal("-", DashboardService.Fmt(0));
    }

    [Fact]
    public void Fmt_OneHour_Returns1Colon00()
    {
        Assert.Equal("1:00", DashboardService.Fmt(3600));
    }

    [Fact]
    public void Fmt_OneHourThirtyMinutes()
    {
        Assert.Equal("1:30", DashboardService.Fmt(5400));
    }

    [Fact]
    public void Fmt_45Minutes()
    {
        Assert.Equal("0:45", DashboardService.Fmt(2700));
    }

    [Fact]
    public void FmtOrBlank_ZeroSeconds_ReturnsEmpty()
    {
        Assert.Equal("", DashboardService.FmtOrBlank(0));
    }

    [Fact]
    public void FmtOrBlank_NegativeSeconds_ReturnsEmpty()
    {
        Assert.Equal("", DashboardService.FmtOrBlank(-100));
    }

    [Fact]
    public void FmtOrBlank_TwoHours_Returns2Colon00()
    {
        Assert.Equal("2:00", DashboardService.FmtOrBlank(7200));
    }

    // =========================================================================
    // GetWeekGrid
    // =========================================================================

    [Fact]
    public void GetWeekGrid_ReturnsEmpty_WhenNoPcs()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var week  = new DateOnly(2024, 1, 11);   // Thursday

        var grid = _svc.GetWeekGrid(audId, week, new List<PcInfo>());
        Assert.Empty(grid);
    }

    [Fact]
    public void GetWeekGrid_Auditor_ReturnsSessionSeconds()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11);   // Thursday
        // Session on Thursday (dayIndex 0)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600, adminSec: 600);

        var pcs  = new List<PcInfo> { new PcInfo(pcId, "Client1", "Auditor") };
        var grid = _svc.GetWeekGrid(audId, week, pcs);

        Assert.True(grid.TryGetValue((pcId, 0), out var secs));
        Assert.Equal(3600 + 600, secs);
    }

    [Fact]
    public void GetWeekGrid_CS_ReturnsReviewSeconds()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11);
        var sid  = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600); // Thursday
        _svc.AddCsReview(csId, sid, 1200, "Draft", null);

        var pcs  = new List<PcInfo> { new PcInfo(pcId, "Client1", "CS") };
        var grid = _svc.GetWeekGrid(csId, week, pcs);

        Assert.True(grid.TryGetValue((pcId, 0), out var secs));
        Assert.Equal(1200, secs);
    }

    // =========================================================================
    // AddCsWork
    // =========================================================================

    [Fact]
    public void AddCsWork_ReturnsPositiveId()
    {
        using var conn = Open();
        var csId = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var id = _svc.AddCsWork(csId, pcId, new DateOnly(2024, 1, 10), 1800, "General work");
        Assert.True(id > 0);
    }

    [Fact]
    public void AddCsWork_StoredInCsWorkLogTable()
    {
        using var conn = Open();
        var csId = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var id = _svc.AddCsWork(csId, pcId, new DateOnly(2024, 1, 10), 1800, null);
        var count = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM cs_work_log WHERE CsWorkLogId={id}");
        Assert.Equal(1L, count);
    }

    // =========================================================================
    // GetSoloAuditorInfo — requires StaffRole='Solo' in core_users
    // =========================================================================

    [Fact]
    public void GetSoloAuditorInfo_ReturnsNull_WhenNotFound()
    {
        Assert.Null(_svc.GetSoloAuditorInfo(9999));
    }

    [Fact]
    public void GetSoloAuditorInfo_ReturnsPcInfoWithSoloAuditorRole_ForSoloUser()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Tami", "Cohen");
        TestDbHelper.InsertAuditor(conn, id, staffRole: "Solo", isActive: true); // type:2 → Solo

        var info = _svc.GetSoloAuditorInfo(id);
        Assert.NotNull(info);
        Assert.Equal("SoloAuditor", info!.WorkCapacity);
        Assert.Contains("Tami", info.FullName);
    }

    [Fact]
    public void GetSoloAuditorInfo_ReturnsNull_ForNonSoloUser()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Tami", "Cohen");
        // Only insert a person, no core_users entry with StaffRole='Solo'

        Assert.Null(_svc.GetSoloAuditorInfo(id));
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
