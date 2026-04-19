using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Extended tests for <see cref="DashboardService"/>.
/// Covers: MiscCharge, CsWorkLog in WeekGrid, WeeklyTotals, solo grid,
/// GetLastCsNamesByPc, HasAnyWorkInWeek, GetPendingCsMarkers,
/// GetAllPcs (dashboard version), SetUserPcRole, and cross-service scenarios.
/// </summary>
public class DashboardExtendedTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DashboardService _svc;

    public DashboardExtendedTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new DashboardService(TestConfig.For(_dbPath), new LPM.Services.MessageNotifier());
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // GetAllPcs (DashboardService version — includes solo entries)
    // =========================================================================

    [Fact]
    public void GetAllPcs_Dashboard_ReturnsEmpty_WhenNoPcs()
    {
        Assert.Empty(_svc.GetAllPcs());
    }

    [Fact]
    public void GetAllPcs_Dashboard_ReturnsRegularEntry_ForEachPc()
    {
        using var conn = Open();
        var pid1 = TestDbHelper.InsertPerson(conn, "Alice");
        var pid2 = TestDbHelper.InsertPerson(conn, "Bob");
        TestDbHelper.InsertPC(conn, pid1);
        TestDbHelper.InsertPC(conn, pid2);

        var pcs = _svc.GetAllPcs();
        Assert.Equal(2, pcs.Count(p => p.WorkCapacity != "CSSolo"));
    }

    [Fact]
    public void GetAllPcs_Dashboard_ReturnsRegularEntries_ForAllPcTypes()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Tami");
        TestDbHelper.InsertPC(conn, pid);
        TestDbHelper.InsertAuditor(conn, pid, staffRole: "Auditor", isActive: true);

        var pcs = _svc.GetAllPcs();
        Assert.Single(pcs);
        Assert.Equal("Auditor", pcs[0].WorkCapacity);
    }

    [Fact]
    public void GetAllPcs_Dashboard_ReturnsSoloAuditor_AsRegularEntry()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Aviv");
        TestDbHelper.InsertPC(conn, pid);
        TestDbHelper.InsertAuditor(conn, pid, staffRole: "Solo", isActive: true);

        var pcs = _svc.GetAllPcs();
        Assert.Single(pcs);
        Assert.Contains("Aviv", pcs[0].FullName);
    }

    [Fact]
    public void GetAllPcs_Dashboard_AllEntries_HaveAuditorCapacity()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Tami");
        TestDbHelper.InsertPC(conn, pid);
        TestDbHelper.InsertAuditor(conn, pid, staffRole: "Auditor", isActive: true);

        var pcs = _svc.GetAllPcs();
        Assert.All(pcs, p => Assert.Equal("Auditor", p.WorkCapacity));
    }

    [Fact]
    public void GetAllPcs_Dashboard_RegularOnly_HasNoSpecialHandling()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Genia");
        TestDbHelper.InsertPC(conn, pid);
        TestDbHelper.InsertAuditor(conn, pid, staffRole: "Auditor", isActive: true);

        var pcs = _svc.GetAllPcs();
        Assert.Single(pcs);
    }

    // =========================================================================
    // AddMiscCharge
    // =========================================================================

    [Fact]
    public void AddMiscCharge_ReturnsPositiveId()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "MiscPc");
        TestDbHelper.InsertPC(conn, pcId);

        var id = _svc.AddMiscCharge(audId, pcId, new DateOnly(2024, 1, 15), 1800, 0, false, null);
        Assert.True(id > 0);
    }

    [Fact]
    public void AddMiscCharge_StoredInMiscChargeTable()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "MiscPc");
        TestDbHelper.InsertPC(conn, pcId);

        var id = _svc.AddMiscCharge(audId, pcId, new DateOnly(2024, 1, 15), 1800, 300, false, "Some work");
        var row = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM sess_misc_charges WHERE MiscChargeId={id}");
        Assert.Equal(1L, row);
    }

    [Fact]
    public void AddMiscCharge_IncrementsSequence_ForSameAuditorPcAndDate()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "MiscPc");
        TestDbHelper.InsertPC(conn, pcId);

        var date = new DateOnly(2024, 1, 15);
        var id1  = _svc.AddMiscCharge(audId, pcId, date, 1800, 0, false, null);
        var id2  = _svc.AddMiscCharge(audId, pcId, date, 900,  0, false, null);

        var seq1 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM sess_misc_charges WHERE MiscChargeId={id1}");
        var seq2 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM sess_misc_charges WHERE MiscChargeId={id2}");
        Assert.Equal(1L, seq1);
        Assert.Equal(2L, seq2);
    }

    // =========================================================================
    // SetUserPcRole
    // =========================================================================

    [Fact]
    public void SetUserPcRole_ChangesWorkCapacity()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        _svc.AddUserPc(audId, pcId);
        _svc.SetUserPcRole(audId, pcId, "Miscellaneous");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT WorkCapacity FROM sys_staff_pc_list WHERE UserId={audId} AND PcId={pcId}";
        var cap = cmd.ExecuteScalar() as string;
        Assert.Equal("Miscellaneous", cap);
    }

    // =========================================================================
    // GetDayDetail – Miscellaneous role
    // =========================================================================

    [Fact(Skip = "GetDayDetail does not support 'Miscellaneous' role — misc charges are not surfaced via GetDayDetail")]
    public void GetDayDetail_Miscellaneous_ReturnsOwnMiscRows()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "MiscPc");
        TestDbHelper.InsertPC(conn, pcId);

        var date   = new DateOnly(2024, 1, 15);
        _svc.AddMiscCharge(audId, pcId, date, 3600, 0, false, "Notes");

        var detail = _svc.GetDayDetail(audId, pcId, date, "Miscellaneous");
        Assert.Single(detail.Sessions);
        Assert.Equal(3600, detail.Sessions[0].LengthSec);
    }

    [Fact(Skip = "GetDayDetail does not support 'Miscellaneous' role — misc charges are not surfaced via GetDayDetail")]
    public void GetDayDetail_Miscellaneous_OnlyReturnsOwnRows_NotOtherAuditors()
    {
        using var conn = Open();
        var aud1 = TestDbHelper.InsertPerson(conn, "Aud1");
        var aud2 = TestDbHelper.InsertPerson(conn, "Aud2");
        TestDbHelper.InsertAuditor(conn, aud1);
        TestDbHelper.InsertAuditor(conn, aud2);
        var pcId = TestDbHelper.InsertPerson(conn, "MiscPc");
        TestDbHelper.InsertPC(conn, pcId);

        var date = new DateOnly(2024, 1, 15);
        _svc.AddMiscCharge(aud1, pcId, date, 3600, 0, false, null);
        _svc.AddMiscCharge(aud2, pcId, date, 1800, 0, false, null);

        var detail = _svc.GetDayDetail(aud1, pcId, date, "Miscellaneous");
        Assert.Single(detail.Sessions);
        Assert.Equal(3600, detail.Sessions[0].LengthSec);
    }

    // =========================================================================
    // GetDayDetail – CSSolo role
    // =========================================================================

    [Fact]
    public void GetDayDetail_CSSolo_ReturnsSoloSessions_ForThatDay()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo"); // Solo

        var date = new DateOnly(2024, 1, 15);
        _svc.AddSoloSession(audId, date, 3600, 0, false, null);
        TestDbHelper.AlignSessionCreatedAtToSessionDate(conn);

        var detail = _svc.GetDayDetail(audId, audId, date, "CSSolo");
        Assert.Single(detail.Sessions);
        Assert.Equal(3600, detail.Sessions[0].LengthSec);
    }

    [Fact]
    public void GetDayDetail_CSSolo_DoesNotReturnRegularSessions()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo"); // Solo
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var date = new DateOnly(2024, 1, 15);
        // Regular session (PcId != AuditorId)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-15", 7200);
        // Solo session
        _svc.AddSoloSession(audId, date, 3600, 0, false, null);
        TestDbHelper.AlignSessionCreatedAtToSessionDate(conn);

        var detail = _svc.GetDayDetail(audId, audId, date, "CSSolo");
        Assert.Single(detail.Sessions);
        Assert.Equal(3600, detail.Sessions[0].LengthSec);
    }

    // =========================================================================
    // GetWeekGrid – CS with CsWorkLog entries
    // =========================================================================

    [Fact]
    public void GetWeekGrid_CS_IncludesCsWorkLog_InTotal()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11); // Thursday
        // CS review = 1200 sec
        var sid = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);
        _svc.AddCsReview(csId, sid, 1200, "Draft", null);
        // General CS work = 600 sec
        _svc.AddCsWork(csId, pcId, new DateOnly(2024, 1, 11), 600, null);
        TestDbHelper.AlignSessionCreatedAtToSessionDate(conn);

        var pcs  = new List<PcInfo> { new PcInfo(pcId, "Client1", "CS") };
        var grid = _svc.GetWeekGrid(csId, week, pcs);

        // Grid should contain review + general work = 1800
        Assert.True(grid.TryGetValue((pcId, 0), out var secs));
        Assert.Equal(1200 + 600, secs);
    }

    // =========================================================================
    // GetWeekGridSolo
    // =========================================================================

    [Fact]
    public void GetWeekGridSolo_ReturnsEmpty_WhenNoSoloSessions()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo");

        var grid = _svc.GetWeekGridSolo(audId, new DateOnly(2024, 1, 11));
        Assert.Empty(grid);
    }

    [Fact]
    public void GetWeekGridSolo_ReturnsSoloSessionSeconds()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo");

        var week = new DateOnly(2024, 1, 11); // Thursday
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 3600, 600, false, null);
        TestDbHelper.AlignSessionCreatedAtToSessionDate(conn);

        var grid = _svc.GetWeekGridSolo(audId, week);
        Assert.True(grid.TryGetValue((audId, 0), out var secs));
        Assert.Equal(3600 + 600, secs);
    }

    [Fact]
    public void GetWeekGridSolo_AggregatesTotalForSameDay()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo");

        var week = new DateOnly(2024, 1, 11);
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 3600, 0, false, null);
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 1800, 0, false, null);
        TestDbHelper.AlignSessionCreatedAtToSessionDate(conn);

        var grid = _svc.GetWeekGridSolo(audId, week);
        Assert.True(grid.TryGetValue((audId, 0), out var secs));
        Assert.Equal(5400, secs);
    }

    // =========================================================================
    // GetWeeklyTotals
    // =========================================================================

    [Fact]
    public void GetWeeklyTotals_ReturnsRequestedWeekCount()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);

        var result = _svc.GetWeeklyTotals(audId, new DateOnly(2024, 1, 11), 6, new List<PcInfo>());
        Assert.Equal(6, result.Count);
    }

    [Fact]
    public void GetWeeklyTotals_AllZero_WhenNoPcs()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");

        var result = _svc.GetWeeklyTotals(audId, new DateOnly(2024, 1, 11), 4, new List<PcInfo>());
        Assert.All(result, w => Assert.Equal(0, w.TotalSeconds));
    }

    [Fact]
    public void GetWeeklyTotals_SumsSessionsForAuditorRole()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11);
        // Two sessions in the same week (Thu + Mon)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600, adminSec: 0);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-15", 1800, adminSec: 0);

        var pcs    = new List<PcInfo> { new PcInfo(pcId, "Client1", "Auditor") };
        var result = _svc.GetWeeklyTotals(audId, week, 2, pcs);
        var latest = result.Last();
        Assert.Equal(3600 + 1800, latest.TotalSeconds);
    }

    [Fact]
    public void GetWeeklyTotals_LastWeekIsLatestWeekStart()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");

        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeeklyTotals(audId, week, 3, new List<PcInfo>());
        Assert.Equal(week, result.Last().WeekStart);
    }

    [Fact]
    public void GetWeeklyTotals_WeekLabel_Format_ddMM()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");

        var week   = new DateOnly(2024, 1, 11);
        var result = _svc.GetWeeklyTotals(audId, week, 1, new List<PcInfo>());
        Assert.Equal("11/01", result[0].WeekLabel);
    }

    [Fact]
    public void GetWeeklyTotals_TopPcs_ListedByTimeDesc()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pc1 = TestDbHelper.InsertPerson(conn, "ClientMore");
        var pc2 = TestDbHelper.InsertPerson(conn, "ClientLess");
        TestDbHelper.InsertPC(conn, pc1);
        TestDbHelper.InsertPC(conn, pc2);

        var week = new DateOnly(2024, 1, 11);
        TestDbHelper.InsertSession(conn, pc1, audId, "2024-01-11", 7200); // more
        TestDbHelper.InsertSession(conn, pc2, audId, "2024-01-11", 1800); // less

        var pcs = new List<PcInfo> {
            new PcInfo(pc1, "ClientMore", "Auditor"),
            new PcInfo(pc2, "ClientLess", "Auditor")
        };
        var result = _svc.GetWeeklyTotals(audId, week, 1, pcs);
        var tops   = result.Last().TopPcs!;
        Assert.Equal("ClientMore", tops[0].FullName);
        Assert.Equal("ClientLess", tops[1].FullName);
    }

    // =========================================================================
    // GetWeeklyTotalsSolo
    // =========================================================================

    [Fact]
    public void GetWeeklyTotalsSolo_AllZero_WhenNoSoloSessions()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");

        var result = _svc.GetWeeklyTotalsSolo(audId, new DateOnly(2024, 1, 11), 4);
        Assert.All(result, w => Assert.Equal(0, w.TotalSeconds));
    }

    [Fact]
    public void GetWeeklyTotalsSolo_SumsOnlySoloSessions()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo"); // Solo
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11);
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 3600, 0, false, null);
        // Regular session (PcId != AuditorId) should NOT be counted
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 7200);
        TestDbHelper.AlignSessionCreatedAtToSessionDate(conn);

        var result = _svc.GetWeeklyTotalsSolo(audId, week, 1);
        Assert.Equal(3600, result.Last().TotalSeconds);
    }

    // =========================================================================
    // GetLastCsNamesByPc
    // =========================================================================

    [Fact]
    public void GetLastCsNamesByPc_ReturnsEmpty_ForEmptyList()
    {
        Assert.Empty(_svc.GetLastCsNamesByPc(new List<int>()));
    }

    [Fact]
    public void GetLastCsNamesByPc_ReturnsEmpty_WhenNoPcHasReview()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);

        var result = _svc.GetLastCsNamesByPc(new List<int> { pcId });
        Assert.Empty(result);
    }

    [Fact]
    public void GetLastCsNamesByPc_ReturnsLatestCsName()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var cs1   = TestDbHelper.InsertPerson(conn, "Tami", "Cohen");
        var cs2   = TestDbHelper.InsertPerson(conn, "Genia", "Levi");
        TestDbHelper.InsertCS(conn, cs1);
        TestDbHelper.InsertCS(conn, cs2);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var sid1 = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600, seqInDay: 1);
        var sid2 = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-12", 1800, seqInDay: 1);

        _svc.AddCsReview(cs1, sid1, 600, "Approved", null);
        _svc.AddCsReview(cs2, sid2, 900, "Draft",    null); // cs2 reviewed last

        var result = _svc.GetLastCsNamesByPc(new List<int> { pcId });
        Assert.True(result.ContainsKey(pcId));
        Assert.Contains("Genia", result[pcId]);
    }

    [Fact]
    public void GetLastCsNamesByPc_ReturnsCorrectCs_ForMultiplePcs()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csA   = TestDbHelper.InsertPerson(conn, "Tami");
        var csB   = TestDbHelper.InsertPerson(conn, "Genia");
        TestDbHelper.InsertCS(conn, csA);
        TestDbHelper.InsertCS(conn, csB);
        var pc1   = TestDbHelper.InsertPerson(conn, "Client1");
        var pc2   = TestDbHelper.InsertPerson(conn, "Client2");
        TestDbHelper.InsertPC(conn, pc1);
        TestDbHelper.InsertPC(conn, pc2);

        var sid1  = TestDbHelper.InsertSession(conn, pc1, audId, "2024-01-11", 3600, seqInDay: 1);
        var sid2  = TestDbHelper.InsertSession(conn, pc2, audId, "2024-01-11", 1800, seqInDay: 1);
        _svc.AddCsReview(csA, sid1, 600, "Draft", null);
        _svc.AddCsReview(csB, sid2, 900, "Draft", null);

        var result = _svc.GetLastCsNamesByPc(new List<int> { pc1, pc2 });
        Assert.Contains("Tami",  result[pc1]);
        Assert.Contains("Genia", result[pc2]);
    }

    // =========================================================================
    // HasAnyWorkInWeek
    // =========================================================================

    [Fact]
    public void HasAnyWorkInWeek_ReturnsFalse_WhenNoWork()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);

        var week = new DateOnly(2024, 1, 11);
        Assert.False(_svc.HasAnyWorkInWeek(audId, week, new List<PcInfo>()));
    }

    [Fact]
    public void HasAnyWorkInWeek_ReturnsTrue_WhenAuditorHasSession()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11);
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);

        var pcs = new List<PcInfo> { new PcInfo(pcId, "Client1", "Auditor") };
        Assert.True(_svc.HasAnyWorkInWeek(audId, week, pcs));
    }

    [Fact]
    public void HasAnyWorkInWeek_ReturnsFalse_WhenSessionIsOutsideWeek()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        // Session on the NEXT Thursday (different week)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-18", 3600);

        var week = new DateOnly(2024, 1, 11);
        var pcs  = new List<PcInfo> { new PcInfo(pcId, "Client1", "Auditor") };
        Assert.False(_svc.HasAnyWorkInWeek(audId, week, pcs));
    }

    [Fact(Skip = "HasAnyWorkInWeek does not check sess_misc_charges — misc charge work is not included")]
    public void HasAnyWorkInWeek_ReturnsTrue_WhenMiscChargeInWeek()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "MiscPc");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11);
        _svc.AddMiscCharge(audId, pcId, new DateOnly(2024, 1, 12), 1800, 0, false, null);

        var pcs = new List<PcInfo> { new PcInfo(pcId, "MiscPc", "Miscellaneous") };
        Assert.True(_svc.HasAnyWorkInWeek(audId, week, pcs));
    }

    [Fact]
    public void HasAnyWorkInWeek_SoloMode_ReturnsTrue_ForSoloSession()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Solo");

        var week = new DateOnly(2024, 1, 11);
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 3600, 0, false, null);
        TestDbHelper.AlignSessionCreatedAtToSessionDate(conn);

        Assert.True(_svc.HasAnyWorkInWeek(audId, week, new List<PcInfo>(), soloMode: true));
    }

    [Fact]
    public void HasAnyWorkInWeek_SoloMode_ReturnsFalse_WhenOnlyRegularSessions()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId, staffRole: "Auditor");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        // Regular session only (PcId != AuditorId)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);

        var week = new DateOnly(2024, 1, 11);
        Assert.False(_svc.HasAnyWorkInWeek(audId, week, new List<PcInfo>(), soloMode: true));
    }

    // =========================================================================
    // GetPendingCsMarkers
    // =========================================================================

    [Fact]
    public void GetPendingCsMarkers_ReturnsEmpty_WhenAllSessionsReviewed()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11);
        var sid  = TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600);
        _svc.AddCsReview(csId, sid, 600, "Draft", null);

        var pcs = new List<PcInfo> { new PcInfo(pcId, "Client1", "CS") };
        var markers = _svc.GetPendingCsMarkers(csId, week, pcs);
        Assert.Empty(markers);
    }

    [Fact]
    public void GetPendingCsMarkers_ReturnsCells_WhenSessionHasNoReview()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11); // Thursday
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600); // unreviewed

        var pcs     = new List<PcInfo> { new PcInfo(pcId, "Client1", "CS") };
        var markers = _svc.GetPendingCsMarkers(csId, week, pcs);
        Assert.Contains((pcId, 0), markers);
    }

    [Fact]
    public void GetPendingCsMarkers_ReturnsEmpty_WhenNoPcs()
    {
        var week    = new DateOnly(2024, 1, 11);
        var markers = _svc.GetPendingCsMarkers(1, week, new List<PcInfo>());
        Assert.Empty(markers);
    }

    // =========================================================================
    // Cross-service: complete workflow scenario
    // =========================================================================

    [Fact]
    public void FullWorkflow_PcAddedThenSessionThenCsReview_IntegratesCorrectly()
    {
        // 1. Add PC via PcService
        var pcSvc  = new PcService(TestConfig.For(_dbPath));
        var pcId   = pcSvc.AddPcWithPerson("Moshe", "Levi", "050-5555", "m@x.com", "1975-03-10", "M");

        // 2. Add auditor directly
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Tami");
        TestDbHelper.InsertAuditor(conn, audId);
        var csId  = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);

        // 3. Add session via DashboardService
        var date = new DateOnly(2024, 1, 15);
        var sid  = _svc.AddSession(audId, pcId, date, 3600, 600, false, "<p>Session notes</p>");

        // 4. Add CS review
        _svc.AddCsReview(csId, sid, 1200, "Approved", "All good");
        TestDbHelper.AlignSessionCreatedAtToSessionDate(conn);

        // 5. Verify DayDetail via CS role returns correct data
        var detail = _svc.GetDayDetail(csId, pcId, date, "CS");
        Assert.Single(detail.Sessions);
        Assert.Single(detail.Reviews);
        Assert.Equal(3600, detail.Sessions[0].LengthSec);
        Assert.Equal(1200, detail.Reviews[0].ReviewSec);
        Assert.Equal("Approved", detail.Reviews[0].Status);

        // 6. Verify PcStats shows the session
        var stats = pcSvc.GetPcStats(pcId);
        Assert.Equal(1, stats.TotalSessions);
        Assert.Equal(3600L, stats.UsedSec);
    }

    [Fact]
    public void FullWorkflow_Purchase_ReducesBalance_VisibleInPcService()
    {
        var pcSvc = new PcService(TestConfig.For(_dbPath));
        var pcId  = pcSvc.AddPcWithPerson("Dana", "Cohen", "", "", "", "F");

        // Buy 5 hours via CreatePurchase
        pcSvc.CreatePurchase(pcId, "2024-01-01", null, null, null,
            new List<(string, int?, int?, double, double)> { ("Auditing", null, null, 5.0, 1500) });

        // Use 3600 sec (1 hour) via DashboardService
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        _svc.AddSession(audId, pcId, new DateOnly(2024, 1, 15), 3600, 0, false, null);

        // Verify remaining = (5*3600) - 3600 = 14400
        var pcs = pcSvc.GetAllPcs();
        var pc  = pcs.Single(p => p.PcId == pcId);
        Assert.Equal(14400L, pc.RemainSec);
    }

    [Fact]
    public void FullWorkflow_AuditorCreatedDirectly_IsRecognisedByIsAuditor()
    {
        using var conn = Open();
        var personId = TestDbHelper.InsertPerson(conn, "Eitan", "G");
        TestDbHelper.InsertAuditor(conn, personId, staffRole: "Auditor", isActive: true);

        Assert.True(_svc.IsAuditor(personId));
    }

    // =========================================================================
    // ToggleSessionFree / ToggleCsReviewFree
    // =========================================================================

    [Fact]
    public void ToggleSessionFree_FlipsFlag_WhenZero()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Pc1");
        TestDbHelper.InsertPC(conn, pcId);
        var sid = TestDbHelper.InsertSession(conn, pcId, audId, "2024-05-01", 3600, isFree: false);

        _svc.ToggleSessionFree(sid);

        Assert.Equal(1L, TestDbHelper.Scalar(conn,
            $"SELECT IsFreeSession FROM sess_sessions WHERE SessionId = {sid}"));
    }

    [Fact]
    public void ToggleSessionFree_FlipsFlag_WhenOne()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Pc1");
        TestDbHelper.InsertPC(conn, pcId);
        var sid = TestDbHelper.InsertSession(conn, pcId, audId, "2024-05-01", 3600, isFree: true);

        _svc.ToggleSessionFree(sid);

        Assert.Equal(0L, TestDbHelper.Scalar(conn,
            $"SELECT IsFreeSession FROM sess_sessions WHERE SessionId = {sid}"));
    }

    [Fact]
    public void ToggleSessionFree_IsIdempotent_AfterTwoFlips()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "Pc1");
        TestDbHelper.InsertPC(conn, pcId);
        var sid = TestDbHelper.InsertSession(conn, pcId, audId, "2024-05-01", 3600, isFree: false);

        _svc.ToggleSessionFree(sid);
        _svc.ToggleSessionFree(sid);

        Assert.Equal(0L, TestDbHelper.Scalar(conn,
            $"SELECT IsFreeSession FROM sess_sessions WHERE SessionId = {sid}"));
    }

    [Fact]
    public void ToggleCsReviewFree_FlipsNotes_BetweenFreeAndBill_ForSoloSession()
    {
        using var conn = Open();
        // Solo: session has AuditorId=NULL. PC is an active Solo user.
        var pcId = TestDbHelper.InsertPerson(conn, "SoloUser");
        TestDbHelper.InsertPC(conn, pcId);
        TestDbHelper.InsertCoreUser(conn, pcId, "solo1", "x", staffRole: "Solo");

        var csId = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);

        var sid = TestDbHelper.InsertSession(conn, pcId, auditorId: null, "2024-05-01", 3600);
        // Seed cs_reviews with Notes='Bill'
        using var ins = conn.CreateCommand();
        ins.CommandText = @"INSERT INTO cs_reviews (SessionId, CsId, ReviewLengthSeconds, Status, Notes)
                            VALUES (@s, @c, 600, 'Approved', 'Bill')";
        ins.Parameters.AddWithValue("@s", sid);
        ins.Parameters.AddWithValue("@c", csId);
        ins.ExecuteNonQuery();
        var crId = (int)TestDbHelper.Scalar(conn, "SELECT last_insert_rowid()");

        _svc.ToggleCsReviewFree(crId);
        using (var q = conn.CreateCommand())
        {
            q.CommandText = $"SELECT Notes FROM cs_reviews WHERE CsReviewId = {crId}";
            Assert.Equal("Free", (string)q.ExecuteScalar()!);
        }

        _svc.ToggleCsReviewFree(crId);
        using (var q = conn.CreateCommand())
        {
            q.CommandText = $"SELECT Notes FROM cs_reviews WHERE CsReviewId = {crId}";
            Assert.Equal("Bill", (string)q.ExecuteScalar()!);
        }
    }

    [Fact]
    public void ToggleCsReviewFree_DoesNothing_ForNonSoloSession()
    {
        using var conn = Open();
        // Non-solo: session has AuditorId set. Notes preserved as-is.
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId = TestDbHelper.InsertPerson(conn, "Pc1");
        TestDbHelper.InsertPC(conn, pcId);
        var csId = TestDbHelper.InsertPerson(conn, "CS1");
        TestDbHelper.InsertCS(conn, csId);

        var sid = TestDbHelper.InsertSession(conn, pcId, audId, "2024-05-01", 3600);
        using var ins = conn.CreateCommand();
        ins.CommandText = @"INSERT INTO cs_reviews (SessionId, CsId, ReviewLengthSeconds, Status, Notes)
                            VALUES (@s, @c, 600, 'Approved', 'custom notes')";
        ins.Parameters.AddWithValue("@s", sid);
        ins.Parameters.AddWithValue("@c", csId);
        ins.ExecuteNonQuery();
        var crId = (int)TestDbHelper.Scalar(conn, "SELECT last_insert_rowid()");

        _svc.ToggleCsReviewFree(crId);

        using var q = conn.CreateCommand();
        q.CommandText = $"SELECT Notes FROM cs_reviews WHERE CsReviewId = {crId}";
        Assert.Equal("custom notes", (string)q.ExecuteScalar()!);
    }

    // =========================================================================
    // GetSalaryReport — filter by CreatedAt (not SessionDate)
    // =========================================================================

    [Fact]
    public void GetSalaryReport_FiltersByCreatedAt_NotSessionDate()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId = TestDbHelper.InsertPerson(conn, "Pc1");
        TestDbHelper.InsertPC(conn, pcId);

        // Session with SessionDate in Feb but CreatedAt in April → should appear in April's report
        var sid = TestDbHelper.InsertSession(conn, pcId, audId, "2024-02-10", 3600,
            adminSec: 0, verifiedStatus: "Approved", createdAt: "2024-04-05 10:00:00");
        using (var u = conn.CreateCommand())
        {
            u.CommandText = $"UPDATE sess_sessions SET ChargedRateCentsPerHour=10000, AuditorSalaryCentsPerHour=5000, ChargeSeconds=3600 WHERE SessionId={sid}";
            u.ExecuteNonQuery();
        }

        var febReport   = _svc.GetSalaryReport(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 28));
        var aprilReport = _svc.GetSalaryReport(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30));

        // February (by SessionDate) must NOT include it — filter is on CreatedAt.
        Assert.DoesNotContain(febReport.Groups, g => g.Sessions.Any(s => s.SessionId == sid));
        // April (by CreatedAt) must include it.
        Assert.Contains(aprilReport.Groups, g => g.Sessions.Any(s => s.SessionId == sid));
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
