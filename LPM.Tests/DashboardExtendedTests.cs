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
        _svc    = new DashboardService(TestConfig.For(_dbPath));
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
        Assert.Equal(2, pcs.Count(p => !p.IsSolo));
    }

    [Fact]
    public void GetAllPcs_Dashboard_AddsSoloEntry_ForSoloTypeAuditors()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Tami");
        TestDbHelper.InsertPC(conn, pid);
        TestDbHelper.InsertAuditor(conn, pid, type: 3, isActive: true); // RegularAndSolo

        var pcs = _svc.GetAllPcs();
        // Should have 1 regular entry + 1 solo entry
        Assert.Equal(1, pcs.Count(p => !p.IsSolo));
        Assert.Equal(1, pcs.Count(p => p.IsSolo));
    }

    [Fact]
    public void GetAllPcs_Dashboard_SoloEntry_HasSuffix()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Aviv");
        TestDbHelper.InsertPC(conn, pid);
        TestDbHelper.InsertAuditor(conn, pid, type: 2, isActive: true); // SoloOnly

        var pcs  = _svc.GetAllPcs();
        var solo = pcs.Single(p => p.IsSolo);
        Assert.Contains("Solo", solo.FullName);
    }

    [Fact]
    public void GetAllPcs_Dashboard_SoloEntry_WorkCapacity_IsCS()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Tami");
        TestDbHelper.InsertPC(conn, pid);
        TestDbHelper.InsertAuditor(conn, pid, type: 3, isActive: true);

        var solo = _svc.GetAllPcs().Single(p => p.IsSolo);
        Assert.Equal("CS", solo.WorkCapacity);
    }

    [Fact]
    public void GetAllPcs_Dashboard_NoSoloEntry_ForType1_RegularOnly()
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, "Genia");
        TestDbHelper.InsertPC(conn, pid);
        TestDbHelper.InsertAuditor(conn, pid, type: 1, isActive: true);

        var pcs = _svc.GetAllPcs();
        Assert.Empty(pcs.Where(p => p.IsSolo));
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
        var row = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM MiscCharge WHERE MiscChargeId={id}");
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

        var seq1 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM MiscCharge WHERE MiscChargeId={id1}");
        var seq2 = TestDbHelper.Scalar(conn, $"SELECT SequenceInDay FROM MiscCharge WHERE MiscChargeId={id2}");
        Assert.Equal(1L, seq1);
        Assert.Equal(2L, seq2);
    }

    // =========================================================================
    // SetUserPcRole
    // =========================================================================

    [Fact]
    public void SetUserPcRole_ChangesWorkCapacity_ForNonSoloEntry()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        _svc.AddUserPc(audId, pcId, isSolo: false);
        _svc.SetUserPcRole(audId, pcId, "Miscellaneous");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT WorkCapacity FROM StaffPcList WHERE UserId={audId} AND PcId={pcId} AND IsSolo=0";
        var cap = cmd.ExecuteScalar() as string;
        Assert.Equal("Miscellaneous", cap);
    }

    [Fact]
    public void SetUserPcRole_DoesNotAffectSoloEntry()
    {
        // SetUserPcRole only touches IsSolo=0 rows
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        _svc.AddUserPc(audId, pcId, isSolo: false);
        _svc.AddUserPc(audId, pcId, isSolo: true);
        _svc.SetUserPcRole(audId, pcId, "Miscellaneous");

        // solo entry should still be "CS"
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT WorkCapacity FROM StaffPcList WHERE UserId={audId} AND PcId={pcId} AND IsSolo=1";
        var cap = cmd.ExecuteScalar() as string;
        Assert.Equal("CS", cap);
    }

    // =========================================================================
    // GetDayDetail – Miscellaneous role
    // =========================================================================

    [Fact]
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

    [Fact]
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
    // GetDayDetail – SoloAuditor role
    // =========================================================================

    [Fact]
    public void GetDayDetail_SoloAuditor_ReturnsSoloSessions_ForThatDay()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, type: 3);

        var date = new DateOnly(2024, 1, 15);
        _svc.AddSoloSession(audId, date, 3600, 0, false, null);

        var detail = _svc.GetDayDetail(audId, audId, date, "SoloAuditor");
        Assert.Single(detail.Sessions);
        Assert.Equal(3600, detail.Sessions[0].LengthSec);
    }

    [Fact]
    public void GetDayDetail_SoloAuditor_DoesNotReturnRegularSessions()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, type: 3);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var date = new DateOnly(2024, 1, 15);
        // Regular session (IsSolo=0)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-15", 7200, isSolo: false);
        // Solo session (IsSolo=1)
        _svc.AddSoloSession(audId, date, 3600, 0, false, null);

        var detail = _svc.GetDayDetail(audId, audId, date, "SoloAuditor");
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

        var pcs  = new List<PcInfo> { new PcInfo(pcId, "Client1", "CS", false) };
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
        TestDbHelper.InsertAuditor(conn, audId, type: 3);

        var grid = _svc.GetWeekGridSolo(audId, new DateOnly(2024, 1, 11));
        Assert.Empty(grid);
    }

    [Fact]
    public void GetWeekGridSolo_ReturnsSoloSessionSeconds()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, type: 3);

        var week = new DateOnly(2024, 1, 11); // Thursday
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 3600, 600, false, null);

        var grid = _svc.GetWeekGridSolo(audId, week);
        Assert.True(grid.TryGetValue((audId, 0), out var secs));
        Assert.Equal(3600 + 600, secs);
    }

    [Fact]
    public void GetWeekGridSolo_AggregatesTotalForSameDay()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, type: 3);

        var week = new DateOnly(2024, 1, 11);
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 3600, 0, false, null);
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 1800, 0, false, null);

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

        var pcs    = new List<PcInfo> { new PcInfo(pcId, "Client1", "Auditor", false) };
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
            new PcInfo(pc1, "ClientMore", "Auditor", false),
            new PcInfo(pc2, "ClientLess", "Auditor", false)
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
        TestDbHelper.InsertAuditor(conn, audId, type: 3);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11);
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 3600, 0, false, null);
        // Regular session should NOT be counted
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 7200, isSolo: false);

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

        var pcs = new List<PcInfo> { new PcInfo(pcId, "Client1", "Auditor", false) };
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
        var pcs  = new List<PcInfo> { new PcInfo(pcId, "Client1", "Auditor", false) };
        Assert.False(_svc.HasAnyWorkInWeek(audId, week, pcs));
    }

    [Fact]
    public void HasAnyWorkInWeek_ReturnsTrue_WhenMiscChargeInWeek()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId);
        var pcId  = TestDbHelper.InsertPerson(conn, "MiscPc");
        TestDbHelper.InsertPC(conn, pcId);

        var week = new DateOnly(2024, 1, 11);
        _svc.AddMiscCharge(audId, pcId, new DateOnly(2024, 1, 12), 1800, 0, false, null);

        var pcs = new List<PcInfo> { new PcInfo(pcId, "MiscPc", "Miscellaneous", false) };
        Assert.True(_svc.HasAnyWorkInWeek(audId, week, pcs));
    }

    [Fact]
    public void HasAnyWorkInWeek_SoloMode_ReturnsTrue_ForSoloSession()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "SoloAud");
        TestDbHelper.InsertAuditor(conn, audId, type: 3);

        var week = new DateOnly(2024, 1, 11);
        _svc.AddSoloSession(audId, new DateOnly(2024, 1, 11), 3600, 0, false, null);

        Assert.True(_svc.HasAnyWorkInWeek(audId, week, new List<PcInfo>(), soloMode: true));
    }

    [Fact]
    public void HasAnyWorkInWeek_SoloMode_ReturnsFalse_WhenOnlyRegularSessions()
    {
        using var conn = Open();
        var audId = TestDbHelper.InsertPerson(conn, "Aud1");
        TestDbHelper.InsertAuditor(conn, audId, type: 1);
        var pcId  = TestDbHelper.InsertPerson(conn, "Client1");
        TestDbHelper.InsertPC(conn, pcId);

        // Regular session only (IsSolo=0)
        TestDbHelper.InsertSession(conn, pcId, audId, "2024-01-11", 3600, isSolo: false);

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

        var pcs = new List<PcInfo> { new PcInfo(pcId, "Client1", "CS", false) };
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

        var pcs     = new List<PcInfo> { new PcInfo(pcId, "Client1", "CS", false) };
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
    public void FullWorkflow_Payment_ReducesBalance_VisibleInPcService()
    {
        var pcSvc = new PcService(TestConfig.For(_dbPath));
        var pcId  = pcSvc.AddPcWithPerson("Dana", "Cohen", "", "", "", "F");

        // Buy 5 hours
        pcSvc.AddPayment(pcId, "2024-01-01", 5, 1500, null);

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
    public void FullWorkflow_AuditorAddedViaService_IsRecognisedByIsAuditor()
    {
        var audSvc = new AuditorService(TestConfig.For(_dbPath));
        var audId  = audSvc.AddAuditor("Eitan", "G", null, type: 1);

        Assert.True(_svc.IsAuditor(audId));
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
