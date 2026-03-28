using LPM.Services;
using LPM.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LPM.Tests;

public class UserActivityServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly UserActivityService _svc;

    public UserActivityServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new UserActivityService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    private SqliteConnection OpenConn() => new($"Data Source={_dbPath}");

    private void Insert(string username, string activityAt, string action, string kind)
    {
        using var conn = OpenConn();
        conn.Open();
        TestDbHelper.InsertActivityLog(conn, username, activityAt, action, kind);
    }

    // ── In-memory circuit tracking (IsOnline / RecordLogin / RecordLogout) ─

    [Fact]
    public void IsOnline_ReturnsFalse_BeforeAnyLogin()
    {
        Assert.False(_svc.IsOnline("aviv"));
    }

    [Fact]
    public void RecordLogin_MakesUserOnline()
    {
        _svc.RecordLogin("aviv");
        Assert.True(_svc.IsOnline("aviv"));
    }

    [Fact]
    public void RecordLogout_MakesUserOffline()
    {
        _svc.RecordLogin("aviv");
        _svc.RecordLogout("aviv");
        Assert.False(_svc.IsOnline("aviv"));
    }

    [Fact]
    public void IsOnline_CaseInsensitive()
    {
        _svc.RecordLogin("Aviv");
        Assert.True(_svc.IsOnline("aviv"));
        Assert.True(_svc.IsOnline("AVIV"));
    }

    [Fact]
    public void MultipleLogins_RequireMatchingLogouts()
    {
        _svc.RecordLogin("aviv");
        _svc.RecordLogin("aviv");
        _svc.RecordLogout("aviv");
        Assert.True(_svc.IsOnline("aviv")); // one session still open
        _svc.RecordLogout("aviv");
        Assert.False(_svc.IsOnline("aviv"));
    }

    [Fact]
    public void ExtraLogout_DoesNotGoNegative()
    {
        _svc.RecordLogin("aviv");
        _svc.RecordLogout("aviv");
        _svc.RecordLogout("aviv"); // extra — should not throw or go negative
        Assert.False(_svc.IsOnline("aviv"));
    }

    [Fact]
    public void DifferentUsers_TrackedIndependently()
    {
        _svc.RecordLogin("aviv");
        Assert.True(_svc.IsOnline("aviv"));
        Assert.False(_svc.IsOnline("tami"));
    }

    // ── GetSummary ────────────────────────────────────────────────────────

    [Fact]
    public void GetSummary_EmptyWhenNoRows()
    {
        Assert.Empty(_svc.GetSummary());
    }

    [Fact]
    public void GetSummary_ReturnsOneRowPerUser()
    {
        Insert("aviv", "2025-03-01 10:00:00", "Login",     "login");
        Insert("aviv", "2025-03-01 11:00:00", "Navigated", "nav");
        Insert("tami", "2025-03-01 12:00:00", "Login",     "login");

        Assert.Equal(2, _svc.GetSummary().Count);
    }

    [Fact]
    public void GetSummary_GroupsCaseInsensitive()
    {
        Insert("Aviv", "2025-03-01 10:00:00", "Login",     "login");
        Insert("aviv", "2025-03-01 11:00:00", "Navigated", "nav");

        Assert.Single(_svc.GetSummary());
    }

    [Fact]
    public void GetSummary_ReturnsActionFromLatestRow()
    {
        Insert("aviv", "2025-03-01 09:00:00", "Login",         "login");
        Insert("aviv", "2025-03-01 11:00:00", "Opened folder", "nav");

        var row = Assert.Single(_svc.GetSummary());
        Assert.Equal("Opened folder", row.LastAction);
        Assert.Equal("nav",           row.LastKind);
    }

    [Fact]
    public void GetSummary_OrderedByMostRecentFirst()
    {
        Insert("tami", "2025-03-01 08:00:00", "Login", "login");
        Insert("aviv", "2025-03-01 12:00:00", "Login", "login");

        var result = _svc.GetSummary();

        Assert.Equal("aviv", result[0].Username);
        Assert.Equal("tami", result[1].Username);
    }

    [Fact]
    public void GetSummary_IsOnline_ReflectsInMemoryState()
    {
        Insert("aviv", "2025-03-01 10:00:00", "Login", "login");
        _svc.RecordLogin("aviv"); // mark online in memory

        var row = Assert.Single(_svc.GetSummary());
        Assert.True(row.IsOnline);
    }

    [Fact]
    public void GetSummary_IsOnline_FalseWhenNotLoggedIn()
    {
        Insert("aviv", "2025-03-01 10:00:00", "Login", "login");
        // no RecordLogin call — circuit count is 0

        var row = Assert.Single(_svc.GetSummary());
        Assert.False(row.IsOnline);
    }

    // ── GetAuditLog ───────────────────────────────────────────────────────

    [Fact]
    public void GetAuditLog_ReturnsRowsForUser()
    {
        Insert("aviv", "2025-03-01 10:00:00", "Login",     "login");
        Insert("aviv", "2025-03-01 11:00:00", "Navigated", "nav");
        Insert("tami", "2025-03-01 12:00:00", "Login",     "login");

        Assert.Equal(2, _svc.GetAuditLog("aviv", 0).Count);
    }

    [Fact]
    public void GetAuditLog_CaseInsensitive()
    {
        Insert("Aviv", "2025-03-01 10:00:00", "Login",     "login");
        Insert("AVIV", "2025-03-01 11:00:00", "Navigated", "nav");

        Assert.Equal(2, _svc.GetAuditLog("aviv", 0).Count);
    }

    [Fact]
    public void GetAuditLog_Days0_ReturnsAll()
    {
        var recent = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss");
        var old    = DateTime.UtcNow.AddDays(-200).ToString("yyyy-MM-dd HH:mm:ss");
        Insert("aviv", recent, "Recent", "nav");
        Insert("aviv", old,    "Old",    "nav");

        Assert.Equal(2, _svc.GetAuditLog("aviv", 0).Count);
    }

    [Fact]
    public void GetAuditLog_FiltersByDays()
    {
        var recent = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss");
        var old    = DateTime.UtcNow.AddDays(-100).ToString("yyyy-MM-dd HH:mm:ss");
        Insert("aviv", recent, "Recent", "nav");
        Insert("aviv", old,    "Old",    "nav");

        var result = _svc.GetAuditLog("aviv", 7);

        Assert.Single(result);
        Assert.Equal("Recent", result[0].Action);
    }

    [Fact]
    public void GetAuditLog_OrderedByMostRecentFirst()
    {
        Insert("aviv", "2025-03-01 08:00:00", "First", "nav");
        Insert("aviv", "2025-03-01 12:00:00", "Last",  "nav");

        var result = _svc.GetAuditLog("aviv", 0);

        Assert.Equal("Last",  result[0].Action);
        Assert.Equal("First", result[1].Action);
    }

    [Fact]
    public void GetAuditLog_EmptyWhenUserHasNoRows()
    {
        Assert.Empty(_svc.GetAuditLog("nobody", 0));
    }

    // ── Initialize (purge) ────────────────────────────────────────────────

    [Fact]
    public void Initialize_PurgesEntriesOlderThan90Days()
    {
        var old    = DateTime.UtcNow.AddDays(-91).ToString("yyyy-MM-dd HH:mm:ss");
        var recent = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss");
        Insert("aviv", old,    "OldEntry",    "nav");
        Insert("aviv", recent, "RecentEntry", "nav");

        _svc.Initialize();

        var result = _svc.GetAuditLog("aviv", 0);
        Assert.Single(result);
        Assert.Equal("RecentEntry", result[0].Action);
    }

    [Fact]
    public void Initialize_KeepsEntriesWithin90Days()
    {
        var ts = DateTime.UtcNow.AddDays(-89).ToString("yyyy-MM-dd HH:mm:ss");
        Insert("aviv", ts, "Action", "nav");

        _svc.Initialize();

        Assert.Single(_svc.GetAuditLog("aviv", 0));
    }

    // ── Static helper: TimeAgo ────────────────────────────────────────────

    [Fact]
    public void TimeAgo_JustNow()
    {
        var ts = DateTime.UtcNow.AddSeconds(-10).ToString("yyyy-MM-dd HH:mm:ss");
        Assert.Equal("just now", UserActivityService.TimeAgo(ts));
    }

    [Fact]
    public void TimeAgo_MinutesAgo()
    {
        var ts = DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-dd HH:mm:ss");
        Assert.Equal("5 min ago", UserActivityService.TimeAgo(ts));
    }

    [Fact]
    public void TimeAgo_HoursAgo()
    {
        var ts = DateTime.UtcNow.AddHours(-3).AddMinutes(-20).ToString("yyyy-MM-dd HH:mm:ss");
        Assert.StartsWith("3h", UserActivityService.TimeAgo(ts));
    }

    [Fact]
    public void TimeAgo_DaysAgo()
    {
        var ts = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd HH:mm:ss");
        Assert.Equal("2 days ago", UserActivityService.TimeAgo(ts));
    }

    [Fact]
    public void TimeAgo_OneDayAgo_Singular()
    {
        var ts = DateTime.UtcNow.AddDays(-1).AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss");
        Assert.Equal("1 day ago", UserActivityService.TimeAgo(ts));
    }

    [Fact]
    public void TimeAgo_InvalidString_ReturnsInput()
    {
        Assert.Equal("not-a-date", UserActivityService.TimeAgo("not-a-date"));
    }

    // ── Static helper: FriendlyPageName ──────────────────────────────────

    [Theory]
    [InlineData("/home",            "Home")]
    [InlineData("/admin/backup",    "Backup")]
    [InlineData("/admin/diagnosis", "Diagnosis")]
    [InlineData("/admin/users",     "User Management")]
    [InlineData("/pcfolder",        "PC Folder")]
    [InlineData("/import",          "Import")]
    [InlineData("/login",           "Login")]
    [InlineData("/usersettings",    "User Settings")]
    public void FriendlyPageName_KnownRoutes(string url, string expected)
    {
        Assert.Equal(expected, UserActivityService.FriendlyPageName(url));
    }

    [Fact]
    public void FriendlyPageName_UnknownAdminRoute_PrefixesAdmin()
    {
        Assert.StartsWith("Admin /", UserActivityService.FriendlyPageName("/admin/somepage"));
    }

    [Fact]
    public void FriendlyPageName_UnknownRoute_ReturnsLastSegment()
    {
        var result = UserActivityService.FriendlyPageName("/some/deep/path");
        Assert.Equal("path", result);
    }

    [Fact]
    public void FriendlyPageName_Root_ReturnsHome()
    {
        Assert.Equal("Home", UserActivityService.FriendlyPageName("/"));
    }

    [Fact]
    public void FriendlyPageName_CaseInsensitiveMatch()
    {
        Assert.Equal("Home", UserActivityService.FriendlyPageName("/Home"));
        Assert.Equal("Home", UserActivityService.FriendlyPageName("/HOME"));
    }
}
