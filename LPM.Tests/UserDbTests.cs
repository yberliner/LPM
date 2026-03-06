using Microsoft.Data.Sqlite;
using LPM.Auth;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Comprehensive tests for <see cref="UserDb"/>.
/// Covers login validation, password hashing, role assignment, and avatar management.
/// All tests use an isolated SQLite database pre-seeded with the Users/Roles/UserRoles schema.
/// </summary>
public class UserDbTests : IDisposable
{
    private readonly string _dbPath;
    private readonly UserDb _svc;

    public UserDbTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new UserDb(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // ValidateUser – success paths
    // =========================================================================

    [Fact]
    public void ValidateUser_ReturnsTrue_WithCorrectCredentials()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "tami", "Tami1234!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        Assert.True(_svc.ValidateUser("tami", "Tami1234!", out _));
    }

    [Fact]
    public void ValidateUser_IsCaseInsensitive_ForUsername()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "Genia", "Genia5678!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        Assert.True(_svc.ValidateUser("GENIA",  "Genia5678!", out _));
        Assert.True(_svc.ValidateUser("genia",  "Genia5678!", out _));
        Assert.True(_svc.ValidateUser("GeNiA",  "Genia5678!", out _));
    }

    [Fact]
    public void ValidateUser_PopulatesRoles_WithAssignedRoles()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "admin_user", "Admin9999!");
        TestDbHelper.AssignRole(conn, uid, "Admin");

        _svc.ValidateUser("admin_user", "Admin9999!", out var roles);
        Assert.Contains("Admin", roles);
    }

    [Fact]
    public void ValidateUser_ReturnsAllAssignedRoles()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "multiRole", "Multi1234!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");
        TestDbHelper.AssignRole(conn, uid, "Viewer");

        _svc.ValidateUser("multiRole", "Multi1234!", out var roles);
        Assert.Equal(2, roles.Count);
        Assert.Contains("CaseWorker", roles);
        Assert.Contains("Viewer",     roles);
    }

    [Fact]
    public void ValidateUser_ReturnsTrue_WithCaseWorkerRole()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "worker1", "Worker1234!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        Assert.True(_svc.ValidateUser("worker1", "Worker1234!", out var roles));
        Assert.Contains("CaseWorker", roles);
    }

    [Fact]
    public void ValidateUser_ReturnsTrue_WithViewerRole()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "viewer1", "View5678!");
        TestDbHelper.AssignRole(conn, uid, "Viewer");

        Assert.True(_svc.ValidateUser("viewer1", "View5678!", out var roles));
        Assert.Contains("Viewer", roles);
    }

    // =========================================================================
    // ValidateUser – failure paths
    // =========================================================================

    [Fact]
    public void ValidateUser_ReturnsFalse_WithWrongPassword()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "eitan", "Correct123!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        Assert.False(_svc.ValidateUser("eitan", "WrongPass!", out _));
    }

    [Fact]
    public void ValidateUser_ReturnsFalse_ForNonExistentUser()
    {
        Assert.False(_svc.ValidateUser("nobody", "pass123", out _));
    }

    [Fact]
    public void ValidateUser_ReturnsFalse_ForInactiveUser()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "inactive_user", "Pass1234!", isActive: false);
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        Assert.False(_svc.ValidateUser("inactive_user", "Pass1234!", out _));
    }

    [Fact]
    public void ValidateUser_ReturnsFalse_WithEmptyUsername()
    {
        Assert.False(_svc.ValidateUser("", "somepassword", out _));
    }

    [Fact]
    public void ValidateUser_ReturnsFalse_WithEmptyPassword()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "eyal", "EyalPass123!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        Assert.False(_svc.ValidateUser("eyal", "", out _));
    }

    [Fact]
    public void ValidateUser_ReturnsFalse_WithNullUsername()
    {
        Assert.False(_svc.ValidateUser(null!, "pass123", out _));
    }

    [Fact]
    public void ValidateUser_ReturnsFalse_WithNullPassword()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "samai", "SamaiPass1!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        Assert.False(_svc.ValidateUser("samai", null!, out _));
    }

    [Fact]
    public void ValidateUser_ReturnsFalse_WhenUserHasNoRoles()
    {
        // Even with correct password, no roles → returns false
        using var conn = Open();
        TestDbHelper.InsertUser(conn, "no_role", "NoRole123!");

        Assert.False(_svc.ValidateUser("no_role", "NoRole123!", out _));
    }

    [Fact]
    public void ValidateUser_ReturnsEmptyRoles_OnFailure()
    {
        _svc.ValidateUser("nobody", "bad", out var roles);
        Assert.Empty(roles);
    }

    [Fact]
    public void ValidateUser_PasswordIsCaseSensitive()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "carmela", "Secret123!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        // Password must be exact-case
        Assert.False(_svc.ValidateUser("carmela", "secret123!", out _));
        Assert.True(_svc.ValidateUser("carmela",  "Secret123!", out _));
    }

    // =========================================================================
    // ChangePassword
    // =========================================================================

    [Fact]
    public void ChangePassword_Succeeds_WithCorrectCurrentPassword()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "tami", "OldPass123!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        var result = _svc.ChangePassword("tami", "OldPass123!", "NewPass456!");
        Assert.Null(result);  // null = success
    }

    [Fact]
    public void ChangePassword_NewPasswordValidates()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "tami", "OldPass123!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        _svc.ChangePassword("tami", "OldPass123!", "NewPass456!");

        Assert.True(_svc.ValidateUser("tami", "NewPass456!", out _));
        Assert.False(_svc.ValidateUser("tami", "OldPass123!", out _));
    }

    [Fact]
    public void ChangePassword_ReturnsError_WithWrongCurrentPassword()
    {
        using var conn = Open();
        TestDbHelper.InsertUser(conn, "genia", "Correct123!");

        var result = _svc.ChangePassword("genia", "WrongCurrent!", "NewPass456!");
        Assert.NotNull(result);  // error message returned
    }

    [Fact]
    public void ChangePassword_ReturnsError_ForNonExistentUser()
    {
        var result = _svc.ChangePassword("nobody", "pass", "newpass");
        Assert.NotNull(result);
    }

    [Fact]
    public void ChangePassword_OldPasswordNoLongerValid_AfterChange()
    {
        using var conn = Open();
        var uid = TestDbHelper.InsertUser(conn, "eitan", "OldPass123!");
        TestDbHelper.AssignRole(conn, uid, "CaseWorker");

        _svc.ChangePassword("eitan", "OldPass123!", "BrandNew789!");

        Assert.False(_svc.ValidateUser("eitan", "OldPass123!", out _));
    }

    // =========================================================================
    // GetAvatarPath / SetAvatarPath
    // =========================================================================

    [Fact]
    public void GetAvatarPath_ReturnsNull_WhenNotSet()
    {
        using var conn = Open();
        TestDbHelper.InsertUser(conn, "tami", "Pass123!");

        Assert.Null(_svc.GetAvatarPath("tami"));
    }

    [Fact]
    public void SetAvatarPath_StoresRelativePath()
    {
        using var conn = Open();
        TestDbHelper.InsertUser(conn, "genia", "Pass456!");

        _svc.SetAvatarPath("genia", "/avatars/genia.png");

        Assert.Equal("/avatars/genia.png", _svc.GetAvatarPath("genia"));
    }

    [Fact]
    public void SetAvatarPath_CanClearPath_WithNull()
    {
        using var conn = Open();
        TestDbHelper.InsertUser(conn, "eital", "Pass789!");

        _svc.SetAvatarPath("eital", "/avatars/eital.png");
        _svc.SetAvatarPath("eital", null);

        Assert.Null(_svc.GetAvatarPath("eital"));
    }

    [Fact]
    public void GetAvatarPath_IsCaseInsensitive_ForUsername()
    {
        using var conn = Open();
        TestDbHelper.InsertUser(conn, "Yaniv", "Pass000!");

        _svc.SetAvatarPath("Yaniv", "/avatars/yaniv.jpg");

        Assert.Equal("/avatars/yaniv.jpg", _svc.GetAvatarPath("yaniv"));
        Assert.Equal("/avatars/yaniv.jpg", _svc.GetAvatarPath("YANIV"));
    }

    [Fact]
    public void SetAvatarPath_UpdatesExistingPath()
    {
        using var conn = Open();
        TestDbHelper.InsertUser(conn, "aviv", "AvivPass1!");

        _svc.SetAvatarPath("aviv", "/avatars/old.png");
        _svc.SetAvatarPath("aviv", "/avatars/new.png");

        Assert.Equal("/avatars/new.png", _svc.GetAvatarPath("aviv"));
    }

    [Fact]
    public void GetAvatarPath_ReturnsNull_ForNonExistentUser()
    {
        Assert.Null(_svc.GetAvatarPath("nobody"));
    }

    // =========================================================================
    // UserDb schema (EnsureSchema idempotency)
    // =========================================================================

    [Fact]
    public void UserDb_CanBeInstantiatedTwice_WithoutError()
    {
        // Second instantiation should not throw (EnsureSchema is idempotent)
        var ex = Record.Exception(() => new UserDb(TestConfig.For(_dbPath)));
        Assert.Null(ex);
    }

    [Fact]
    public void UserDb_AvatarPath_ColumnExists_AfterInit()
    {
        using var conn = Open();
        Assert.True(TestDbHelper.ColumnExists(conn, "Users", "AvatarPath"));
    }

    // =========================================================================
    // Multiple users independence
    // =========================================================================

    [Fact]
    public void MultipleUsers_CanCoexistWithDifferentPasswords()
    {
        using var conn = Open();
        var uid1 = TestDbHelper.InsertUser(conn, "user1", "Pass111!");
        var uid2 = TestDbHelper.InsertUser(conn, "user2", "Pass222!");
        TestDbHelper.AssignRole(conn, uid1, "CaseWorker");
        TestDbHelper.AssignRole(conn, uid2, "CaseWorker");

        Assert.True(_svc.ValidateUser("user1",  "Pass111!", out _));
        Assert.True(_svc.ValidateUser("user2",  "Pass222!", out _));
        Assert.False(_svc.ValidateUser("user1", "Pass222!", out _)); // wrong user's pass
    }

    [Fact]
    public void InactiveUser_DoesNotAffectOtherUsersLogin()
    {
        using var conn = Open();
        var uid1 = TestDbHelper.InsertUser(conn, "active",   "Pass111!", isActive: true);
        var uid2 = TestDbHelper.InsertUser(conn, "inactive", "Pass222!", isActive: false);
        TestDbHelper.AssignRole(conn, uid1, "CaseWorker");
        TestDbHelper.AssignRole(conn, uid2, "CaseWorker");

        Assert.True(_svc.ValidateUser("active",   "Pass111!", out _));
        Assert.False(_svc.ValidateUser("inactive", "Pass222!", out _));
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
