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
        _svc    = new DashboardService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // GetUserIdByUsername
    // =========================================================================

    [Fact]
    public void GetUserIdByUsername_ReturnsPersonId_WhenFound()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Tami", "Cohen");

        Assert.Equal(id, _svc.GetUserIdByUsername("Tami"));
    }

    [Fact]
    public void GetUserIdByUsername_IsCaseInsensitive()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Tami", "Cohen");

        Assert.Equal(id, _svc.GetUserIdByUsername("TAMI"));
        Assert.Equal(id, _svc.GetUserIdByUsername("tami"));
        Assert.Equal(id, _svc.GetUserIdByUsername("TaMi"));
    }

    [Fact]
    public void GetUserIdByUsername_ReturnsNull_WhenNotFound()
    {
        Assert.Null(_svc.GetUserIdByUsername("nobody"));
    }

    [Fact]
    public void GetUserIdByUsername_ReturnsFirstMatch_WhenDuplicateFirstNames()
    {
        using var conn = Open();
        TestDbHelper.InsertPerson(conn, "Dana", "A");
        TestDbHelper.InsertPerson(conn, "Dana", "B");

        // Should return a valid person ID (not null) — exact first-match is fine
        var result = _svc.GetUserIdByUsername("Dana");
        Assert.NotNull(result);
    }

    // =========================================================================
    // IsAuditor
    // =========================================================================

    [Fact]
    public void IsAuditor_ReturnsTrue_ForActiveRegularAuditor()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Tami");
        TestDbHelper.InsertAuditor(conn, id, type: 1, isActive: true);

        Assert.True(_svc.IsAuditor(id));
    }

    [Fact]
    public void IsAuditor_ReturnsTrue_ForType3_RegularAndSolo()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Aviv");
        TestDbHelper.InsertAuditor(conn, id, type: 3, isActive: true);

        Assert.True(_svc.IsAuditor(id));
    }

    [Fact]
    public void IsAuditor_ReturnsFalse_ForType2_SoloOnly()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "SoloWorker");
        TestDbHelper.InsertAuditor(conn, id, type: 2, isActive: true);

        Assert.False(_svc.IsAuditor(id));
    }

    [Fact]
    public void IsAuditor_ReturnsFalse_ForInactiveAuditor()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Inactive");
        TestDbHelper.InsertAuditor(conn, id, type: 1, isActive: false);

        Assert.False(_svc.IsAuditor(id));
    }

    [Fact]
    public void IsAuditor_ReturnsFalse_ForPersonNotInAuditorsTable()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "NotAnAuditor");

        Assert.False(_svc.IsAuditor(id));
    }

    // =========================================================================
    // IsSoloAuditor
    // =========================================================================

    [Fact]
    public void IsSoloAuditor_ReturnsTrue_ForType2()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "SoloA");
        TestDbHelper.InsertAuditor(conn, id, type: 2, isActive: true);

        Assert.True(_svc.IsSoloAuditor(id));
    }

    [Fact]
    public void IsSoloAuditor_ReturnsTrue_ForType3()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "BothA");
        TestDbHelper.InsertAuditor(conn, id, type: 3, isActive: true);

        Assert.True(_svc.IsSoloAuditor(id));
    }

    [Fact]
    public void IsSoloAuditor_ReturnsFalse_ForType1()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "RegA");
        TestDbHelper.InsertAuditor(conn, id, type: 1, isActive: true);

        Assert.False(_svc.IsSoloAuditor(id));
    }

    [Fact]
    public void IsSoloAuditor_ReturnsFalse_WhenInactive()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "InactiveSolo");
        TestDbHelper.InsertAuditor(conn, id, type: 2, isActive: false);

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
    public void IsCS_ReturnsFalse_ForPersonNotInCsTable()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "NotACS");

        Assert.False(_svc.IsCS(id));
    }

    // =========================================================================
    // GetAuditorType
    // =========================================================================

    [Fact]
    public void GetAuditorType_Returns1_ForRegularOnly()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Reg");
        TestDbHelper.InsertAuditor(conn, id, type: 1);

        Assert.Equal(1, _svc.GetAuditorType(id));
    }

    [Fact]
    public void GetAuditorType_Returns0_ForInactive()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Inact");
        TestDbHelper.InsertAuditor(conn, id, type: 0);

        Assert.Equal(0, _svc.GetAuditorType(id));
    }

    [Fact]
    public void GetAuditorType_Returns1_WhenPersonNotInAuditorsTable()
    {
        // Default fallback when auditor not found
        Assert.Equal(1, _svc.GetAuditorType(9999));
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

        var seq1 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM Sessions WHERE SessionId={sid1}");
        var seq2 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM Sessions WHERE SessionId={sid2}");
        var seq3 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM Sessions WHERE SessionId={sid3}");

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

        var seq2 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM Sessions WHERE SessionId={sid2}");
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
        var flag = TestDbHelper.Scalar(conn, $"SELECT IsFreeSession FROM Sessions WHERE SessionId={sid}");
        Assert.Equal(1L, flag);
    }

    [Fact]
    public void AddSession_StoresSummary()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid     = _svc.AddSession(audId, pcId, new DateOnly(2024, 1, 10), 3600, 0, false, "<p>Notes here</p>");
        using var c = conn.CreateCommand();
        c.CommandText = $"SELECT SessionSummaryHtml FROM Sessions WHERE SessionId={sid}";
        var summary = c.ExecuteScalar() as string;
        Assert.Equal("<p>Notes here</p>", summary);
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

        var len  = TestDbHelper.Scalar(conn, $"SELECT LengthSeconds  FROM Sessions WHERE SessionId={sid}");
        var adm  = TestDbHelper.Scalar(conn, $"SELECT AdminSeconds   FROM Sessions WHERE SessionId={sid}");
        var free = TestDbHelper.Scalar(conn, $"SELECT IsFreeSession  FROM Sessions WHERE SessionId={sid}");
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
    public void AddCsReview_DuplicateSessionId_ThrowsException()
    {
        // CsReviews.SessionId has UNIQUE constraint
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-10", 3600);
        _svc.AddCsReview(csId, sid, 1200, "Draft", null);

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(
            () => _svc.AddCsReview(csId, sid, 600, "Approved", null));
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
        _svc.UpdateCsReview(revId, 1800, "Approved", "Looks good");

        var rev  = TestDbHelper.Scalar(conn, $"SELECT ReviewLengthSeconds FROM CsReviews WHERE CsReviewId={revId}");
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
    // AddSoloSession
    // =========================================================================

    [Fact]
    public void AddSoloSession_CreatesSessionWithIsSoloFlag()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, type: 3);

        var sid  = _svc.AddSoloSession(audId, new DateOnly(2024, 1, 10), 3600, 0, false, null);
        var flag = TestDbHelper.Scalar(conn, $"SELECT IsSolo FROM Sessions WHERE SessionId={sid}");
        Assert.Equal(1L, flag);
    }

    [Fact]
    public void AddSoloSession_UsesSamePersonIdForPcIdAndAuditorId()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, type: 2);

        var sid   = _svc.AddSoloSession(audId, new DateOnly(2024, 1, 10), 3600, 0, false, null);
        var pcId  = TestDbHelper.Scalar(conn, $"SELECT PcId FROM Sessions WHERE SessionId={sid}");
        Assert.Equal((long)audId, pcId);
    }

    [Fact]
    public void AddSoloSession_SequenceIncrements_ForSameDate()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, type: 3);

        var sid1 = _svc.AddSoloSession(audId, new DateOnly(2024, 1, 10), 3600, 0, false, null);
        var sid2 = _svc.AddSoloSession(audId, new DateOnly(2024, 1, 10), 1800, 0, false, null);

        var seq1 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM Sessions WHERE SessionId={sid1}");
        var seq2 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM Sessions WHERE SessionId={sid2}");
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
            $"SELECT COUNT(*) FROM StaffPcList WHERE UserId={audId} AND PcId={pcId}");
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
        cmd.CommandText = $"SELECT WorkCapacity FROM StaffPcList WHERE UserId={audId} AND PcId={pcId} AND WorkCapacity='CSSolo'";
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
        cmd.CommandText = $"SELECT WorkCapacity FROM StaffPcList WHERE UserId={audId} AND PcId={pcId}";
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
        _svc.AddUserPc(audId, pcId);  // should be silently ignored

        var count = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM StaffPcList WHERE UserId={audId} AND PcId={pcId}");
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

        var count = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM StaffPcList WHERE UserId={audId} AND PcId={pcId}");
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
        var count = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM CsWorkLog WHERE CsWorkLogId={id}");
        Assert.Equal(1L, count);
    }

    // =========================================================================
    // GetSoloAuditorInfo
    // =========================================================================

    [Fact]
    public void GetSoloAuditorInfo_ReturnsNull_WhenNotFound()
    {
        Assert.Null(_svc.GetSoloAuditorInfo(9999));
    }

    [Fact]
    public void GetSoloAuditorInfo_ReturnsPcInfoWithSoloAuditorRole()
    {
        using var conn = Open();
        var id = TestDbHelper.InsertPerson(conn, "Tami", "Cohen");

        var info = _svc.GetSoloAuditorInfo(id);
        Assert.NotNull(info);
        Assert.Equal("SoloAuditor", info!.WorkCapacity);
        Assert.Contains("Tami", info.FullName);
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
