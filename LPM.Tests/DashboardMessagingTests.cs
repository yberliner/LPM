using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Tests for DashboardService messaging, permissions, and weekly remarks.
/// </summary>
public class DashboardMessagingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DashboardService _svc;

    public DashboardMessagingTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new DashboardService(TestConfig.For(_dbPath), new MessageNotifier());
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // SendMessage / GetPendingMessages / GetPendingMessageCount
    // =========================================================================

    [Fact]
    public void SendMessage_MessageAppearsInPending()
    {
        using var conn = Open();
        var from = TestDbHelper.InsertPerson(conn, "Alice", "A");
        var to   = TestDbHelper.InsertPerson(conn, "Bob", "B");

        _svc.SendMessage(from, to, "Hello Bob");

        var msgs = _svc.GetPendingMessages(to);
        Assert.Single(msgs);
        Assert.Equal("Hello Bob", msgs[0].MsgText);
        Assert.Contains("Alice", msgs[0].FromName);
    }

    [Fact]
    public void GetPendingMessageCount_ReturnsCorrectCount()
    {
        using var conn = Open();
        var from = TestDbHelper.InsertPerson(conn, "Carol", "C");
        var to   = TestDbHelper.InsertPerson(conn, "Dan", "D");

        Assert.Equal(0, _svc.GetPendingMessageCount(to));

        _svc.SendMessage(from, to, "Msg 1");
        _svc.SendMessage(from, to, "Msg 2");
        _svc.SendMessage(from, to, "Msg 3");

        Assert.Equal(3, _svc.GetPendingMessageCount(to));
    }

    [Fact]
    public void GetPendingMessages_DoesNotIncludeOtherUsersMessages()
    {
        using var conn = Open();
        var from = TestDbHelper.InsertPerson(conn, "Eve", "E");
        var to1  = TestDbHelper.InsertPerson(conn, "Fred", "F");
        var to2  = TestDbHelper.InsertPerson(conn, "Grace", "G");

        _svc.SendMessage(from, to1, "For Fred");
        _svc.SendMessage(from, to2, "For Grace");

        Assert.Single(_svc.GetPendingMessages(to1));
        Assert.Equal("For Fred", _svc.GetPendingMessages(to1)[0].MsgText);
    }

    // =========================================================================
    // AcknowledgeMessage — requires (messageId, toStaffId)
    // =========================================================================

    [Fact]
    public void AcknowledgeMessage_RemovesFromPending()
    {
        using var conn = Open();
        var from = TestDbHelper.InsertPerson(conn, "Hank", "H");
        var to   = TestDbHelper.InsertPerson(conn, "Ivan", "I");

        _svc.SendMessage(from, to, "Ack me");

        var msgs = _svc.GetPendingMessages(to);
        Assert.Single(msgs);

        _svc.AcknowledgeMessage(msgs[0].Id, to);

        Assert.Empty(_svc.GetPendingMessages(to));
        Assert.Equal(0, _svc.GetPendingMessageCount(to));
    }

    [Fact]
    public void AcknowledgeMessage_OnlyAffectsTargetMessage()
    {
        using var conn = Open();
        var from = TestDbHelper.InsertPerson(conn, "Jane", "J");
        var to   = TestDbHelper.InsertPerson(conn, "Kate", "K");

        _svc.SendMessage(from, to, "Msg A");
        _svc.SendMessage(from, to, "Msg B");

        var msgs = _svc.GetPendingMessages(to);
        Assert.Equal(2, msgs.Count);

        _svc.AcknowledgeMessage(msgs[0].Id, to);

        Assert.Single(_svc.GetPendingMessages(to));
    }

    // =========================================================================
    // SendAutoMessageToAdmins
    // =========================================================================

    [Fact]
    public void SendAutoMessageToAdmins_SendsToAllAdminUsers()
    {
        using var conn = Open();
        var sender = TestDbHelper.InsertPerson(conn, "Worker", "W");

        // Create two admin users (InsertUser creates stub person + core_users)
        var userId1 = TestDbHelper.InsertUser(conn, "Admin1", "pass1");
        TestDbHelper.AssignRole(conn, userId1, "Admin");
        var admin1PersonId = (int)TestDbHelper.Scalar(conn, $"SELECT PersonId FROM core_users WHERE Id={userId1}");

        var userId2 = TestDbHelper.InsertUser(conn, "Admin2", "pass2");
        TestDbHelper.AssignRole(conn, userId2, "Admin");
        var admin2PersonId = (int)TestDbHelper.Scalar(conn, $"SELECT PersonId FROM core_users WHERE Id={userId2}");

        _svc.SendAutoMessageToAdmins(sender, "Auto notification");

        Assert.True(_svc.GetPendingMessageCount(admin1PersonId) >= 1);
        Assert.True(_svc.GetPendingMessageCount(admin2PersonId) >= 1);
    }

    // =========================================================================
    // CheckOrRequestPermission
    // =========================================================================

    [Fact]
    public void CheckOrRequestPermission_ReturnsFalse_WhenNoPermission()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "Aud", "A");
        TestDbHelper.InsertAuditor(conn, auditor);
        var pc = TestDbHelper.InsertPerson(conn, "Pc", "P");
        TestDbHelper.InsertPC(conn, pc);

        // Need an admin user so SendAutoMessageToAdmins doesn't fail silently
        var admUsr = TestDbHelper.InsertUser(conn, "Adm", "pass");
        TestDbHelper.AssignRole(conn, admUsr, "Admin");

        Assert.False(_svc.CheckOrRequestPermission(auditor, pc));
    }

    [Fact]
    public void CheckOrRequestPermission_ReturnsTrue_WhenAllowAll()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "AllAud", "AA");
        TestDbHelper.InsertAuditor(conn, auditor);
        // Set AllowAll flag in core_users
        TestDbHelper.Exec(conn, $"UPDATE core_users SET AllowAll = 1 WHERE PersonId = {auditor}");
        var pc = TestDbHelper.InsertPerson(conn, "AnyPc", "AP");
        TestDbHelper.InsertPC(conn, pc);

        Assert.True(_svc.CheckOrRequestPermission(auditor, pc));
    }

    [Fact]
    public void CheckOrRequestPermission_ReturnsTrue_WhenApproved()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "AppAud", "AB");
        TestDbHelper.InsertAuditor(conn, auditor);
        var pc = TestDbHelper.InsertPerson(conn, "AppPc", "AC");
        TestDbHelper.InsertPC(conn, pc);

        _svc.AddApprovedPermission(auditor, pc);

        Assert.True(_svc.CheckOrRequestPermission(auditor, pc));
    }

    // =========================================================================
    // Permission CRUD
    // =========================================================================

    [Fact]
    public void GetPendingPermissionRequests_ReturnsEmpty_Initially()
    {
        Assert.Empty(_svc.GetPendingPermissionRequests());
    }

    [Fact]
    public void CheckOrRequestPermission_CreatesPendingRequest()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "ReqAud", "RA");
        TestDbHelper.InsertAuditor(conn, auditor);
        var pc = TestDbHelper.InsertPerson(conn, "ReqPc", "RP");
        TestDbHelper.InsertPC(conn, pc);

        var admUsr = TestDbHelper.InsertUser(conn, "ReqAdm", "pass");
        TestDbHelper.AssignRole(conn, admUsr, "Admin");

        _svc.CheckOrRequestPermission(auditor, pc);

        var pending = _svc.GetPendingPermissionRequests();
        Assert.Single(pending);
        // PermissionRequest has UserId (not AuditorId)
        Assert.Equal(auditor, pending[0].UserId);
        Assert.Equal(pc, pending[0].PcId);
    }

    [Fact]
    public void ApprovePermissionRequest_RemovesFromPending()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "ApvAud", "AA2");
        TestDbHelper.InsertAuditor(conn, auditor);
        var pc = TestDbHelper.InsertPerson(conn, "ApvPc", "AP2");
        TestDbHelper.InsertPC(conn, pc);

        var admUsr = TestDbHelper.InsertUser(conn, "ApvAdm", "pass");
        TestDbHelper.AssignRole(conn, admUsr, "Admin");

        _svc.CheckOrRequestPermission(auditor, pc);
        var pending = _svc.GetPendingPermissionRequests();

        _svc.ApprovePermissionRequest(pending[0].Id);

        Assert.Empty(_svc.GetPendingPermissionRequests());
        Assert.True(_svc.CheckOrRequestPermission(auditor, pc));
    }

    [Fact]
    public void RejectPermissionRequest_RemovesFromPendingAndPcList()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "RejAud", "RJ");
        TestDbHelper.InsertAuditor(conn, auditor);
        var pc = TestDbHelper.InsertPerson(conn, "RejPc", "RJP");
        TestDbHelper.InsertPC(conn, pc);

        var admUsr = TestDbHelper.InsertUser(conn, "RejAdm", "pass");
        TestDbHelper.AssignRole(conn, admUsr, "Admin");

        _svc.CheckOrRequestPermission(auditor, pc);
        var pending = _svc.GetPendingPermissionRequests();

        _svc.RejectPermissionRequest(pending[0].Id);

        Assert.Empty(_svc.GetPendingPermissionRequests());
    }

    [Fact]
    public void AddApprovedPermission_IsIdempotent()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "IdmAud", "IM");
        TestDbHelper.InsertAuditor(conn, auditor);
        var pc = TestDbHelper.InsertPerson(conn, "IdmPc", "IP");
        TestDbHelper.InsertPC(conn, pc);

        _svc.AddApprovedPermission(auditor, pc);
        _svc.AddApprovedPermission(auditor, pc); // should not throw

        Assert.True(_svc.CheckOrRequestPermission(auditor, pc));
    }

    [Fact]
    public void RemovePermission_RemovesApprovedPermission()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "RmAud", "RM");
        TestDbHelper.InsertAuditor(conn, auditor);
        var pc = TestDbHelper.InsertPerson(conn, "RmPc", "RMP");
        TestDbHelper.InsertPC(conn, pc);

        _svc.AddApprovedPermission(auditor, pc);

        // Get the permission ID from sys_staff_pc_list (UserId = auditor)
        var permId = (int)TestDbHelper.Scalar(conn,
            $"SELECT Id FROM sys_staff_pc_list WHERE UserId = {auditor} AND PcId = {pc}");

        _svc.RemovePermission(permId);

        // Need admin for auto-message on re-request
        var admUsr = TestDbHelper.InsertUser(conn, "RmAdm", "pass");
        TestDbHelper.AssignRole(conn, admUsr, "Admin");

        Assert.False(_svc.CheckOrRequestPermission(auditor, pc));
    }

    // =========================================================================
    // GetWeeklyRemarks / SaveWeeklyRemarks
    // =========================================================================

    [Fact]
    public void GetWeeklyRemarks_ReturnsNull_WhenNone()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "WkAud", "WK");
        Assert.Null(_svc.GetWeeklyRemarks(auditor, "2024-06-06"));
    }

    [Fact]
    public void SaveWeeklyRemarks_ThenGetReturnsRemarks()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "WkAud2", "WK2");

        _svc.SaveWeeklyRemarks(auditor, "2024-06-06", "Great week");
        Assert.Equal("Great week", _svc.GetWeeklyRemarks(auditor, "2024-06-06"));
    }

    [Fact]
    public void SaveWeeklyRemarks_UpdatesExisting()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "WkAud3", "WK3");

        _svc.SaveWeeklyRemarks(auditor, "2024-06-06", "First");
        _svc.SaveWeeklyRemarks(auditor, "2024-06-06", "Updated");

        Assert.Equal("Updated", _svc.GetWeeklyRemarks(auditor, "2024-06-06"));
    }

    [Fact]
    public void SaveWeeklyRemarks_DifferentWeeks_Independent()
    {
        using var conn = Open();
        var auditor = TestDbHelper.InsertPerson(conn, "WkAud4", "WK4");

        _svc.SaveWeeklyRemarks(auditor, "2024-06-06", "Week 1");
        _svc.SaveWeeklyRemarks(auditor, "2024-06-13", "Week 2");

        Assert.Equal("Week 1", _svc.GetWeeklyRemarks(auditor, "2024-06-06"));
        Assert.Equal("Week 2", _svc.GetWeeklyRemarks(auditor, "2024-06-13"));
    }

    [Fact]
    public void SaveWeeklyRemarks_DifferentAuditors_Independent()
    {
        using var conn = Open();
        var aud1 = TestDbHelper.InsertPerson(conn, "WkAud5", "WK5");
        var aud2 = TestDbHelper.InsertPerson(conn, "WkAud6", "WK6");

        _svc.SaveWeeklyRemarks(aud1, "2024-06-06", "Aud1 remarks");
        _svc.SaveWeeklyRemarks(aud2, "2024-06-06", "Aud2 remarks");

        Assert.Equal("Aud1 remarks", _svc.GetWeeklyRemarks(aud1, "2024-06-06"));
        Assert.Equal("Aud2 remarks", _svc.GetWeeklyRemarks(aud2, "2024-06-06"));
    }

    // =========================================================================
    // GetUserIdByUsername — queries core_users.Username
    // =========================================================================

    [Fact]
    public void GetUserIdByUsername_ReturnsNull_WhenNotFound()
    {
        Assert.Null(_svc.GetUserIdByUsername("nonexistent"));
    }

    [Fact]
    public void GetUserIdByUsername_FindsByUsername_CaseInsensitive()
    {
        using var conn = Open();
        // InsertUser creates stub person + core_users with Username="tami"
        var uid = TestDbHelper.InsertUser(conn, "tami", "pass");
        var personId = (int)TestDbHelper.Scalar(conn, $"SELECT PersonId FROM core_users WHERE Id={uid}");

        Assert.Equal(personId, _svc.GetUserIdByUsername("tami"));
        Assert.Equal(personId, _svc.GetUserIdByUsername("TAMI"));
        Assert.Equal(personId, _svc.GetUserIdByUsername("Tami"));
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
