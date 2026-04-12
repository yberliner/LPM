using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LPM.Services;

public record PcInfo(int PcId, string FullName, string WorkCapacity, string Nick = "", bool IsActive = true);
public record SessionRow(int SessionId, int LengthSec, int AdminSec, bool IsFree, string? Summary, string CreatedAt, string AuditorName, string VerifiedStatus = "Pending", string? Name = null, bool IsSolo = false, string SessionDate = "");
public record CsReviewRow(int CsReviewId, int SessionId, int ReviewSec, string Status, string? Notes);
public record CsWorkRow(int CsWorkLogId, int LengthSec, string? Notes, string CreatedAt);
public record PcWeekItem(string FullName, int Seconds);
public record WeekTotal(DateOnly WeekStart, int TotalSeconds, List<PcWeekItem>? TopPcs = null)
{
    public string WeekLabel => WeekStart.ToString("dd/MM", CultureInfo.InvariantCulture);
}
public record DayDetail(List<SessionRow> Sessions, List<CsReviewRow> Reviews, List<CsWorkRow>? GeneralWork = null);

public record AdminSessionRow(
    int SessionId, int PcId, string PcName, string AuditorName, string SessionDate,
    int LengthSec, int AdminSec, bool IsFree,
    int ChargedRateCentsPerHour, int AuditorSalaryCentsPerHour,
    string VerifiedStatus);
public record PcSessionGroup(int PcId, string PcName, List<AdminSessionRow> Sessions);
public record AuditorSessionGroup(int AuditorId, string AuditorName, List<PcSessionGroup> PcGroups);

public record AdminCsRow(
    int CsReviewId, int SessionId, int PcId, string PcName, int CsId, string CsName,
    string SessionDate, int ReviewLengthSeconds, int CsSalaryCentsPerHour, string CsStatus,
    bool IsSolo = false, int ChargedCentsRatePerHour = 0, string? Notes = null);
public record PcCsGroup(int PcId, string PcName, List<AdminCsRow> Reviews);
public record CsReviewerGroup(int CsId, string CsName, List<PcCsGroup> PcGroups);

public record StaffMember(int PersonId, string FullName);

public record SalarySessionRow(string Date, string PcName, int DurationSec, int RateCentsPerHour, long PaymentCents, bool IsApproved);
public record SalaryCsRow(string Date, string PcName, int DurationSec, int RateCentsPerHour, long PaymentCents, bool IsApproved);
public record CommissionDetail(
    int PurchaseId, string PurchaseDate,
    string PaymentMethod, int PaymentGross, string PaymentDate,
    string ItemType, string ItemName, int ItemPrice, int TotalPurchasePrice,
    decimal ItemShare, decimal VatAmount, decimal CcAmount, decimal BookPrice,
    decimal NetAfterDeductions,
    decimal ReserveAmount, decimal NetBase, double CommissionPct,
    string? CourseFinishDate = null);
public record CommissionRow(string PcName, string Role, string Category, long AmountCents, CommissionDetail Detail);
public record UnassignedReferralRow(string PcName, string Category, long AmountCents, string Notes, CommissionDetail Detail);
public record SalaryReport(List<UserSalaryGroup> Groups, List<UnassignedReferralRow> UnassignedReferrals);
public record UserSalaryGroup(int PersonId, string FullName, List<SalarySessionRow> Sessions, List<SalaryCsRow> CsReviews, List<CommissionRow> Commissions)
{
    public long TotalCents => Sessions.Where(s => s.IsApproved).Sum(s => s.PaymentCents)
                            + CsReviews.Where(c => c.IsApproved).Sum(c => c.PaymentCents)
                            + Commissions.Sum(c => c.AmountCents);
}
public record StaffMessage(int Id, int FromId, string FromName, int ToId, string ToName, string MsgText, string CreatedAt, string? AcknowledgedAt);

public record PermissionRequest(int Id, int UserId, string AuditorName, int PcId, string PcName, string RequestedAt);

// ── Session Manager shared records ──────────────────────────────────────────
public record SessionListItem(int SessionId, string Name, string SessionDate,
    string AuditorName, int LengthSec, int AdminSec, bool IsFree,
    string VerifiedStatus, bool IsImported);
public record SessionDetailModel(
    int SessionId, int PcId, int? AuditorId, string AuditorName,
    string SessionDate, int SequenceInDay, int LengthSeconds, int AdminSeconds,
    bool IsFreeSession, int ChargeSeconds, int ChargedRateCentsPerHour,
    int AuditorSalaryCentsPerHour, string Name, string VerifiedStatus,
    string? ApprovedNotes, bool IsImported, string CreatedAt, int? CreatedByUserId,
    int? CsReviewId, int? CsId, string? CsName, int? ReviewLengthSeconds,
    string? ReviewedAt, string? CsStatus, string? CsNotes,
    int? CsSalaryCentsPerHour, int? CsChargedCentsRatePerHour);
public record SessionFkTable(string Table, List<string> Cols, List<List<string>> Rows);
public record SessionDeleteInfo(int SessionId, int PcId, string SessionName,
    List<SessionFkTable> FkTables, List<string> Files);
public record ApprovedPcEntry(int Id, int PcId, string PcName, string WorkCapacity = StaffRoles.Auditor);
public record AuditorPermGroup(int AuditorId, string AuditorName, bool AllowAll, List<ApprovedPcEntry> ApprovedPcs, string StaffRole = StaffRoles.Auditor);

public class DashboardService
{
    private readonly string _connectionString;
    private readonly MessageNotifier _messageNotifier;

    /// <summary>Fired after any data-mutating operation in MainHeader. Subscribers should refresh their view.</summary>
    public event Action? OnDataChanged;
    public void NotifyDataChanged() => OnDataChanged?.Invoke();

    // Reusable SQL expression for a person's display name (requires alias p for Persons)
    private const string FullNameExpr =
        "TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), ''))";

    public DashboardService(IConfiguration config, MessageNotifier messageNotifier)
    {
        _messageNotifier = messageNotifier;
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        RunMigrations();
    }

    private void RunMigrations()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Schema managed directly in DB — no CREATE TABLE statements here.

        // Ensure all active staff members exist in PCs table
        using var ensurePcsCmd = conn.CreateCommand();
        ensurePcsCmd.CommandText = @"
            INSERT OR IGNORE INTO core_pcs (PcId)
            SELECT PersonId FROM core_users WHERE IsActive = 1 AND StaffRole != 'None'";
        var pcsInserted = ensurePcsCmd.ExecuteNonQuery();
        if (pcsInserted > 0)
            Console.WriteLine($"[Startup] Added {pcsInserted} staff member(s) to PCs table.");

        // BUG check: sessions with AuditorId IS NULL but PC has no Solo user
        using var bugCmd = conn.CreateCommand();
        bugCmd.CommandText = @"
            SELECT DISTINCT s.PcId FROM sess_sessions s
            WHERE s.AuditorId IS NULL
              AND s.IsImported = 0
              AND NOT EXISTS (
                  SELECT 1 FROM core_users u
                  WHERE u.PersonId = s.PcId AND u.StaffRole = 'Solo' AND u.IsActive = 1
              )";
        using var bugR = bugCmd.ExecuteReader();
        while (bugR.Read())
            Console.WriteLine($"BUG!! PC {bugR.GetInt32(0)} has sessions with AuditorId=NULL but no active Solo user in core_users (StaffRole='Solo')");


    }

    // ── Import Session ─────────────────────────────────────────

    public record ApprovedPc(int PcId, string FullName, bool IsAlsoSolo = false);

    /// <summary>Returns PCs the user is allowed to access (AllowAll or approved permission).</summary>
    public List<ApprovedPc> GetApprovedPcsForUser(int userId, string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Query by username (unique) to always get the correct row — avoids ambiguity when a PersonId has both Auditor and Solo rows
        using var aaCmd = conn.CreateCommand();
        aaCmd.CommandText = "SELECT COALESCE(AllowAll, 0), COALESCE(StaffRole, '') FROM core_users WHERE LOWER(Username) = LOWER(@user) AND IsActive = 1";
        aaCmd.Parameters.AddWithValue("@user", username);
        using var aaR = aaCmd.ExecuteReader();
        bool allowAll = false;
        bool isSolo = false;
        if (aaR.Read()) { allowAll = aaR.GetInt32(0) == 1; isSolo = aaR.GetString(1) == StaffRoles.Solo; }
        aaR.Close();

        using var cmd = conn.CreateCommand();
        if (isSolo)
        {
            // Solo user: only their own PC
            cmd.CommandText = $@"
                SELECT pc.PcId, {FullNameExpr} AS FullName, CASE WHEN EXISTS (SELECT 1 FROM core_users cu WHERE cu.PersonId = pc.PcId AND cu.StaffRole = 'Solo' AND cu.IsActive = 1) THEN 1 ELSE 0 END
                FROM core_pcs pc
                JOIN core_persons p ON p.PersonId = pc.PcId
                WHERE pc.PcId = @uid";
            cmd.Parameters.AddWithValue("@uid", userId);
        }
        else if (allowAll)
        {
            cmd.CommandText = $@"
                SELECT pc.PcId, {FullNameExpr} AS FullName, CASE WHEN EXISTS (SELECT 1 FROM core_users cu WHERE cu.PersonId = pc.PcId AND cu.StaffRole = 'Solo' AND cu.IsActive = 1) THEN 1 ELSE 0 END
                FROM core_pcs pc
                JOIN core_persons p ON p.PersonId = pc.PcId
                ORDER BY p.FirstName, p.LastName";
        }
        else
        {
            cmd.CommandText = $@"
                SELECT pc.PcId, {FullNameExpr} AS FullName, CASE WHEN EXISTS (SELECT 1 FROM core_users cu WHERE cu.PersonId = pc.PcId AND cu.StaffRole = 'Solo' AND cu.IsActive = 1) THEN 1 ELSE 0 END
                FROM sys_staff_pc_list spl
                JOIN core_pcs pc ON pc.PcId = spl.PcId
                JOIN core_persons p ON p.PersonId = pc.PcId
                WHERE spl.UserId = @uid AND spl.IsApproved = 1
                ORDER BY p.FirstName, p.LastName";
            cmd.Parameters.AddWithValue("@uid", userId);
        }
        var list = new List<ApprovedPc>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ApprovedPc(r.GetInt32(0), r.GetString(1), r.GetInt32(2) == 1));
        return list;
    }

    /// <summary>
    /// Returns PCs the user is allowed to SESSION on (Add Session / Write to Folder Summary).
    /// Unlike GetApprovedPcsForUser, this ALWAYS uses sys_staff_pc_list — AllowAll is ignored.
    /// Solo users can only session their own PC.
    /// </summary>
    public List<ApprovedPc> GetSessionablePcsForUser(int userId, string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Query by username (unique) to always get the correct row
        using var roleCmd = conn.CreateCommand();
        roleCmd.CommandText = "SELECT COALESCE(StaffRole,'') FROM core_users WHERE LOWER(Username) = LOWER(@user) AND IsActive = 1";
        roleCmd.Parameters.AddWithValue("@user", username);
        var staffRole = roleCmd.ExecuteScalar() as string ?? "";

        using var cmd = conn.CreateCommand();
        if (staffRole == StaffRoles.Solo)
        {
            cmd.CommandText = $@"
                SELECT pc.PcId, {FullNameExpr} AS FullName, CASE WHEN EXISTS (SELECT 1 FROM core_users cu WHERE cu.PersonId = pc.PcId AND cu.StaffRole = 'Solo' AND cu.IsActive = 1) THEN 1 ELSE 0 END
                FROM core_pcs pc
                JOIN core_persons p ON p.PersonId = pc.PcId
                WHERE pc.PcId = @uid";
            cmd.Parameters.AddWithValue("@uid", userId);
        }
        else
        {
            cmd.CommandText = $@"
                SELECT pc.PcId, {FullNameExpr} AS FullName, CASE WHEN EXISTS (SELECT 1 FROM core_users cu WHERE cu.PersonId = pc.PcId AND cu.StaffRole = 'Solo' AND cu.IsActive = 1) THEN 1 ELSE 0 END
                FROM sys_staff_pc_list spl
                JOIN core_pcs pc ON pc.PcId = spl.PcId
                JOIN core_persons p ON p.PersonId = pc.PcId
                WHERE spl.UserId = @uid AND spl.IsApproved = 1
                ORDER BY p.FirstName, p.LastName";
            cmd.Parameters.AddWithValue("@uid", userId);
        }
        var list = new List<ApprovedPc>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ApprovedPc(r.GetInt32(0), r.GetString(1), r.GetInt32(2) == 1));
        return list;
    }

    /// <summary>Insert a new session and return the SessionId. Pass isSolo=true for solo auditors (AuditorId stored as NULL).</summary>
    /// <summary>
    /// Returns the SessionId of an existing session with the same (PcId, AuditorId, Name),
    /// or null if none exists. AuditorId comparison uses IS (handles NULL correctly).
    /// </summary>
    private static int? FindDuplicateSession(SqliteConnection conn, int pcId, object audParam, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT SessionId FROM sess_sessions
            WHERE PcId = @pc AND AuditorId IS @aud AND Name = @name
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@aud", audParam);
        cmd.Parameters.AddWithValue("@name", name);
        var result = cmd.ExecuteScalar();
        return result is null ? null : Convert.ToInt32(result);
    }

    public int CreateImportedSession(int pcId, int auditorId, string sessionName,
        int lengthSeconds = 0, int adminSeconds = 0, bool isFreeSession = false, bool isSolo = false,
        string? sessionDate = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var today = sessionDate ?? DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        object audParam = isSolo ? DBNull.Value : (object)auditorId;

        // Guard: return existing session if same (PcId, AuditorId, Name) already exists
        var existing = FindDuplicateSession(conn, pcId, audParam, sessionName);
        if (existing.HasValue)
        {
            Console.WriteLine($"[DashboardService] Duplicate session skipped — PC {pcId}, name: '{sessionName}', existing SessionId: {existing.Value}");
            return existing.Value;
        }

        // Calculate SequenceInDay
        using var seqCmd = conn.CreateCommand();
        seqCmd.CommandText = @"
            SELECT COALESCE(MAX(SequenceInDay), 0)
            FROM sess_sessions
            WHERE PcId = @pc AND AuditorId IS @aud AND SessionDate = @dt";
        seqCmd.Parameters.AddWithValue("@pc", pcId);
        seqCmd.Parameters.AddWithValue("@aud", audParam);
        seqCmd.Parameters.AddWithValue("@dt", today);
        var maxSeq = (long)(seqCmd.ExecuteScalar() ?? 0L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_sessions
                (PcId, AuditorId, SessionDate, SequenceInDay, LengthSeconds, AdminSeconds, IsFreeSession, Name, CreatedByUserId, CreatedAt)
            VALUES (@pc, @aud, @dt, @seq, @len, @admin, @free, @name, @creator, datetime('now', '+2 hours'));
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@aud", audParam);
        cmd.Parameters.AddWithValue("@dt", today);
        cmd.Parameters.AddWithValue("@seq", maxSeq + 1);
        cmd.Parameters.AddWithValue("@len", lengthSeconds);
        cmd.Parameters.AddWithValue("@admin", adminSeconds);
        cmd.Parameters.AddWithValue("@free", isFreeSession ? 1 : 0);
        cmd.Parameters.AddWithValue("@name", sessionName);
        cmd.Parameters.AddWithValue("@creator", auditorId);
        var sessionId = Convert.ToInt32(cmd.ExecuteScalar());
        Console.WriteLine($"[DashboardService] Created session for PC {pcId}, name: '{sessionName}', length: {lengthSeconds}s, solo: {isSolo}");
        return sessionId;
    }

    /// <summary>Insert a session with a specific date, and mark it verified. Returns SessionId.</summary>
    public int CreateImportedSessionWithDate(int pcId, int auditorId, string sessionName,
        string sessionDate, string createdAt, int verifiedByUserId, bool isSolo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        object audParam = isSolo ? DBNull.Value : (object)(-1);

        // Guard: return existing session if same (PcId, AuditorId, Name) already exists
        var existing = FindDuplicateSession(conn, pcId, audParam, sessionName);
        if (existing.HasValue)
        {
            Console.WriteLine($"[DashboardService] Duplicate session skipped — PC {pcId}, name: '{sessionName}', existing SessionId: {existing.Value}");
            return existing.Value;
        }

        using var seqCmd = conn.CreateCommand();
        seqCmd.CommandText = @"
            SELECT COALESCE(MAX(SequenceInDay), 0)
            FROM sess_sessions
            WHERE PcId = @pc AND AuditorId IS @aud AND SessionDate = @dt";
        seqCmd.Parameters.AddWithValue("@pc", pcId);
        seqCmd.Parameters.AddWithValue("@aud", audParam);   // must match what INSERT writes
        seqCmd.Parameters.AddWithValue("@dt", sessionDate);
        var maxSeq = (long)(seqCmd.ExecuteScalar() ?? 0L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_sessions
                (PcId, AuditorId, SessionDate, SequenceInDay, LengthSeconds, Name,
                 CreatedByUserId, CreatedAt, VerifiedStatus, VerifiedByUserId, VerifiedAt, IsImported)
            VALUES (@pc, @aud, @dt, @seq, 0, @name,
                    @creator, @createdAt, 'Verified', @verifier, @verifiedAt, 1);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@aud", isSolo ? DBNull.Value : (object)(-1));
        cmd.Parameters.AddWithValue("@dt", sessionDate);
        cmd.Parameters.AddWithValue("@seq", maxSeq + 1);
        cmd.Parameters.AddWithValue("@name", sessionName);
        cmd.Parameters.AddWithValue("@creator", verifiedByUserId);
        cmd.Parameters.AddWithValue("@createdAt", createdAt);
        cmd.Parameters.AddWithValue("@verifier", verifiedByUserId);
        cmd.Parameters.AddWithValue("@verifiedAt", createdAt);
        var sessionId = Convert.ToInt32(cmd.ExecuteScalar());

        // Mark CS review as done by import mechanism (CsId = -1)
        using var crCmd = conn.CreateCommand();
        crCmd.CommandText = @"
            INSERT INTO cs_reviews (SessionId, CsId, ReviewLengthSeconds, ReviewedAt, Status)
            VALUES (@sid, -1, 0, @reviewedAt, 'Done')";
        crCmd.Parameters.AddWithValue("@sid", sessionId);
        crCmd.Parameters.AddWithValue("@reviewedAt", createdAt);
        crCmd.ExecuteNonQuery();

        Console.WriteLine($"[DashboardService] Created imported session with date for PC {pcId}, name: '{sessionName}', date: {sessionDate}");
        return sessionId;
    }

    public record FolderItem(int ItemId, string Name, string Section);

    public List<FolderItem> GetFolderItems()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ItemId, Name, Section FROM lkp_folder_items ORDER BY Section, ItemId";
        var list = new List<FolderItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FolderItem(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    // ── Staff Permissions ─────────────────────────────────────────

    /// <summary>
    /// Called when an auditor adds a PC to their dashboard. Returns true if the auditor is
    /// permitted (AllowAll=1 or existing approved permission). Returns false if a pending
    /// request was created and the PC should appear grayed out.
    /// </summary>
    public bool CheckOrRequestPermission(int auditorId, int pcId, string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Query by username (unique) to always get the correct row
        using var aaCmd = conn.CreateCommand();
        aaCmd.CommandText = "SELECT COALESCE(AllowAll,0) FROM core_users WHERE LOWER(Username) = LOWER(@user) AND IsActive = 1";
        aaCmd.Parameters.AddWithValue("@user", username);
        var allowAll = aaCmd.ExecuteScalar() is long aa && aa == 1;
        if (allowAll)
        {
            Console.WriteLine($"[DashboardService] Permission check for user {auditorId} on PC {pcId}: AllowAll");
            // Auto-approve: set IsApproved=1 for any pending entry (AllowAll users skip admin review)
            using var approveCmd = conn.CreateCommand();
            approveCmd.CommandText = @"
                UPDATE sys_staff_pc_list SET IsApproved = 1
                WHERE UserId = @aud AND PcId = @pc AND IsApproved = 0";
            approveCmd.Parameters.AddWithValue("@aud", auditorId);
            approveCmd.Parameters.AddWithValue("@pc", pcId);
            approveCmd.ExecuteNonQuery();
            return true;
        }

        // Check existing permission
        using var chkCmd = conn.CreateCommand();
        chkCmd.CommandText = "SELECT IsApproved FROM sys_staff_pc_list WHERE UserId = @aud AND PcId = @pc";
        chkCmd.Parameters.AddWithValue("@aud", auditorId);
        chkCmd.Parameters.AddWithValue("@pc",  pcId);
        var existing = chkCmd.ExecuteScalar();
        if (existing is long approved)
        {
            var result = approved == 1;
            Console.WriteLine($"[DashboardService] Permission check for user {auditorId} on PC {pcId}: {(result ? "Approved" : "Pending")}");
            return result;
        }

        // No existing record — create a pending request
        using var insCmd = conn.CreateCommand();
        insCmd.CommandText = $@"
            INSERT OR IGNORE INTO sys_staff_pc_list (UserId, PcId, WorkCapacity, IsApproved, RequestedAt)
            VALUES (@aud, @pc, '{StaffRoles.Auditor}', 0, datetime('now'))";
        insCmd.Parameters.AddWithValue("@aud", auditorId);
        insCmd.Parameters.AddWithValue("@pc",  pcId);
        var inserted = insCmd.ExecuteNonQuery();

        // Send automatic message to all Admin-role users about the permission request
        if (inserted > 0)
        {
            var staffName = GetPersonName(conn, auditorId);
            var pcName    = GetPersonName(conn, pcId);
            var msgText   = $"Automatic msg: {staffName} request permission to add {pcName}";
            SendAutoMessageToAdmins(auditorId, msgText);
        }

        Console.WriteLine($"[DashboardService] Permission check for user {auditorId} on PC {pcId}: RequestCreated");
        return false;
    }

    private string GetPersonName(SqliteConnection conn, int personId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {FullNameExpr} FROM core_persons p WHERE p.PersonId = @id";
        cmd.Parameters.AddWithValue("@id", personId);
        return cmd.ExecuteScalar() as string ?? $"Person #{personId}";
    }

    public string GetPersonName(int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return GetPersonName(conn, personId);
    }

    public void SendAutoMessageToAdmins(int fromStaffId, string msgText)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // Find Admin-type users and resolve their PersonId
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT PersonId FROM core_users
            WHERE UserType = 'Admin' AND IsActive = 1";
        var adminPersonIds = new List<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) adminPersonIds.Add(r.GetInt32(0));

        foreach (var adminId in adminPersonIds)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = @"
                INSERT INTO sys_staff_messages (FromStaffId, ToStaffId, MsgText)
                VALUES (@from, @to, @msg)";
            ins.Parameters.AddWithValue("@from", fromStaffId);
            ins.Parameters.AddWithValue("@to",   adminId);
            ins.Parameters.AddWithValue("@msg",  msgText);
            ins.ExecuteNonQuery();
            _messageNotifier.NotifyNewMessage(adminId);
        }
        Console.WriteLine($"[DashboardService] Sent auto message: '{msgText}'");
    }

    /// Returns all PcIds in the auditor's StaffPcList that are NOT explicitly approved.
    /// Includes PCs added before the permission system existed.
    public HashSet<int> GetUnapprovedPcIds(int auditorId, string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Query by username (unique) to always get the correct row
        using var aaCmd = conn.CreateCommand();
        aaCmd.CommandText = "SELECT COALESCE(AllowAll, 0) FROM core_users WHERE LOWER(Username) = LOWER(@user) AND IsActive = 1";
        aaCmd.Parameters.AddWithValue("@user", username);
        if (aaCmd.ExecuteScalar() is long aa && aa == 1) return [];

        // Any PC in StaffPcList without an IsApproved=1 entry is unapproved
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PcId FROM sys_staff_pc_list WHERE UserId = @aud AND IsApproved = 0";
        cmd.Parameters.AddWithValue("@aud", auditorId);
        var set = new HashSet<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt32(0));
        return set;
    }

    public List<PermissionRequest> GetPendingPermissionRequests()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT spl.Id, spl.UserId,
                   TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), '')) AS AuditorName,
                   spl.PcId,
                   TRIM(pp.FirstName || ' ' || COALESCE(NULLIF(pp.LastName,''), '')) AS PcName,
                   spl.RequestedAt
            FROM sys_staff_pc_list spl
            JOIN core_persons pa ON pa.PersonId = spl.UserId
            JOIN core_persons pp ON pp.PersonId = spl.PcId
            WHERE spl.IsApproved = 0
            ORDER BY spl.RequestedAt DESC, pa.FirstName";
        var list = new List<PermissionRequest>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PermissionRequest(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2),
                r.GetInt32(3), r.GetString(4),
                r.IsDBNull(5) ? "" : r.GetString(5)));
        return list;
    }

    public void ApprovePermissionRequest(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sys_staff_pc_list SET IsApproved = 1 WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Approved permission request {id}");
    }

    public void RejectPermissionRequest(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sys_staff_pc_list WHERE Id = @id AND IsApproved = 0";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Rejected permission request {id}");
    }

    public List<AuditorPermGroup> GetAuditorPermGroups()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Get all active auditors/CS/solo with AllowAll
        using var audCmd = conn.CreateCommand();
        audCmd.CommandText = $@"
            SELECT u.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS Name,
                   COALESCE(u.AllowAll, 0),
                   COALESCE(u.StaffRole, '{StaffRoles.Auditor}')
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.IsActive = 1 AND u.StaffRole IN {StaffRoles.SqlInAuditorCSSolo()}
            ORDER BY p.FirstName, p.LastName";
        var auditors = new List<(int Id, string Name, bool AllowAll, string StaffRole)>();
        using var ar = audCmd.ExecuteReader();
        while (ar.Read())
            auditors.Add((ar.GetInt32(0), ar.GetString(1), ar.GetInt32(2) == 1, ar.GetString(3)));

        // Get approved permissions per auditor
        using var permCmd = conn.CreateCommand();
        permCmd.CommandText = @"
            SELECT spl.Id, spl.UserId, spl.PcId,
                   TRIM(pp.FirstName || ' ' || COALESCE(NULLIF(pp.LastName,''), '')) AS PcName,
                   spl.WorkCapacity
            FROM sys_staff_pc_list spl
            JOIN core_persons pp ON pp.PersonId = spl.PcId
            WHERE spl.IsApproved = 1
            ORDER BY pp.FirstName, pp.LastName";
        var permsByAuditor = new Dictionary<int, List<ApprovedPcEntry>>();
        using var pr = permCmd.ExecuteReader();
        while (pr.Read())
        {
            int audId = pr.GetInt32(1);
            if (!permsByAuditor.ContainsKey(audId)) permsByAuditor[audId] = new();
            permsByAuditor[audId].Add(new ApprovedPcEntry(pr.GetInt32(0), pr.GetInt32(2), pr.GetString(3), pr.GetString(4)));
        }

        return auditors.Select(a => new AuditorPermGroup(
            a.Id, a.Name, a.AllowAll,
            permsByAuditor.GetValueOrDefault(a.Id) ?? new(),
            a.StaffRole)).ToList();
    }

    public void SetAuditorAllowAll(int auditorId, bool allow)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE core_users SET AllowAll = @v WHERE PersonId = @id AND StaffRole IN {StaffRoles.SqlInAuditorCS()}";
        cmd.Parameters.AddWithValue("@v",  allow ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", auditorId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Set AllowAll={allow} for auditor {auditorId}");
    }

    /// Admin adds a PC permission directly (approved).
    public void AddApprovedPermission(int auditorId, int pcId, string workCapacity = StaffRoles.Auditor)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sys_staff_pc_list (UserId, PcId, WorkCapacity, IsApproved, RequestedAt)
            VALUES (@aud, @pc, @cap, 1, datetime('now'))
            ON CONFLICT(UserId, PcId) DO UPDATE SET IsApproved = 1, WorkCapacity = @cap";
        cmd.Parameters.AddWithValue("@aud", auditorId);
        cmd.Parameters.AddWithValue("@pc",  pcId);
        cmd.Parameters.AddWithValue("@cap", workCapacity);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Added approved permission for auditor {auditorId}, PC {pcId} as {workCapacity}");
    }

    public void RemovePermission(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sys_staff_pc_list WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Removed permission {id}");
    }

    /// <summary>
    /// Returns the PersonId for the given username (matches Persons.FirstName case-insensitively).
    /// </summary>
    public int? GetUserIdByUsername(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PersonId FROM core_users WHERE LOWER(Username) = LOWER(@u) AND IsActive = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : null;
    }


    /// <summary>Check if a user (by PersonId) can access a PC's folder.
    /// Admins always can. Otherwise requires approved permission or AllowAll.</summary>
    /// <summary>
    /// Returns true if the user has this PC on their dashboard AND has approved permission for it.
    /// Used by CsNotificationService to decide whether to refresh a user's Home screen.
    /// </summary>
    public bool UserHasApprovedPcOnDashboard(int userId, int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Check AllowAll flag
        using var aaCmd = conn.CreateCommand();
        aaCmd.CommandText = $"SELECT COALESCE(AllowAll,0) FROM core_users WHERE PersonId = @uid AND IsActive = 1 AND StaffRole IN {StaffRoles.SqlInAuditorCS()} LIMIT 1";
        aaCmd.Parameters.AddWithValue("@uid", userId);
        if (aaCmd.ExecuteScalar() is long a && a == 1) return true;

        // Must have approved entry in staff list
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sys_staff_pc_list WHERE UserId = @uid AND PcId = @pid AND IsApproved = 1";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@pid", pcId);
        return cmd.ExecuteScalar() is not null;
    }

    public bool CanAccessPcFolder(int personId, int pcId, string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Query the exact row for this username (unique) — avoids picking wrong row for dual-role users
        using var cmdAllow = conn.CreateCommand();
        cmdAllow.CommandText = "SELECT COALESCE(AllowAll,0), COALESCE(StaffRole,'') FROM core_users WHERE LOWER(Username) = LOWER(@user) AND IsActive = 1";
        cmdAllow.Parameters.AddWithValue("@user", username);
        using var ar = cmdAllow.ExecuteReader();
        if (ar.Read())
        {
            if (ar.GetString(1) == StaffRoles.Solo) return pcId == personId; // Solo: own PC only (ignore AllowAll)
            if (ar.GetString(1) == StaffRoles.SeniorCS) return true; // SeniorCS: any PC
            if (ar.GetInt32(0) == 1) return true;                   // AllowAll (non-Solo only)
        }
        ar.Close();

        // Check approved permission
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sys_staff_pc_list WHERE UserId = @aid AND PcId = @pid AND IsApproved = 1";
        cmd.Parameters.AddWithValue("@aid", personId);
        cmd.Parameters.AddWithValue("@pid", pcId);
        return cmd.ExecuteScalar() is not null;
    }

    public bool IsAuditor(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM core_users WHERE PersonId = @id AND IsActive = 1 AND StaffRole IN {StaffRoles.SqlInAuditorCS()} LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() is not null;
    }

    public bool IsCS(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM core_users WHERE PersonId = @id AND IsActive = 1 AND StaffRole IN {StaffRoles.SqlInCS()} LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() is not null;
    }

    public bool IsSeniorCS(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM core_users WHERE PersonId = @id AND IsActive = 1 AND StaffRole = '{StaffRoles.SeniorCS}' LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>Returns all active CS/SeniorCS staff, excluding the given user.</summary>
    public List<(int PersonId, string FullName)> GetCsStaffList(int excludeUserId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT u.PersonId, TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            WHERE u.StaffRole IN {StaffRoles.SqlInCS()} AND u.IsActive = 1 AND u.PersonId != @exclude
            ORDER BY p.FirstName, p.LastName";
        cmd.Parameters.AddWithValue("@exclude", excludeUserId);
        var list = new List<(int, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public List<PcInfo> GetUserPcs(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT pc.PcId,
                   {FullNameExpr} AS FullName,
                   spl.WorkCapacity,
                   COALESCE(p.Nick, '') AS Nick
            FROM sys_staff_pc_list spl
            JOIN core_pcs     pc ON pc.PcId    = spl.PcId
            JOIN core_persons p  ON p.PersonId = pc.PcId
            WHERE spl.UserId = @uid
            ORDER BY p.FirstName, p.LastName";
        cmd.Parameters.AddWithValue("@uid", userId);
        var list = new List<PcInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcInfo(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    /// Returns PcIds that also have a Solo user (PersonId matches a core_users Solo row).
    public HashSet<int> GetSoloPcIds()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PersonId FROM core_users WHERE StaffRole = 'Solo' AND IsActive = 1";
        var set = new HashSet<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt32(0));
        return set;
    }

    public List<PcInfo> GetAllPcs()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Regular entries for all PCs
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT pc.PcId,
                   {FullNameExpr} AS FullName,
                   COALESCE(p.Nick, '') AS Nick,
                   COALESCE(p.IsActive, 1) AS IsActive
            FROM core_pcs     pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            ORDER BY COALESCE(p.IsActive,1) DESC, p.FirstName, p.LastName";
        var list = new List<PcInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcInfo(r.GetInt32(0), r.GetString(1), StaffRoles.Auditor, r.GetString(2), r.GetInt32(3) == 1));

        return list;
    }

    public void AddUserPc(int userId, int pcId, string workCapacity = StaffRoles.Auditor)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Insert new entry as pending; if already exists update WorkCapacity only (never touch IsApproved or RequestedAt)
        cmd.CommandText = @"
            INSERT INTO sys_staff_pc_list (UserId, PcId, WorkCapacity, IsApproved, RequestedAt)
            VALUES (@uid, @pcId, @cap, 0, datetime('now'))
            ON CONFLICT(UserId, PcId) DO UPDATE SET WorkCapacity = @cap";
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@cap",  workCapacity);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Added PC {pcId} to user {userId} as {workCapacity}");
    }

    public void RemoveUserPc(int userId, int pcId, string workCapacity = StaffRoles.Auditor)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sys_staff_pc_list WHERE UserId = @uid AND PcId = @pcId";
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Removed PC {pcId} from user {userId}");
    }

    public void SetUserPcRole(int userId, int pcId, string role)
    {
        // Delegate to AddUserPc which implements mutual exclusivity rules
        AddUserPc(userId, pcId, role);
        Console.WriteLine($"[DashboardService] SetUserPcRole: PC {pcId} → '{role}' for user {userId}");
    }

    /// <summary>
    /// Grid data keyed by (pcId, dayIndex 0=Thu).
    /// Each PC's time is computed from Sessions (Auditor role) or CsReviews (CS role).
    /// </summary>
    public Dictionary<(int pcId, int dayIndex), int> GetWeekGrid(
        int userId, DateOnly weekStart, List<PcInfo> userPcs)
    {
        var result = new Dictionary<(int, int), int>();
        if (userPcs.Count == 0) return result;

        var dates    = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var dateList = string.Join(",", dates.Select(d => $"'{d:yyyy-MM-dd}'"));

        var auditorPcIds = userPcs.Where(p => p.WorkCapacity == StaffRoles.Auditor).Select(p => p.PcId).ToList();
        var csPcIds      = userPcs.Where(p => p.WorkCapacity == StaffRoles.CS).Select(p => p.PcId).ToList();
        var soloAuditorIds = csPcIds.Count > 0 ? GetSoloPcIds().Intersect(csPcIds).ToList() : new List<int>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        if (auditorPcIds.Count > 0)
        {
            var pcList = string.Join(",", auditorPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT PcId, SessionDate, SUM(LengthSeconds + AdminSeconds)
                FROM sess_sessions
                WHERE AuditorId = @uid AND PcId IN ({pcList}) AND SessionDate IN ({dateList})
                GROUP BY PcId, SessionDate";
            cmd.Parameters.AddWithValue("@uid", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var pcId   = r.GetInt32(0);
                var date   = DateOnly.Parse(r.GetString(1));
                var secs   = r.GetInt32(2);
                var dayIdx = dates.IndexOf(date);
                if (dayIdx < 0) continue;
                var key = (pcId, dayIdx);
                result[key] = result.GetValueOrDefault(key) + secs;
            }
        }

        if (csPcIds.Count > 0)
        {
            var pcList = string.Join(",", csPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.PcId, s.SessionDate, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE cr.CsId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate IN ({dateList}) AND s.AuditorId IS NOT NULL
                GROUP BY s.PcId, s.SessionDate";
            cmd.Parameters.AddWithValue("@uid", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var pcId   = r.GetInt32(0);
                var date   = DateOnly.Parse(r.GetString(1));
                var secs   = r.GetInt32(2);
                var dayIdx = dates.IndexOf(date);
                if (dayIdx < 0) continue;
                var key = (pcId, dayIdx);
                result[key] = result.GetValueOrDefault(key) + secs;
            }

            // Also include standalone General CS work (not linked to any session)
            using var wCmd = conn.CreateCommand();
            wCmd.CommandText = $@"
                SELECT PcId, WorkDate, SUM(LengthSeconds)
                FROM cs_work_log
                WHERE CsId = @uid AND PcId IN ({pcList}) AND WorkDate IN ({dateList})
                GROUP BY PcId, WorkDate";
            wCmd.Parameters.AddWithValue("@uid", userId);
            using var wr = wCmd.ExecuteReader();
            while (wr.Read())
            {
                var pcId   = wr.GetInt32(0);
                var date   = DateOnly.Parse(wr.GetString(1));
                var secs   = wr.GetInt32(2);
                var dayIdx = dates.IndexOf(date);
                if (dayIdx < 0) continue;
                var key = (pcId, dayIdx);
                result[key] = result.GetValueOrDefault(key) + secs;
            }
        }

        // CS Solo columns: reviewing sessions where AuditorId IS NULL (solo self-sessions)
        // Grid key uses -pcId to appear in a separate CS Solo table column
        if (soloAuditorIds.Count > 0)
        {
            var pcList = string.Join(",", soloAuditorIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.PcId, s.SessionDate, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE cr.CsId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate IN ({dateList}) AND s.AuditorId IS NULL
                GROUP BY s.PcId, s.SessionDate";
            cmd.Parameters.AddWithValue("@uid", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var pcId   = r.GetInt32(0);
                var date   = DateOnly.Parse(r.GetString(1));
                var secs   = r.GetInt32(2);
                var dayIdx = dates.IndexOf(date);
                if (dayIdx < 0) continue;
                var key = (-pcId, dayIdx);   // negative key for CS Solo column
                result[key] = result.GetValueOrDefault(key) + secs;
            }
        }

        return result;
    }

    /// <summary>
    /// Auditor role → this user's own sessions for the PC+date; no reviews.
    /// CS role → ALL sessions for the PC+date (with auditor name) + ALL reviews for those sessions.
    /// </summary>
    public DayDetail GetDayDetail(int userId, int pcId, DateOnly date, string role)
    {
        var sessions    = new List<SessionRow>();
        var reviews     = new List<CsReviewRow>();
        var generalWork = new List<CsWorkRow>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var dateStr = date.ToString("yyyy-MM-dd");

        if (role == StaffRoles.Auditor)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT s.SessionId, s.LengthSeconds, s.AdminSeconds,
                       s.IsFreeSession, s.CreatedAt,
                       p.FirstName, s.VerifiedStatus, s.Name
                FROM sess_sessions s
                JOIN core_persons p ON p.PersonId = s.AuditorId
                WHERE s.AuditorId = @uid AND s.PcId = @pcId AND s.SessionDate = @date
                ORDER BY s.SequenceInDay";
            cmd.Parameters.AddWithValue("@uid",  userId);
            cmd.Parameters.AddWithValue("@pcId", pcId);
            cmd.Parameters.AddWithValue("@date", dateStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                sessions.Add(new SessionRow(
                    r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                    r.GetInt32(3) == 1,
                    null,
                    r.IsDBNull(4) ? ""   : r.GetString(4),
                    r.IsDBNull(5) ? ""   : r.GetString(5),
                    r.IsDBNull(6) ? "Pending" : r.GetString(6),
                    r.IsDBNull(7) ? null : r.GetString(7),
                    SessionDate: dateStr));
            }
        }
        else  // CS or CSSolo role
        {
            // All sessions for this PC+date, with auditor's first name
            // CSSolo column: only sessions where AuditorId IS NULL; CS: AuditorId IS NOT NULL
            bool isCSSolo = role == "CSSolo";
            using var sessCmd = conn.CreateCommand();
            sessCmd.CommandText = isCSSolo
                ? @"SELECT s.SessionId, s.LengthSeconds, s.AdminSeconds,
                       s.IsFreeSession, s.CreatedAt,
                       'Solo' AS AuditorName, s.VerifiedStatus, s.Name
                FROM sess_sessions s
                WHERE s.PcId = @pcId AND s.SessionDate = @date AND s.AuditorId IS NULL
                ORDER BY s.SequenceInDay"
                : @"SELECT s.SessionId, s.LengthSeconds, s.AdminSeconds,
                       s.IsFreeSession, s.CreatedAt,
                       COALESCE(p.FirstName,'') AS AuditorName, s.VerifiedStatus, s.Name
                FROM sess_sessions s
                LEFT JOIN core_persons p ON p.PersonId = s.AuditorId
                WHERE s.PcId = @pcId AND s.SessionDate = @date AND s.AuditorId IS NOT NULL
                ORDER BY s.SequenceInDay";
            sessCmd.Parameters.AddWithValue("@pcId", pcId);
            sessCmd.Parameters.AddWithValue("@date", dateStr);
            using var rs = sessCmd.ExecuteReader();
            while (rs.Read())
            {
                sessions.Add(new SessionRow(
                    rs.GetInt32(0), rs.GetInt32(1), rs.GetInt32(2),
                    rs.GetInt32(3) == 1,
                    null,
                    rs.IsDBNull(4) ? ""   : rs.GetString(4),
                    rs.IsDBNull(5) ? ""   : rs.GetString(5),
                    rs.IsDBNull(6) ? "Pending" : rs.GetString(6),
                    rs.IsDBNull(7) ? null : rs.GetString(7),
                    IsSolo: isCSSolo,
                    SessionDate: dateStr));
            }

            // All reviews for those sessions (by any CS worker — UNIQUE per session anyway)
            using var revCmd = conn.CreateCommand();
            revCmd.CommandText = @"
                SELECT cr.CsReviewId, cr.SessionId, cr.ReviewLengthSeconds,
                       cr.Status, cr.Notes
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE s.PcId = @pcId AND s.SessionDate = @date";
            revCmd.Parameters.AddWithValue("@pcId", pcId);
            revCmd.Parameters.AddWithValue("@date", dateStr);
            using var rr = revCmd.ExecuteReader();
            while (rr.Read())
            {
                reviews.Add(new CsReviewRow(
                    rr.GetInt32(0), rr.GetInt32(1), rr.GetInt32(2),
                    rr.GetString(3),
                    rr.IsDBNull(4) ? null : rr.GetString(4)));
            }

            // General CS work (not linked to any session)
            using var workCmd = conn.CreateCommand();
            workCmd.CommandText = @"
                SELECT CsWorkLogId, LengthSeconds, Notes, CreatedAt
                FROM cs_work_log
                WHERE CsId = @uid AND PcId = @pcId AND WorkDate = @date
                ORDER BY CsWorkLogId";
            workCmd.Parameters.AddWithValue("@uid",  userId);
            workCmd.Parameters.AddWithValue("@pcId", pcId);
            workCmd.Parameters.AddWithValue("@date", dateStr);
            using var rw = workCmd.ExecuteReader();
            while (rw.Read())
            {
                generalWork.Add(new CsWorkRow(
                    rw.GetInt32(0), rw.GetInt32(1),
                    rw.IsDBNull(2) ? null : rw.GetString(2),
                    rw.IsDBNull(3) ? ""   : rw.GetString(3)));
            }
        }

        return new DayDetail(sessions, reviews, generalWork);
    }

    public int AddSession(
        int auditorId, int pcId, DateOnly date,
        int lengthSec, int adminSec, bool isFree, string? summary)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var dateStr = date.ToString("yyyy-MM-dd");

        using var seqCmd = conn.CreateCommand();
        seqCmd.CommandText = @"
            SELECT COALESCE(MAX(SequenceInDay), 0) + 1
            FROM sess_sessions
            WHERE PcId = @pcId AND SessionDate = @date";
        seqCmd.Parameters.AddWithValue("@pcId", pcId);
        seqCmd.Parameters.AddWithValue("@date", dateStr);
        var seq = (long)(seqCmd.ExecuteScalar() ?? 1L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_sessions
              (PcId, AuditorId, SessionDate, SequenceInDay,
               LengthSeconds, AdminSeconds, IsFreeSession,
               ChargeSeconds, ChargedRateCentsPerHour,
               CreatedAt)
            VALUES
              (@pcId, @audId, @date, @seq,
               @len, @adm, @free,
               0, 0,
               datetime('now', '+2 hours'))";
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@audId", auditorId);
        cmd.Parameters.AddWithValue("@date",  dateStr);
        cmd.Parameters.AddWithValue("@seq",   seq);
        cmd.Parameters.AddWithValue("@len",   lengthSec);
        cmd.Parameters.AddWithValue("@adm",   adminSec);
        cmd.Parameters.AddWithValue("@free",  isFree ? 1 : 0);
        cmd.ExecuteNonQuery();

        using var rowIdCmd = conn.CreateCommand();
        rowIdCmd.CommandText = "SELECT last_insert_rowid()";
        var newSessionId = (int)(long)rowIdCmd.ExecuteScalar()!;
        Console.WriteLine($"[DashboardService] Added session {newSessionId} for PC {pcId}, auditor {auditorId}");
        return newSessionId;
    }

    /// <summary>Insert a free-session memo row into sess_sessions. Returns the new SessionId.</summary>
    public int AddMemoSession(int auditorId, int pcId, string name, DateOnly date, bool solo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var dateStr = date.ToString("yyyy-MM-dd");

        using var seqCmd = conn.CreateCommand();
        seqCmd.CommandText = @"
            SELECT COALESCE(MAX(SequenceInDay), 0) + 1
            FROM sess_sessions
            WHERE PcId = @pcId AND SessionDate = @date";
        seqCmd.Parameters.AddWithValue("@pcId", pcId);
        seqCmd.Parameters.AddWithValue("@date", dateStr);
        var seq = (long)(seqCmd.ExecuteScalar() ?? 1L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_sessions
              (PcId, AuditorId, SessionDate, SequenceInDay,
               LengthSeconds, AdminSeconds, IsFreeSession,
               ChargeSeconds, ChargedRateCentsPerHour,
               Name, CreatedByUserId, CreatedAt)
            VALUES
              (@pcId, @audId, @date, @seq,
               0, 0, 1,
               0, 0,
               @name, @creator, datetime('now', '+2 hours'))";
        cmd.Parameters.AddWithValue("@pcId",    pcId);
        cmd.Parameters.AddWithValue("@audId",   solo ? DBNull.Value : auditorId);
        cmd.Parameters.AddWithValue("@date",    dateStr);
        cmd.Parameters.AddWithValue("@seq",     seq);
        cmd.Parameters.AddWithValue("@name",    name);
        cmd.Parameters.AddWithValue("@creator", auditorId);
        cmd.ExecuteNonQuery();

        using var rowIdCmd2 = conn.CreateCommand();
        rowIdCmd2.CommandText = "SELECT last_insert_rowid()";
        var newId = (int)(long)rowIdCmd2.ExecuteScalar()!;
        Console.WriteLine($"[DashboardService] Added memo session {newId} for PC {pcId}, auditor {auditorId}, name='{name}'");
        return newId;
    }

    public int AddMiscCharge(
        int auditorId, int pcId, DateOnly date,
        int lengthSec, int adminSec, bool isFree, string? summary)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var dateStr = date.ToString("yyyy-MM-dd");

        using var seqCmd = conn.CreateCommand();
        seqCmd.CommandText = @"
            SELECT COALESCE(MAX(SequenceInDay), 0) + 1
            FROM sess_misc_charges
            WHERE AuditorId = @uid AND PcId = @pcId AND ChargeDate = @date";
        seqCmd.Parameters.AddWithValue("@uid",  auditorId);
        seqCmd.Parameters.AddWithValue("@pcId", pcId);
        seqCmd.Parameters.AddWithValue("@date", dateStr);
        var seq = (long)(seqCmd.ExecuteScalar() ?? 1L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_misc_charges
              (AuditorId, PcId, ChargeDate, SequenceInDay,
               LengthSeconds, AdminSeconds, IsFree, Summary, CreatedAt)
            VALUES
              (@audId, @pcId, @date, @seq,
               @len, @adm, @free, @sum, datetime('now', '+2 hours'))";
        cmd.Parameters.AddWithValue("@audId", auditorId);
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@date",  dateStr);
        cmd.Parameters.AddWithValue("@seq",   seq);
        cmd.Parameters.AddWithValue("@len",   lengthSec);
        cmd.Parameters.AddWithValue("@adm",   adminSec);
        cmd.Parameters.AddWithValue("@free",  isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@sum",   (object?)summary ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        using var rowIdCmd = conn.CreateCommand();
        rowIdCmd.CommandText = "SELECT last_insert_rowid()";
        var newChargeId = (int)(long)rowIdCmd.ExecuteScalar()!;
        Console.WriteLine($"[DashboardService] Added misc charge {newChargeId} for PC {pcId}, auditor {auditorId}");
        return newChargeId;
    }

    public int AddCsReview(
        int csId, int sessionId, int reviewSec, string status, string? notes)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Guard: if a review already exists for this session, return its id instead of inserting
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT CsReviewId FROM cs_reviews WHERE SessionId = @sid LIMIT 1";
        checkCmd.Parameters.AddWithValue("@sid", sessionId);
        var existing = checkCmd.ExecuteScalar();
        if (existing is not null)
        {
            var existingId = Convert.ToInt32(existing);
            Console.WriteLine($"[DashboardService] Duplicate cs_review skipped for session {sessionId}, existing CsReviewId: {existingId}");
            return existingId;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cs_reviews
              (SessionId, CsId, ReviewLengthSeconds, ReviewedAt, Status, Notes)
            VALUES
              (@sid, @csId, @rev, datetime('now', '+2 hours'), @status, @notes)";
        cmd.Parameters.AddWithValue("@sid",    sessionId);
        cmd.Parameters.AddWithValue("@csId",   csId);
        cmd.Parameters.AddWithValue("@rev",    reviewSec);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@notes",  (object?)notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        using var rowIdCmd = conn.CreateCommand();
        rowIdCmd.CommandText = "SELECT last_insert_rowid()";
        var newReviewId = (int)(long)rowIdCmd.ExecuteScalar()!;
        Console.WriteLine($"[DashboardService] Added CS review {newReviewId} for session {sessionId}");
        return newReviewId;
    }

    public void UpdateSession(int sessionId, int lengthSec, int adminSec, bool isFree, string? summary)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_sessions
            SET LengthSeconds=@len, AdminSeconds=@adm, IsFreeSession=@free
            WHERE SessionId=@id";
        cmd.Parameters.AddWithValue("@len",  lengthSec);
        cmd.Parameters.AddWithValue("@adm",  adminSec);
        cmd.Parameters.AddWithValue("@free", isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@id",   sessionId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Updated session {sessionId}");
    }

    public void UpdateCsReview(int csReviewId, int callerCsId, int reviewSec, string status, string? notes)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Ownership check: only the assigned CS user may update their own review
        cmd.CommandText = @"
            UPDATE cs_reviews
            SET ReviewLengthSeconds=@rev, Status=@status, Notes=@notes
            WHERE CsReviewId=@id AND CsId=@csId";
        cmd.Parameters.AddWithValue("@rev",    reviewSec);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@notes",  (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id",     csReviewId);
        cmd.Parameters.AddWithValue("@csId",   callerCsId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Updated CS review {csReviewId} by csId={callerCsId}");
    }

    /// <summary>
    /// Deletes the cs_reviews row for a given session. Returns rows affected (must be 1).
    /// Throws if more than 1 row would be affected (safety guard).
    /// </summary>
    public bool IsCsReviewApproved(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Status, COALESCE(CsSalaryCentsPerHour, 0) FROM cs_reviews WHERE SessionId = @s LIMIT 1";
        cmd.Parameters.AddWithValue("@s", sessionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return false;
        return r.GetString(0) == "Approved" || r.GetInt32(1) > 0;
    }

    public int DeleteCsReview(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cs_reviews WHERE SessionId = @s";
        cmd.Parameters.AddWithValue("@s", sessionId);
        var affected = cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] DeleteCsReview sessionId={sessionId} — {affected} row(s) deleted");
        return affected;
    }

    /// <summary>
    /// Returns total seconds worked per week for the last <paramref name="weekCount"/> weeks,
    /// ending with <paramref name="latestWeekStart"/>. Respects each PC's role.
    /// </summary>
    public List<WeekTotal> GetWeeklyTotals(
        int userId, DateOnly latestWeekStart, int weekCount, List<PcInfo> userPcs)
    {
        var weeks = Enumerable.Range(0, weekCount)
            .Select(i => latestWeekStart.AddDays(-(weekCount - 1 - i) * 7))
            .ToList();
        var result = weeks.ToDictionary(w => w, _ => 0);

        if (userPcs.Count == 0)
            return weeks.Select(w => new WeekTotal(w, 0)).ToList();

        var startStr = weeks[0].ToString("yyyy-MM-dd");
        var auditorPcIds = userPcs.Where(p => p.WorkCapacity == StaffRoles.Auditor).Select(p => p.PcId).ToList();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var pcTotals = weeks.ToDictionary(w => w, _ => new Dictionary<string, int>());

        void Accumulate(string dateStr, int secs, string? pcName = null)
        {
            if (!DateOnly.TryParse(dateStr, out var d)) return;
            var ws = GetWeekStart(d);
            if (!result.ContainsKey(ws)) return;
            result[ws] += secs;
            if (pcName != null)
            {
                var dict = pcTotals[ws];
                dict[pcName] = dict.GetValueOrDefault(pcName) + secs;
            }
        }

        if (auditorPcIds.Count > 0)
        {
            var pcList = string.Join(",", auditorPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.SessionDate,
                       TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS FullName,
                       SUM(s.LengthSeconds + s.AdminSeconds)
                FROM sess_sessions s
                JOIN core_persons p ON p.PersonId = s.PcId
                WHERE s.AuditorId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate >= @start
                GROUP BY s.SessionDate, s.PcId";
            cmd.Parameters.AddWithValue("@uid",   userId);
            cmd.Parameters.AddWithValue("@start", startStr);
            using var r = cmd.ExecuteReader();
            while (r.Read()) Accumulate(r.GetString(0), r.GetInt32(2), r.GetString(1));
        }

        // CS columns intentionally excluded from weekly totals graph

        return weeks.Select(w =>
        {
            var tops = pcTotals[w]
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new PcWeekItem(kv.Key, kv.Value))
                .ToList();
            return new WeekTotal(w, result[w], tops);
        }).ToList();
    }

    public int AddCsWork(int csId, int pcId, DateOnly date, int lengthSec, string? notes)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cs_work_log (CsId, PcId, WorkDate, LengthSeconds, Notes, CreatedAt)
            VALUES (@csId, @pcId, @date, @len, @notes, datetime('now', '+2 hours'))";
        cmd.Parameters.AddWithValue("@csId", csId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@len",  lengthSec);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        using var rowIdCmd = conn.CreateCommand();
        rowIdCmd.CommandText = "SELECT last_insert_rowid()";
        var newWorkId = (int)(long)rowIdCmd.ExecuteScalar()!;
        Console.WriteLine($"[DashboardService] Added CS work log {newWorkId} for PC {pcId}");
        return newWorkId;
    }

    public static DateOnly GetWeekStart(DateOnly d)
    {
        int offset = ((int)d.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
        return d.AddDays(-offset);
    }

    public static string Fmt(int s)
    {
        if (s == 0) return "-";
        int totalMin = (int)Math.Round(s / 60.0); // standard rounding (>=30s rounds up)
        return $"{totalMin / 60}:{totalMin % 60:D2}";
    }

    /// Returns a formatted H:MM string, or "" for zero/negative values (suitable for input pre-fill).
    public static string FmtOrBlank(int s)
    {
        if (s <= 0) return "";
        int totalMin = (int)Math.Round(s / 60.0);
        return $"{totalMin / 60}:{totalMin % 60:D2}";
    }

    /// Grid-key convention: CSSolo columns use -PcId so same person can appear in both columns.
    /// Before calling GKey, CSAndCSSolo entries must be normalized to CS or CSSolo.
    public static int GKey(PcInfo pc) => pc.WorkCapacity == "CSSolo" ? -(pc.PcId) : pc.PcId;

    public HashSet<(int pcId, int dayIndex)> GetPendingCsMarkers(
    int csId, DateOnly weekStart, List<PcInfo> userPcs)
    {
        var result = new HashSet<(int pcId, int dayIndex)>();

        var allCsPcIds   = userPcs.Where(p => p.WorkCapacity == StaffRoles.CS).Select(p => p.PcId).ToList();
        var soloSet      = allCsPcIds.Count > 0 ? GetSoloPcIds() : new HashSet<int>();
        var soloPcIds    = allCsPcIds.Where(id => soloSet.Contains(id)).ToList();
        var regularPcIds = allCsPcIds.Where(id => !soloSet.Contains(id)).ToList();

        if (regularPcIds.Count == 0 && soloPcIds.Count == 0)
            return result;

        var dates    = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var dateList = string.Join(",", dates.Select(d => $"'{d:yyyy-MM-dd}'"));

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        void RunQuery(List<int> pcIds, bool solo)
        {
            if (pcIds.Count == 0) return;
            var pcList = string.Join(",", pcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.PcId, s.SessionDate
                FROM sess_sessions s
                LEFT JOIN cs_reviews cr ON cr.SessionId = s.SessionId
                WHERE s.PcId IN ({pcList})
                  AND s.SessionDate IN ({dateList})
                  AND s.AuditorId {(solo ? "IS NULL" : "IS NOT NULL")}
                  AND cr.CsReviewId IS NULL
                GROUP BY s.PcId, s.SessionDate";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var pcId   = r.GetInt32(0);
                var date   = DateOnly.Parse(r.GetString(1));
                var dayIdx = dates.IndexOf(date);
                if (dayIdx >= 0 && dayIdx < 7)
                    result.Add((solo ? -pcId : pcId, dayIdx));  // negative key for solo columns
            }
        }

        RunQuery(regularPcIds, false);
        RunQuery(soloPcIds,    true);

        return result;
    }

    /// Returns PcIds that have sessions where PcId=AuditorId (solo) with no CsReview yet.
    public HashSet<int> GetPcIdsWithPendingSoloReviews()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT s.PcId
            FROM sess_sessions s
            LEFT JOIN cs_reviews cr ON cr.SessionId = s.SessionId
            WHERE s.AuditorId IS NULL AND cr.CsReviewId IS NULL";
        var set = new HashSet<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt32(0));
        return set;
    }

    /// Returns PcIds that have sessions where PcId!=AuditorId (regular) with no CsReview yet.
    public HashSet<int> GetPcIdsWithPendingRegularReviews()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT s.PcId
            FROM sess_sessions s
            LEFT JOIN cs_reviews cr ON cr.SessionId = s.SessionId
            WHERE s.AuditorId IS NOT NULL AND cr.CsReviewId IS NULL";
        var set = new HashSet<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt32(0));
        return set;
    }

    // ── Solo Auditor methods ──

    public bool IsSoloAuditor(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM core_users WHERE PersonId = @id AND IsActive = 1 AND StaffRole = 'Solo' LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() is not null;
    }

    public PcInfo? GetSoloAuditorInfo(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // BUG check: verify this user actually has StaffRole='Solo'
        cmd.CommandText = "SELECT 1 FROM core_users WHERE PersonId = @id AND StaffRole = 'Solo' AND IsActive = 1";
        cmd.Parameters.AddWithValue("@id", userId);
        if (cmd.ExecuteScalar() is null)
        {
            Console.WriteLine($"BUG!! GetSoloAuditorInfo called for userId={userId} but no active Solo user found in core_users");
            return null;
        }
        cmd.Parameters.Clear();
        cmd.CommandText = @"
            SELECT TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''), ''))
            FROM core_persons WHERE PersonId = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        var name = cmd.ExecuteScalar() as string;
        return name is null ? null : new PcInfo(userId, name, "SoloAuditor");
    }

    public Dictionary<(int pcId, int dayIndex), int> GetWeekGridSolo(int userId, DateOnly weekStart)
    {
        var result = new Dictionary<(int, int), int>();
        var dates    = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var dateList = string.Join(",", dates.Select(d => $"'{d:yyyy-MM-dd}'"));

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT SessionDate, SUM(LengthSeconds + AdminSeconds)
            FROM sess_sessions
            WHERE AuditorId IS NULL AND PcId = @pcId AND SessionDate IN ({dateList})
            GROUP BY SessionDate";
        cmd.Parameters.AddWithValue("@pcId", userId);  // userId IS the PersonId == PcId for solo
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var date   = DateOnly.Parse(r.GetString(0));
            var secs   = r.GetInt32(1);
            var dayIdx = dates.IndexOf(date);
            if (dayIdx < 0) continue;
            var key = (userId, dayIdx);
            result[key] = result.GetValueOrDefault(key) + secs;
        }
        return result;
    }

    public List<WeekTotal> GetWeeklyTotalsSolo(int userId, DateOnly latestWeekStart, int weekCount)
    {
        var weeks = Enumerable.Range(0, weekCount)
            .Select(i => latestWeekStart.AddDays(-(weekCount - 1 - i) * 7))
            .ToList();
        var result = weeks.ToDictionary(w => w, _ => 0);

        var startStr = weeks[0].ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT SessionDate, SUM(LengthSeconds + AdminSeconds)
            FROM sess_sessions
            WHERE AuditorId IS NULL AND PcId = @pcId AND SessionDate >= @start
            GROUP BY SessionDate";
        cmd.Parameters.AddWithValue("@pcId",  userId);  // userId IS the PersonId == PcId for solo
        cmd.Parameters.AddWithValue("@start", startStr);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!DateOnly.TryParse(r.GetString(0), out var d)) continue;
            var ws = GetWeekStart(d);
            if (result.ContainsKey(ws)) result[ws] += r.GetInt32(1);
        }

        return weeks.Select(w => new WeekTotal(w, result[w], [])).ToList();
    }

    public int AddSoloSession(
        int personId, DateOnly date,
        int lengthSec, int adminSec, bool isFree, string? summary)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var dateStr = date.ToString("yyyy-MM-dd");

        // BUG check: caller must be a Solo user
        using var bugCmd = conn.CreateCommand();
        bugCmd.CommandText = "SELECT 1 FROM core_users WHERE PersonId = @id AND StaffRole = 'Solo' AND IsActive = 1";
        bugCmd.Parameters.AddWithValue("@id", personId);
        if (bugCmd.ExecuteScalar() is null)
            Console.WriteLine($"BUG!! AddSoloSession called for personId={personId} but no active Solo user in core_users");

        // Ensure person exists in PCs table
        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = "INSERT OR IGNORE INTO core_pcs (PcId) VALUES (@id)";
        pcCmd.Parameters.AddWithValue("@id", personId);
        pcCmd.ExecuteNonQuery();

        using var seqCmd = conn.CreateCommand();
        seqCmd.CommandText = @"
            SELECT COALESCE(MAX(SequenceInDay), 0) + 1
            FROM sess_sessions
            WHERE AuditorId IS NULL AND PcId = @pcId AND SessionDate = @date";
        seqCmd.Parameters.AddWithValue("@pcId", personId);
        seqCmd.Parameters.AddWithValue("@date", dateStr);
        var seq = (long)(seqCmd.ExecuteScalar() ?? 1L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_sessions
              (PcId, AuditorId, SessionDate, SequenceInDay,
               LengthSeconds, AdminSeconds, IsFreeSession,
               ChargeSeconds, ChargedRateCentsPerHour,
               CreatedAt)
            VALUES
              (@pcId, NULL, @date, @seq,
               @len, @adm, @free,
               0, 0,
               datetime('now', '+2 hours'))";
        cmd.Parameters.AddWithValue("@pcId", personId);
        cmd.Parameters.AddWithValue("@date", dateStr);
        cmd.Parameters.AddWithValue("@seq",  seq);
        cmd.Parameters.AddWithValue("@len",  lengthSec);
        cmd.Parameters.AddWithValue("@adm",  adminSec);
        cmd.Parameters.AddWithValue("@free", isFree ? 1 : 0);
        cmd.ExecuteNonQuery();

        using var rowIdCmd = conn.CreateCommand();
        rowIdCmd.CommandText = "SELECT last_insert_rowid()";
        var newSoloId = (int)(long)rowIdCmd.ExecuteScalar()!;
        Console.WriteLine($"[DashboardService] Added solo session {newSoloId} for personId={personId}");
        return newSoloId;
    }

    /// Returns a map of pcId → first name of the CS who last reviewed a session for that PC.
    public Dictionary<string, string> GetCsStatusLabels()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Code, Label FROM lkp_cs_status ORDER BY SortOrder";
        var result = new Dictionary<string, string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result[r.GetString(0)] = r.GetString(1);
        return result;
    }

    public Dictionary<int, string> GetLastCsNamesByPc(List<int> pcIds)
    {
        var result = new Dictionary<int, string>();
        if (pcIds.Count == 0) return result;

        var pcList = string.Join(",", pcIds);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT s.PcId,
                   {FullNameExpr} AS FullName
            FROM (
                SELECT s2.PcId, MAX(cr2.CsReviewId) AS MaxId
                FROM cs_reviews cr2
                JOIN sess_sessions s2 ON s2.SessionId = cr2.SessionId
                WHERE s2.PcId IN ({pcList})
                GROUP BY s2.PcId
            ) latest
            JOIN cs_reviews cr ON cr.CsReviewId = latest.MaxId
            JOIN sess_sessions s   ON s.SessionId   = cr.SessionId
            JOIN core_persons p    ON p.PersonId    = cr.CsId";

        using var r = cmd.ExecuteReader();
        while (r.Read())
            result[r.GetInt32(0)] = r.GetString(1);

        return result;
    }

    public bool HasAnyWorkInWeek(int userId, DateOnly weekStart, List<PcInfo> userPcs, bool soloMode = false)
    {
        var dates = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var dateList = string.Join(",", dates.Select(d => $"'{d:yyyy-MM-dd}'"));

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Solo mode: only solo sessions (AuditorId IS NULL, PcId = solo user's PersonId)
        if (soloMode)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
            SELECT 1
            FROM sess_sessions
            WHERE AuditorId IS NULL AND PcId = @uid AND SessionDate IN ({dateList})
            LIMIT 1";
            cmd.Parameters.AddWithValue("@uid", userId);
            return cmd.ExecuteScalar() is not null;
        }

        // Regular mode: auditor sessions only (CS excluded from graph)
        var auditorPcIds = userPcs.Where(p => p.WorkCapacity == StaffRoles.Auditor).Select(p => p.PcId).ToList();

        if (auditorPcIds.Count > 0)
        {
            var pcList = string.Join(",", auditorPcIds);
            using var sCmd = conn.CreateCommand();
            sCmd.CommandText = $@"
            SELECT 1
            FROM sess_sessions
            WHERE AuditorId = @uid AND PcId IN ({pcList}) AND SessionDate IN ({dateList})
            LIMIT 1";
            sCmd.Parameters.AddWithValue("@uid", userId);
            if (sCmd.ExecuteScalar() is not null) return true;
        }

        return false;
    }

    // ── Admin: Session Approval ──

    /// <param name="includeApproved">When true, returns Draft + Approved sessions.</param>
    /// <param name="from">Inclusive start date. Applied only when includeApproved=true.</param>
    /// <param name="to">Inclusive end date. Applied only when includeApproved=true.</param>
    public List<AuditorSessionGroup> GetSessionsGroupedByAuditorAndPc(
        bool includeApproved = false,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        var where = new System.Text.StringBuilder(
            includeApproved
                ? "s.AuditorId IS NOT NULL AND s.VerifiedStatus IN ('Pending','Approved') AND COALESCE(s.IsImported,0) = 0"
                : "s.AuditorId IS NOT NULL AND s.VerifiedStatus != 'Approved' AND COALESCE(s.IsImported,0) = 0");

        if (includeApproved && from.HasValue)
        {
            where.Append(" AND s.SessionDate >= @from");
            cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd"));
        }
        if (includeApproved && to.HasValue)
        {
            where.Append(" AND s.SessionDate <= @to");
            cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd"));
        }

        cmd.CommandText = $@"
            SELECT s.SessionId, s.PcId,
                   TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')) AS PcName,
                   COALESCE(TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), '')), 'Solo') AS AuditorName,
                   s.SessionDate, s.LengthSeconds, s.AdminSeconds, s.IsFreeSession,
                   s.ChargedRateCentsPerHour, s.AuditorSalaryCentsPerHour,
                   s.VerifiedStatus, COALESCE(s.AuditorId, -s.PcId) AS AuditorKey
            FROM sess_sessions s
            LEFT JOIN core_persons pa ON pa.PersonId = s.AuditorId
            JOIN core_persons pc ON pc.PersonId = s.PcId
            WHERE {where}
            ORDER BY pa.FirstName, pa.LastName, pc.FirstName, pc.LastName, s.SessionDate, s.SequenceInDay";

        var auditorNames = new Dictionary<int, string>();
        var pcNames      = new Dictionary<(int aud, int pc), string>();
        var sessionLists = new Dictionary<(int aud, int pc), List<AdminSessionRow>>();

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var auditorId   = r.GetInt32(11);
            var auditorName = r.GetString(3);
            var pcId        = r.GetInt32(1);
            var pcName      = r.GetString(2);
            var key         = (auditorId, pcId);

            var session = new AdminSessionRow(
                r.GetInt32(0), pcId, pcName, auditorName,
                r.GetString(4),
                r.GetInt32(5), r.GetInt32(6),
                r.GetInt32(7) == 1,
                r.GetInt32(8), r.GetInt32(9),
                r.IsDBNull(10) ? "Pending" : r.GetString(10));

            auditorNames.TryAdd(auditorId, auditorName);
            pcNames.TryAdd(key, pcName);
            if (!sessionLists.ContainsKey(key))
                sessionLists[key] = new List<AdminSessionRow>();
            sessionLists[key].Add(session);
        }

        return auditorNames.Keys.Select(audId =>
        {
            var pcGroups = sessionLists.Keys
                .Where(k => k.aud == audId)
                .Select(k => new PcSessionGroup(k.pc, pcNames[k], sessionLists[k]))
                .ToList();
            return new AuditorSessionGroup(audId, auditorNames[audId], pcGroups);
        }).ToList();
    }

    public List<CsReviewerGroup> GetCsReviewsGroupedByCsAndPc(
        bool includeApproved = false,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        var where = new System.Text.StringBuilder(
            includeApproved ? "COALESCE(s.IsImported,0) = 0" : "cr.Status != 'Approved' AND COALESCE(s.IsImported,0) = 0");

        if (includeApproved && from.HasValue)
        {
            where.Append(" AND s.SessionDate >= @from");
            cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd"));
        }
        if (includeApproved && to.HasValue)
        {
            where.Append(" AND s.SessionDate <= @to");
            cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd"));
        }

        cmd.CommandText = $@"
            SELECT cr.CsReviewId, cr.SessionId, s.PcId,
                   TRIM(pc.FirstName  || ' ' || COALESCE(NULLIF(pc.LastName,''),  '')) AS PcName,
                   cr.CsId,
                   TRIM(pcs.FirstName || ' ' || COALESCE(NULLIF(pcs.LastName,''), '')) AS CsName,
                   s.SessionDate, cr.ReviewLengthSeconds, cr.CsSalaryCentsPerHour, cr.Status,
                   CASE WHEN s.AuditorId IS NULL THEN 1 ELSE 0 END AS IsSolo,
                   COALESCE(cr.ChargedCentsRatePerHour, 0) AS ChargedCentsRatePerHour,
                   cr.Notes
            FROM cs_reviews cr
            JOIN sess_sessions  s   ON s.SessionId   = cr.SessionId
            JOIN core_persons   pc  ON pc.PersonId   = s.PcId
            JOIN core_persons   pcs ON pcs.PersonId  = cr.CsId
            WHERE {where}
            ORDER BY pcs.FirstName, pcs.LastName, pc.FirstName, pc.LastName, s.SessionDate, s.SequenceInDay";

        var csNames  = new Dictionary<int, string>();
        var pcNames  = new Dictionary<(int cs, int pc), string>();
        var revLists = new Dictionary<(int cs, int pc), List<AdminCsRow>>();

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var csId   = r.GetInt32(4);
            var csName = r.GetString(5);
            var pcId   = r.GetInt32(2);
            var pcName = r.GetString(3);
            var key    = (csId, pcId);

            var row = new AdminCsRow(
                r.GetInt32(0), r.GetInt32(1), pcId, pcName, csId, csName,
                r.GetString(6), r.IsDBNull(7) ? 0 : r.GetInt32(7), r.IsDBNull(8) ? 0 : r.GetInt32(8),
                r.IsDBNull(9) ? "Done" : r.GetString(9),
                !r.IsDBNull(10) && r.GetInt32(10) == 1,
                r.IsDBNull(11) ? 0 : r.GetInt32(11),
                r.IsDBNull(12) ? null : r.GetString(12));

            csNames.TryAdd(csId, csName);
            pcNames.TryAdd(key, pcName);
            if (!revLists.ContainsKey(key)) revLists[key] = new List<AdminCsRow>();
            revLists[key].Add(row);
        }

        return csNames.Keys.Select(csId =>
        {
            var pcGroups = revLists.Keys
                .Where(k => k.cs == csId)
                .Select(k => new PcCsGroup(k.pc, pcNames[k], revLists[k]))
                .ToList();
            return new CsReviewerGroup(csId, csNames[csId], pcGroups);
        }).ToList();
    }

    public (int chargeRate, int salary) GetLastApprovedDefaults(int auditorId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ChargedRateCentsPerHour, AuditorSalaryCentsPerHour
            FROM sess_sessions
            WHERE AuditorId = @id AND VerifiedStatus = 'Approved'
            ORDER BY SessionDate DESC, SequenceInDay DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@id", auditorId);
        using var r = cmd.ExecuteReader();
        if (r.Read())
            return (r.GetInt32(0), r.GetInt32(1));
        return (0, 0);
    }

    /// Returns defaults for a specific auditor+PC pair, falling back to any auditor-level default.
    public (int chargeRate, int salary) GetLastApprovedDefaultsForPc(int auditorId, int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Step 1: auditor+PC approved session
        int rate = 0, salary = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT ChargedRateCentsPerHour, AuditorSalaryCentsPerHour
                FROM sess_sessions
                WHERE AuditorId = @aud AND PcId = @pc AND VerifiedStatus = 'Approved'
                ORDER BY SessionDate DESC, SequenceInDay DESC
                LIMIT 1";
            cmd.Parameters.AddWithValue("@aud", auditorId);
            cmd.Parameters.AddWithValue("@pc",  pcId);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { rate = r.GetInt32(0); salary = r.GetInt32(1); }
        }

        // Step 2: fallback to auditor-only approved session
        if (rate == 0 || salary == 0)
        {
            var (defRate, defSal) = GetLastApprovedDefaults(auditorId);
            if (rate == 0) rate = defRate;
            if (salary == 0) salary = defSal;
        }

        // Step 3: fallback for rate only — PC's last auditing purchase
        if (rate == 0)
            rate = GetPcPurchaseRateCents(conn, pcId);

        return (rate, salary);
    }

    private static int GetPcPurchaseRateCents(SqliteConnection conn, int pcId)
    {
        // Budget reset date
        string? resetDate = null;
        using (var rdCmd = conn.CreateCommand())
        {
            rdCmd.CommandText = "SELECT ResetDate FROM fin_budget_reset WHERE PcId = @pc AND IsActive=1 ORDER BY ResetDate DESC LIMIT 1";
            rdCmd.Parameters.AddWithValue("@pc", pcId);
            resetDate = rdCmd.ExecuteScalar() as string;
        }
        var rdFilter = resetDate != null ? " AND pu.PurchaseDate >= @rd" : "";
        var rdFilter2 = resetDate != null ? " AND pu2.PurchaseDate >= @rd" : "";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT SUM(pi.AmountPaid), SUM(pi.HoursBought)
            FROM fin_purchase_items pi
            JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
            WHERE pu.PcId = @pc AND pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'" + rdFilter + @"
              AND pu.PurchaseId = (
                  SELECT pu2.PurchaseId FROM fin_purchases pu2
                  JOIN fin_purchase_items pi2 ON pi2.PurchaseId = pu2.PurchaseId
                  WHERE pu2.PcId = @pc AND pu2.IsDeleted = 0 AND pi2.ItemType = 'Auditing'" + rdFilter2 + @"
                  GROUP BY pu2.PurchaseId HAVING SUM(pi2.AmountPaid) <> 0
                  ORDER BY pu2.PurchaseId DESC LIMIT 1
              )";
        cmd.Parameters.AddWithValue("@pc", pcId);
        if (resetDate != null) cmd.Parameters.AddWithValue("@rd", resetDate);
        using var r = cmd.ExecuteReader();
        if (r.Read() && !r.IsDBNull(0) && !r.IsDBNull(1))
        {
            double hrs = r.GetDouble(1);
            if (Math.Abs(hrs) > 0) return (int)Math.Round(Math.Abs((double)r.GetInt32(0) / hrs)) * 100;
        }
        return 0;
    }

    /// Returns the CsSalaryCentsPerHour from the last approved CS review for a given PC.
    public int GetLastApprovedCsSalaryForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT cr.CsSalaryCentsPerHour
            FROM cs_reviews cr
            JOIN sess_sessions s ON s.SessionId = cr.SessionId
            WHERE s.PcId = @pc AND cr.Status = 'Done'
            ORDER BY s.SessionDate DESC, s.SequenceInDay DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : 0;
    }

    /// Returns the currency from the last auditing purchase for a PC (e.g. "ILS", "USD", "EUR").
    public string GetLastPurchaseCurrency(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        string? resetDate = null;
        using (var rdCmd = conn.CreateCommand())
        {
            rdCmd.CommandText = "SELECT ResetDate FROM fin_budget_reset WHERE PcId = @pc AND IsActive=1 ORDER BY ResetDate DESC LIMIT 1";
            rdCmd.Parameters.AddWithValue("@pc", pcId);
            resetDate = rdCmd.ExecuteScalar() as string;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(pu.Currency, 'ILS')
            FROM fin_purchases pu
            JOIN fin_purchase_items pi ON pi.PurchaseId = pu.PurchaseId
            WHERE pu.PcId = @pc AND pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'"
            + (resetDate != null ? " AND pu.PurchaseDate >= @rd" : "") + @"
            GROUP BY pu.PurchaseId
            HAVING SUM(pi.AmountPaid) <> 0
            ORDER BY pu.PurchaseId DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@pc", pcId);
        if (resetDate != null) cmd.Parameters.AddWithValue("@rd", resetDate);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : "ILS";
    }

    /// Returns the last ChargedCentsRatePerHour from a solo CS review for a given PC.
    public int GetLastSoloCsRateForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        string? resetDate = null;
        using (var rdCmd = conn.CreateCommand())
        {
            rdCmd.CommandText = "SELECT ResetDate FROM fin_budget_reset WHERE PcId = @pc AND IsActive=1 ORDER BY ResetDate DESC LIMIT 1";
            rdCmd.Parameters.AddWithValue("@pc", pcId);
            resetDate = rdCmd.ExecuteScalar() as string;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT cr.ChargedCentsRatePerHour
            FROM cs_reviews cr
            JOIN sess_sessions s ON s.SessionId = cr.SessionId
            WHERE s.PcId = @pc AND s.AuditorId IS NULL AND cr.ChargedCentsRatePerHour > 0"
            + (resetDate != null ? " AND s.SessionDate >= @rd" : "") + @"
            ORDER BY s.SessionDate DESC, s.SequenceInDay DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pc", pcId);
        if (resetDate != null) cmd.Parameters.AddWithValue("@rd", resetDate);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : 0;
    }

    public void ApproveSession(int sessionId, int chargedRateCents, int auditorSalaryCents, int verifiedByUserId = 0)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_sessions
            SET VerifiedStatus = 'Approved',
                ChargedRateCentsPerHour = @rate,
                AuditorSalaryCentsPerHour = @salary,
                ChargeSeconds = CASE WHEN IsFreeSession = 1 THEN 0 ELSE LengthSeconds + AdminSeconds END,
                VerifiedByUserId = @verifier,
                VerifiedAt = @verifiedAt
            WHERE SessionId = @id";
        cmd.Parameters.AddWithValue("@rate",       chargedRateCents);
        cmd.Parameters.AddWithValue("@salary",     auditorSalaryCents);
        cmd.Parameters.AddWithValue("@verifier",   verifiedByUserId > 0 ? (object)verifiedByUserId : DBNull.Value);
        cmd.Parameters.AddWithValue("@verifiedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@id",         sessionId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Approved session {sessionId} by userId={verifiedByUserId}");
    }

    public void ApproveCsReview(int csReviewId, int csSalaryCents, int chargedRateCents = 0)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE cs_reviews
            SET Status = 'Approved', CsSalaryCentsPerHour = @salary, ChargedCentsRatePerHour = @rate
            WHERE CsReviewId = @id";
        cmd.Parameters.AddWithValue("@salary", csSalaryCents);
        cmd.Parameters.AddWithValue("@rate",   chargedRateCents);
        cmd.Parameters.AddWithValue("@id",     csReviewId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Approved CS review {csReviewId}, chargedRate={chargedRateCents}");
    }

    public void ReassignCsReview(int csReviewId, int newCsId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE cs_reviews SET CsId = @cs WHERE CsReviewId = @id";
        cmd.Parameters.AddWithValue("@cs", newCsId);
        cmd.Parameters.AddWithValue("@id", csReviewId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Reassigned CS review {csReviewId} to CsId={newCsId}");
    }

    public void UpdateSessionDate(int sessionId, string newDate)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sess_sessions SET SessionDate = @d WHERE SessionId = @id";
        cmd.Parameters.AddWithValue("@d", newDate);
        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Updated session {sessionId} date to {newDate}");
    }

    /// Returns session IDs that have a cs_review for a given PC (filtered to solo or non-solo).
    public HashSet<int> GetCsedSessionIdsForPc(int pcId, bool isSolo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var soloFilter = isSolo ? "AND s.AuditorId IS NULL" : "AND s.AuditorId IS NOT NULL";
        cmd.CommandText = $@"
            SELECT cr.SessionId FROM cs_reviews cr
            JOIN sess_sessions s ON s.SessionId = cr.SessionId
            WHERE s.PcId = @pcId {soloFilter}";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var set = new HashSet<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            set.Add(r.GetInt32(0));
        return set;
    }

    /// Returns all sessions for a PC: (SessionId, Name), filtered to solo or non-solo.
    public List<(int SessionId, string Name, string CreatedAt)> GetSessionsForPc(int pcId, bool isSolo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var soloFilter = isSolo ? "AND AuditorId IS NULL" : "AND AuditorId IS NOT NULL";
        cmd.CommandText = $@"
            SELECT SessionId, COALESCE(Name,''), COALESCE(CreatedAt,'') FROM sess_sessions
            WHERE PcId = @pcId {soloFilter}";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<(int, string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public record SessionSummaryInfo(string Name, string SessionDate, string? SummaryHtml, int LengthSeconds, int AdminSeconds);
    public record SessionSummaryEditInfo(int SummaryId, int SessionId, string Name, string SessionDate, string CreatedAt, string? SummaryHtml, int LengthSeconds, int AdminSeconds, string AuditorName);

    public List<SessionSummaryInfo> GetSessionSummariesForPc(int pcId, bool isSolo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var soloFilter = isSolo ? "AND fs.AuditorId IS NULL" : "AND fs.AuditorId IS NOT NULL";
        cmd.CommandText = $@"
            SELECT COALESCE(s.Name, ''),
                   COALESCE(s.SessionDate, SUBSTR(fs.CreatedAt, 1, 10)),
                   fs.SummaryHtml,
                   COALESCE(s.LengthSeconds, 0), COALESCE(s.AdminSeconds, 0)
            FROM sess_folder_summary fs
            LEFT JOIN sess_sessions s ON s.SessionId = fs.SessionId
            WHERE fs.PcId = @pcId AND fs.SummaryHtml IS NOT NULL AND fs.SummaryHtml != ''
            {soloFilter}
            ORDER BY fs.CreatedAt DESC";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<SessionSummaryInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SessionSummaryInfo(r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3), r.GetInt32(4)));
        return list;
    }

    public (int id, string html, string? arfJson)? GetFolderSummaryBySession(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, SummaryHtml, ArfJson FROM sess_folder_summary WHERE SessionId = @sid LIMIT 1";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        using var r = cmd.ExecuteReader();
        if (r.Read()) return (r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2));
        return null;
    }

    public List<SessionSummaryEditInfo> GetSessionSummariesForPcEdit(int pcId, bool isSolo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var soloFilter = isSolo ? "AND fs.AuditorId IS NULL" : "AND fs.AuditorId IS NOT NULL";
        cmd.CommandText = $@"
            SELECT fs.Id, COALESCE(s.SessionId, 0),
                   COALESCE(s.Name, ''),
                   COALESCE(s.SessionDate, SUBSTR(fs.CreatedAt, 1, 10)),
                   COALESCE(fs.CreatedAt, ''),
                   fs.SummaryHtml,
                   COALESCE(s.LengthSeconds, 0), COALESCE(s.AdminSeconds, 0),
                   COALESCE(TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')), '')
            FROM sess_folder_summary fs
            LEFT JOIN sess_sessions s ON s.SessionId = fs.SessionId
            LEFT JOIN core_persons p ON p.PersonId = fs.AuditorId
            WHERE fs.PcId = @pcId AND fs.SummaryHtml IS NOT NULL AND fs.SummaryHtml != ''
            {soloFilter}
            ORDER BY fs.CreatedAt DESC";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<SessionSummaryEditInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SessionSummaryEditInfo(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetInt32(6), r.GetInt32(7), r.GetString(8)));
        return list;
    }

    public void UpdateFolderSummary(int id, string html)
    {
        // Normalize: if text content is empty, store a single space so the row remains visible
        var stripped = System.Text.RegularExpressions.Regex.Replace(html ?? "", "<[^>]+>", "").Trim();
        if (string.IsNullOrEmpty(stripped)) html = " ";

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sess_folder_summary SET SummaryHtml = @html WHERE Id = @id";
        cmd.Parameters.AddWithValue("@html", html);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void AddFolderSummary(int? sessionId, int pcId, int? auditorId, string summaryHtml, string? arfJson = null, string? sessionDate = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_folder_summary (SessionId, PcId, AuditorId, SummaryHtml, CreatedAt, ArfJson)
            VALUES (@sid, @pcId, @audId, @html, @createdAt, @arfJson)";
        cmd.Parameters.AddWithValue("@sid", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@audId", (object?)auditorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@html", summaryHtml);
        cmd.Parameters.AddWithValue("@createdAt", sessionDate ?? DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@arfJson", (object?)arfJson ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// Returns the NextCS html from the most recent session of the given PC (excluding the current session being created).
    public string? GetLastArfGrade(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT fs.ArfJson
            FROM sess_folder_summary fs
            JOIN sess_sessions s ON s.SessionId = fs.SessionId
            WHERE s.PcId = @pcId AND fs.ArfJson IS NOT NULL AND fs.ArfJson != ''
            ORDER BY s.SessionDate DESC, s.SessionId DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var json = cmd.ExecuteScalar() as string;
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var arf = System.Text.Json.JsonSerializer.Deserialize<PdfService.ArfData>(json);
            return string.IsNullOrWhiteSpace(arf?.Grade) ? null : arf.Grade;
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns the full name of the CS assigned to the given PC (last entry in sys_staff_pc_list).
    /// </summary>
    public string? GetCsNameForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT {FullNameExpr} AS FullName
            FROM sys_staff_pc_list spl
            JOIN core_persons p ON p.PersonId = spl.UserId
            WHERE spl.PcId = @pcId AND spl.WorkCapacity IN ('{StaffRoles.CS}','{StaffRoles.SeniorCS}')
            ORDER BY spl.Id DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var result = cmd.ExecuteScalar();
        return result is string s && !string.IsNullOrWhiteSpace(s) ? s : null;
    }

    public string? GetLastNextCs(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT nc.NextCS
            FROM sess_next_cs nc
            JOIN sess_sessions s ON s.SessionId = nc.SessionId
            WHERE s.PcId = @pcId AND nc.NextCS != ''
            ORDER BY s.SessionDate DESC, s.SessionId DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : null;
    }

    public void SaveNextCs(int sessionId, string nextCsHtml)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM sess_next_cs WHERE SessionId = @sid";
        del.Parameters.AddWithValue("@sid", sessionId);
        del.ExecuteNonQuery();

        using var ins = conn.CreateCommand();
        ins.CommandText = @"
            INSERT INTO sess_next_cs (SessionId, NextCS, UpdatedAt)
            VALUES (@sid, @html, datetime('now', '+2 hours'))";
        ins.Parameters.AddWithValue("@sid", sessionId);
        ins.Parameters.AddWithValue("@html", nextCsHtml);
        ins.ExecuteNonQuery();
    }

    public bool SessionBelongsToPc(int sessionId, int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sess_sessions WHERE SessionId = @sid AND PcId = @pcId LIMIT 1";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        return cmd.ExecuteScalar() is not null;
    }

    public (int SessionId, string Name)? GetPreviousSession(int pcId, int currentSessionId, bool isSolo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var soloFilter = isSolo ? "AND AuditorId IS NULL" : "AND AuditorId IS NOT NULL";
        cmd.CommandText = $@"
            SELECT SessionId, COALESCE(Name,'')
            FROM sess_sessions
            WHERE PcId = @pcId AND SessionId < @sid {soloFilter}
            ORDER BY SessionDate DESC, SessionId DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@sid", currentSessionId);
        using var r = cmd.ExecuteReader();
        if (r.Read()) return (r.GetInt32(0), r.GetString(1));
        return null;
    }

    public string GetLastAuditorLabel(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT {FullNameExpr}
            FROM sess_sessions s
            JOIN core_persons p ON p.PersonId = s.AuditorId
            WHERE s.PcId = @pcId AND s.AuditorId IS NOT NULL
            ORDER BY s.SessionDate DESC, s.SessionId DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var name = cmd.ExecuteScalar() as string;
        if (name != null) return name;
        // Check if any sessions exist at all
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT 1 FROM sess_sessions WHERE PcId = @pcId LIMIT 1";
        cmd2.Parameters.AddWithValue("@pcId", pcId);
        return cmd2.ExecuteScalar() != null ? "Unassigned" : "No sessions yet";
    }

    /// <summary>Returns the Free/Bill status of the most recent solo CS review for this PC.
    /// True = free (default when no previous review exists).</summary>
    public bool GetPreviousCsFreeStatus(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(cr.Notes, '')
            FROM cs_reviews cr
            JOIN sess_sessions s ON s.SessionId = cr.SessionId
            WHERE s.PcId = @pcId AND s.AuditorId IS NULL
            ORDER BY s.SessionDate DESC, s.SessionId DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var result = cmd.ExecuteScalar() as string;
        // No previous review → default free; 'Bill' → not free; anything else → free
        return result != "Bill";
    }

    public string GetSessionDate(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT SessionDate FROM sess_sessions WHERE SessionId = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        return cmd.ExecuteScalar() as string ?? DateTime.Today.ToString("yyyy-MM-dd");
    }

    public bool GetSessionFreeFlag(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IsFreeSession FROM sess_sessions WHERE SessionId = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        var result = cmd.ExecuteScalar();
        return result is long l && l == 1;
    }

    public void UpdateSessionFreeFlag(int sessionId, bool isFree)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sess_sessions SET IsFreeSession = @free WHERE SessionId = @sid";
        cmd.Parameters.AddWithValue("@free", isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Updated IsFreeSession={isFree} for session {sessionId}");
    }

    public string? GetSessionName(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name FROM sess_sessions WHERE SessionId = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        return cmd.ExecuteScalar() as string;
    }

    public string? GetSessionAuditorName(int pcId, string sessionFileName)
    {
        var name = Path.GetFileNameWithoutExtension(sessionFileName);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), ''))
            FROM sess_sessions s
            JOIN core_persons p ON p.PersonId = COALESCE(NULLIF(s.AuditorId, 0), s.PcId)
            WHERE s.PcId = @pcId AND s.Name = @name
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@name", name);
        return cmd.ExecuteScalar() as string;
    }

    public int GetSessionTotalSeconds(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(LengthSeconds,0) + COALESCE(AdminSeconds,0) FROM sess_sessions WHERE SessionId = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        var result = cmd.ExecuteScalar();
        return result is long v ? (int)v : 0;
    }

    public bool HasCsReview(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM cs_reviews WHERE SessionId = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public (int CsReviewId, int ReviewSec)? GetCsReviewInfo(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CsReviewId, ReviewLengthSeconds FROM cs_reviews WHERE SessionId = @sid LIMIT 1";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt32(0), r.GetInt32(1)) : null;
    }

    public string? GetCsReviewNotes(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Notes FROM cs_reviews WHERE SessionId = @sid LIMIT 1";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        return cmd.ExecuteScalar() as string;
    }

    public void UpdateCsReviewNotes(int csReviewId, string? notes)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE cs_reviews SET Notes = @notes WHERE CsReviewId = @id";
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", csReviewId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Updated notes for CsReviewId={csReviewId} to '{notes}'");
    }

    public void UpdateCsReviewTime(int csReviewId, int reviewSec)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE cs_reviews SET ReviewLengthSeconds = @rev WHERE CsReviewId = @id";
        cmd.Parameters.AddWithValue("@rev", reviewSec);
        cmd.Parameters.AddWithValue("@id", csReviewId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Updated review time for CsReviewId={csReviewId} to {reviewSec}s");
    }

    public record PendingCsSession(
        int SessionId, int PcId, string PcName, string SessionName,
        string SessionDate, int TotalSeconds, bool IsSolo, string AuditorName, string CreatedAt,
        bool IsPendingApproval = false, string CsStatus = "");

    /// Returns sessions from the last <paramref name="lookbackDays"/> days,
    /// limited to PCs where <paramref name="csUserId"/> has an approved CS work-capacity assignment.
    /// When <paramref name="includeDone"/> is false (default), only sessions without a CS review are returned.
    public List<PendingCsSession> GetPendingCsSessions(int csUserId, int lookbackDays = 10, bool includeDone = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var dateFilter  = lookbackDays > 0 ? "AND s.SessionDate >= @cutoff" : "";
        var doneFilter  = includeDone ? "" : "AND (cr.CsReviewId IS NULL OR EXISTS (SELECT 1 FROM sess_questions sq WHERE sq.SessionId = s.SessionId AND sq.Status IN ('Pending','Replied')))";
        cmd.CommandText = $@"
            SELECT s.SessionId, s.PcId,
                   {FullNameExpr} AS PcName,
                   COALESCE(s.Name, '') AS SessionName,
                   s.SessionDate,
                   COALESCE(s.LengthSeconds,0) + COALESCE(s.AdminSeconds,0) AS TotalSec,
                   CASE WHEN s.AuditorId IS NULL THEN 1 ELSE 0 END AS IsSolo,
                   COALESCE(TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), '')), 'Solo') AS AuditorName,
                   COALESCE(s.CreatedAt, '') AS CreatedAt,
                   spl.IsApproved AS IsApproved,
                   COALESCE(cr.Status, '') AS CsStatus
            FROM sess_sessions s
            JOIN core_persons p ON p.PersonId = s.PcId
            JOIN sys_staff_pc_list spl ON spl.PcId = s.PcId
                AND spl.UserId = @csUserId
                AND spl.IsApproved IN (0,1)
                AND spl.WorkCapacity = '{StaffRoles.CS}'
            LEFT JOIN core_persons pa ON pa.PersonId = s.AuditorId
            LEFT JOIN cs_reviews cr ON cr.SessionId = s.SessionId
            WHERE s.IsImported = 0
              {doneFilter}
              {dateFilter}
            ORDER BY spl.IsApproved DESC, cr.CsReviewId IS NULL DESC, PcName, s.SessionDate, s.SequenceInDay";
        cmd.Parameters.AddWithValue("@csUserId", csUserId);
        if (lookbackDays > 0)
            cmd.Parameters.AddWithValue("@cutoff",
                DateOnly.FromDateTime(DateTime.Today.AddDays(-lookbackDays)).ToString("yyyy-MM-dd"));
        var list = new List<PendingCsSession>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PendingCsSession(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.IsDBNull(5) ? 0 : r.GetInt32(5),
                r.GetInt32(6) == 1, r.GetString(7), r.GetString(8),
                IsPendingApproval: r.GetInt32(9) == 0,
                CsStatus: r.GetString(10)));
        }
        return list;
    }

    // ── Auditor Session Status (My Auditing Status) ────────────

    public record AuditorSessionStatus(
        int SessionId, int PcId, string PcName, string SessionName,
        string SessionDate, int TotalSeconds, string CsStatus, string? CsName, string CreatedAt,
        bool IsPendingApproval = false, bool IsSessionVerified = false);

    /// <summary>
    /// Returns sessions the auditor conducted in the last <paramref name="lookbackDays"/> days
    /// with their CS review status: Waiting / Done / Approved / Correction.
    /// </summary>
    public List<AuditorSessionStatus> GetAuditorSessionStatuses(int auditorId, int lookbackDays = 10, bool isSolo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var dateFilter = lookbackDays > 0
            ? "AND s.SessionDate >= @cutoff" : "";
        if (isSolo)
        {
            // Solo: sessions are on their own PcId, no permission check needed
            cmd.CommandText = $@"
                SELECT s.SessionId, s.PcId,
                       {FullNameExpr} AS PcName,
                       COALESCE(s.Name, '') AS SessionName,
                       s.SessionDate,
                       COALESCE(s.LengthSeconds,0) + COALESCE(s.AdminSeconds,0) AS TotalSec,
                       CASE WHEN cr.CsReviewId IS NULL THEN 'Waiting' ELSE cr.Status END AS CsStatus,
                       TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')) AS CsName,
                       COALESCE(s.CreatedAt, '') AS CreatedAt,
                       1 AS IsApproved,
                       CASE WHEN s.VerifiedStatus = 'Approved' THEN 1 ELSE 0 END AS IsSessionVerified
                FROM sess_sessions s
                JOIN core_persons p ON p.PersonId = s.PcId
                LEFT JOIN cs_reviews cr ON cr.SessionId = s.SessionId
                LEFT JOIN core_persons pc ON pc.PersonId = cr.CsId
                WHERE s.PcId = @aid AND s.AuditorId IS NULL AND s.IsImported = 0
                  {dateFilter}
                ORDER BY PcName, s.SessionDate, s.SequenceInDay";
        }
        else
        {
            // Regular auditor: join sys_staff_pc_list (WorkCapacity=Auditor), include pending rows
            cmd.CommandText = $@"
                SELECT s.SessionId, s.PcId,
                       {FullNameExpr} AS PcName,
                       COALESCE(s.Name, '') AS SessionName,
                       s.SessionDate,
                       COALESCE(s.LengthSeconds,0) + COALESCE(s.AdminSeconds,0) AS TotalSec,
                       CASE WHEN cr.CsReviewId IS NULL THEN 'Waiting' ELSE cr.Status END AS CsStatus,
                       TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')) AS CsName,
                       COALESCE(s.CreatedAt, '') AS CreatedAt,
                       spl.IsApproved AS IsApproved,
                       CASE WHEN s.VerifiedStatus = 'Approved' THEN 1 ELSE 0 END AS IsSessionVerified
                FROM sess_sessions s
                JOIN core_persons p ON p.PersonId = s.PcId
                JOIN sys_staff_pc_list spl ON spl.PcId = s.PcId
                    AND spl.UserId = @aid
                    AND spl.WorkCapacity = '{StaffRoles.Auditor}'
                    AND spl.IsApproved IN (0,1)
                LEFT JOIN cs_reviews cr ON cr.SessionId = s.SessionId
                LEFT JOIN core_persons pc ON pc.PersonId = cr.CsId
                WHERE s.AuditorId = @aid AND s.IsImported = 0
                  {dateFilter}
                ORDER BY spl.IsApproved DESC, PcName, s.SessionDate, s.SequenceInDay";
        }
        cmd.Parameters.AddWithValue("@aid", auditorId);
        if (lookbackDays > 0)
            cmd.Parameters.AddWithValue("@cutoff",
                DateOnly.FromDateTime(DateTime.Today.AddDays(-lookbackDays)).ToString("yyyy-MM-dd"));
        var list = new List<AuditorSessionStatus>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new AuditorSessionStatus(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.IsDBNull(5) ? 0 : r.GetInt32(5),
                r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetString(8),
                IsPendingApproval: r.GetInt32(9) == 0,
                IsSessionVerified: r.GetInt32(10) == 1));
        }
        return list;
    }

    /// <summary>Returns whether the user has AllowAll=1 in core_users (looked up by unique username).</summary>
    public bool GetAllowAllFlag(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(AllowAll,0) FROM core_users WHERE LOWER(Username) = LOWER(@user) AND IsActive = 1";
        cmd.Parameters.AddWithValue("@user", username);
        return cmd.ExecuteScalar() is long aa && aa == 1;
    }

    // ── Staff Messaging ─────────────────────────────────────────

    public List<StaffMember> GetActiveStaffMembers()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT DISTINCT p.PersonId, {FullNameExpr} AS FullName
            FROM core_persons p
            JOIN core_users u ON u.PersonId = p.PersonId
            WHERE u.IsActive = 1 AND (u.StaffRole != 'None' OR u.UserType = 'Admin')
            ORDER BY p.FirstName, p.LastName";
        var list = new List<StaffMember>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StaffMember(r.GetInt32(0), r.GetString(1)));

        return list;
    }

    public List<StaffMember> GetNonSoloStaffMembers()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT DISTINCT p.PersonId, {FullNameExpr} AS FullName
            FROM core_persons p
            JOIN core_users u ON u.PersonId = p.PersonId
            WHERE u.IsActive = 1 AND u.StaffRole NOT IN ('Solo', 'None')
            ORDER BY p.FirstName, p.LastName";
        var list = new List<StaffMember>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StaffMember(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public void SendMessage(int fromStaffId, int toStaffId, string msgText)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sys_staff_messages (FromStaffId, ToStaffId, MsgText)
            VALUES (@from, @to, @msg)";
        cmd.Parameters.AddWithValue("@from", fromStaffId);
        cmd.Parameters.AddWithValue("@to",   toStaffId);
        cmd.Parameters.AddWithValue("@msg",  msgText);
        cmd.ExecuteNonQuery();
        _messageNotifier.NotifyNewMessage(toStaffId);
        Console.WriteLine($"[DashboardService] Sent message from {fromStaffId} to {toStaffId}");
    }

    public List<StaffMessage> GetPendingMessages(int staffId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT m.Id, m.FromStaffId,
                   {FullNameExpr.Replace("p.", "pf.")} AS FromName,
                   m.ToStaffId,
                   '' AS ToName,
                   m.MsgText, m.CreatedAt, m.AcknowledgedAt
            FROM sys_staff_messages m
            JOIN core_persons pf ON pf.PersonId = m.FromStaffId
            WHERE m.ToStaffId = @id AND m.AcknowledgedAt IS NULL
            ORDER BY m.CreatedAt DESC";
        cmd.Parameters.AddWithValue("@id", staffId);
        var list = new List<StaffMessage>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StaffMessage(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2),
                r.GetInt32(3), r.GetString(4), r.GetString(5),
                r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7)));
        return list;
    }

    public int GetPendingMessageCount(int staffId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys_staff_messages WHERE ToStaffId = @id AND AcknowledgedAt IS NULL";
        cmd.Parameters.AddWithValue("@id", staffId);
        return (int)(long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void AcknowledgeMessage(int messageId, int toStaffId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Ownership check: only acknowledge if the message belongs to toStaffId
        cmd.CommandText = "UPDATE sys_staff_messages SET AcknowledgedAt = datetime('now', '+2 hours') WHERE Id = @id AND ToStaffId = @toId";
        cmd.Parameters.AddWithValue("@id", messageId);
        cmd.Parameters.AddWithValue("@toId", toStaffId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Acknowledged message {messageId} for staffId={toStaffId}");
    }

    // ── Weekly Remarks ───────────────────────────────────────────────

    public string? GetWeeklyRemarks(int auditorId, string weekDate)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Remarks FROM sys_weekly_remarks WHERE AuditorId = @aud AND WeekDate = @wk";
        cmd.Parameters.AddWithValue("@aud", auditorId);
        cmd.Parameters.AddWithValue("@wk", weekDate);
        return cmd.ExecuteScalar() as string;
    }

    public void SaveWeeklyRemarks(int auditorId, string weekDate, string remarks)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sys_weekly_remarks (AuditorId, WeekDate, Remarks, SubmittedAt)
            VALUES (@aud, @wk, @rem, datetime('now', '+2 hours'))
            ON CONFLICT(AuditorId, WeekDate)
            DO UPDATE SET Remarks = @rem, SubmittedAt = datetime('now', '+2 hours')";
        cmd.Parameters.AddWithValue("@aud", auditorId);
        cmd.Parameters.AddWithValue("@wk", weekDate);
        cmd.Parameters.AddWithValue("@rem", remarks ?? "");
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DashboardService] Saved weekly remarks for auditor {auditorId}, week {weekDate}");
    }

    // ── Commission Calculation ────────────────────────────────────────────────

    private static (decimal NetBase, decimal Vat, decimal Cc, decimal NetAfter, decimal Reserve)
        CalcNetBase(decimal gross, string methodType, FinancialConfig cfg, decimal bookPrice = 0m)
    {
        var vat = gross - (gross / (1m + (decimal)cfg.VatPct / 100m));
        var cc = methodType == "CreditCard"
            ? gross * ((decimal)cfg.CcCommissionPct / 100m) : 0m;
        var netAfter = gross - vat - cc - bookPrice;
        var reserve = netAfter * ((decimal)cfg.ReserveDeductPct / 100m);
        return (netAfter - reserve, vat, cc, netAfter, reserve);
    }

    private static long ToCents(decimal shekels) => (long)Math.Round(shekels * 100m);

    private (Dictionary<int, List<CommissionRow>>, List<UnassignedReferralRow>)
        GetCommissionData(SqliteConnection conn, string fromStr, string toStr)
    {
        var commByUser = new Dictionary<int, List<CommissionRow>>();
        var unassigned = new List<UnassignedReferralRow>();

        // ── Load financial config ──
        FinancialConfig cfg;
        using (var cfgCmd = conn.CreateCommand())
        {
            cfgCmd.CommandText = @"
                SELECT VatPct, CcCommissionPct, AuditRegistrarPct, CourseRegistrarPct,
                       AuditReferralPct, CourseReferralPct, ReserveDeductPct,
                       COALESCE(AcademyInstructorIds,''), InstructorOtPct, CsOtPct
                FROM sys_financial_config WHERE Id = 1";
            using var r = cfgCmd.ExecuteReader();
            cfg = r.Read()
                ? new FinancialConfig(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3),
                    r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetString(7),
                    r.GetDouble(8), r.GetDouble(9))
                : new FinancialConfig(17, 2.5, 10, 10, 5, 5, 0.1, "", 0, 0);
        }
        var academyInstructors = InstructorConfig.ParseList(cfg.AcademyInstructorIds);

        // ── Step A: Qualifying in-range payments ──
        var payments = new List<(int PaymentMethodId, int PurchaseId, int Amount, string MethodType,
            int PcId, int? RegistrarId, int? ReferralId, string? Notes, string PcName,
            string PurchaseDate, string PaymentDate)>();
        var purchaseIds = new HashSet<int>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT pm.PaymentMethodId, pm.PurchaseId, pm.Amount, pm.MethodType,
                       p.PcId, p.RegistrarId, p.ReferralId, p.Notes,
                       TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')) AS PcName,
                       p.PurchaseDate, COALESCE(pm.PaymentDate,'')
                FROM fin_payment_methods pm
                JOIN fin_purchases p ON pm.PurchaseId = p.PurchaseId
                JOIN core_persons pc ON pc.PersonId = p.PcId
                WHERE pm.IsMoneyInBank = 1
                  AND pm.MoneyInBankDate >= @from AND pm.MoneyInBankDate <= @to
                  AND pm.MethodType NOT IN ('Credit', 'ToBePaid')
                  AND p.IsDeleted = 0
                  AND p.TransferPurchaseId IS NULL
                  AND p.ApprovedStatus = 'Approved'";
            cmd.Parameters.AddWithValue("@from", fromStr);
            cmd.Parameters.AddWithValue("@to", toStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var pid = r.GetInt32(1);
                payments.Add((r.GetInt32(0), pid, r.GetInt32(2), r.GetString(3),
                    r.GetInt32(4),
                    r.IsDBNull(5) ? null : r.GetInt32(5),
                    r.IsDBNull(6) ? null : r.GetInt32(6),
                    r.IsDBNull(7) ? null : r.GetString(7),
                    r.GetString(8), r.GetString(9), r.GetString(10)));
                purchaseIds.Add(pid);
            }
        }
        if (payments.Count == 0 && academyInstructors.Count == 0)
            return (commByUser, unassigned);

        // ── Step B: Batch-load items for those purchases ──
        var itemsByPurchase = new Dictionary<int, List<(int PurchaseItemId, string ItemType, int? CourseId, int AmountPaid, string CourseType, string ItemName, decimal BookPrice)>>();
        if (purchaseIds.Count > 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT pi.PurchaseId, pi.PurchaseItemId, pi.ItemType, pi.CourseId, pi.AmountPaid,
                       COALESCE(lc.CourseType, 'Academy') AS CourseType,
                       CASE pi.ItemType
                         WHEN 'Course' THEN COALESCE(lc.Name, 'Course')
                         WHEN 'Book' THEN COALESCE(lb.Name, 'Book')
                         ELSE 'Auditing'
                       END AS ItemName,
                       COALESCE(lc.BookPrice, 0) AS BookPrice
                FROM fin_purchase_items pi
                LEFT JOIN lkp_courses lc ON pi.CourseId = lc.CourseId
                LEFT JOIN lkp_books lb ON pi.BookId = lb.BookId
                WHERE pi.PurchaseId IN ({string.Join(",", purchaseIds)})";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var pid = r.GetInt32(0);
                if (!itemsByPurchase.ContainsKey(pid)) itemsByPurchase[pid] = new();
                itemsByPurchase[pid].Add((r.GetInt32(1), r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetInt32(3), r.GetInt32(4), r.GetString(5), r.GetString(6),
                    r.GetDecimal(7)));
            }
        }

        // ── Step C: Batch-load enrollment finish status ──
        // Collect all (PcId, CourseId) pairs from course items
        var courseKeys = new HashSet<(int PcId, int CourseId)>();
        foreach (var pmt in payments)
        {
            if (!itemsByPurchase.TryGetValue(pmt.PurchaseId, out var items)) continue;
            foreach (var item in items)
                if (item.ItemType == "Course" && item.CourseId.HasValue)
                    courseKeys.Add((pmt.PcId, item.CourseId.Value));
        }

        var enrollments = new Dictionary<(int PcId, int CourseId), (string DateFinished, int? InstructorId, int? CsId)>();
        if (courseKeys.Count > 0)
        {
            // Build OR clauses for each (PersonId, CourseId) pair
            var orClausesOuter = string.Join(" OR ", courseKeys.Select(k => $"(sc.PersonId = {k.PcId} AND sc.CourseId = {k.CourseId})"));
            var orClausesInner = string.Join(" OR ", courseKeys.Select(k => $"(PersonId = {k.PcId} AND CourseId = {k.CourseId})"));
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT sc.PersonId, sc.CourseId, sc.DateFinished, sc.InstructorId, sc.CsId
                FROM acad_student_courses sc
                INNER JOIN (
                    SELECT PersonId, CourseId, MAX(DateFinished) AS MDF
                    FROM acad_student_courses
                    WHERE DateFinished IS NOT NULL AND ({orClausesInner})
                    GROUP BY PersonId, CourseId
                ) latest ON sc.PersonId = latest.PersonId
                         AND sc.CourseId = latest.CourseId
                         AND sc.DateFinished = latest.MDF
                WHERE {orClausesOuter}";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = (r.GetInt32(0), r.GetInt32(1));
                if (!enrollments.ContainsKey(key))
                    enrollments[key] = (r.GetString(2),
                        r.IsDBNull(3) ? null : r.GetInt32(3),
                        r.IsDBNull(4) ? null : r.GetInt32(4));
            }
        }

        // ── Helper to emit a commission ──
        void Emit(int personId, string pcName, string role, string category, decimal netBase, double pct, CommissionDetail detail)
        {
            var cents = ToCents(netBase * (decimal)pct / 100m);
            if (cents == 0) return;
            if (!commByUser.ContainsKey(personId)) commByUser[personId] = new();
            commByUser[personId].Add(new CommissionRow(pcName, role, category, cents, detail with { CommissionPct = pct }));
        }

        void EmitUnassigned(string pcName, string category, decimal netBase, double pct, string? notes, CommissionDetail detail)
        {
            var cents = ToCents(netBase * (decimal)pct / 100m);
            if (cents == 0) return;
            unassigned.Add(new UnassignedReferralRow(pcName, category, cents, notes ?? "", detail with { CommissionPct = pct }));
        }

        // ── Step E: Process in-range payments (normal flow) ──
        foreach (var pmt in payments)
        {
            if (!itemsByPurchase.TryGetValue(pmt.PurchaseId, out var items)) continue;
            var totalAmount = items.Sum(i => (long)i.AmountPaid);
            if (totalAmount == 0) continue;

            foreach (var item in items)
            {
                if (item.AmountPaid <= 0) continue; // negative/zero amounts → no commission
                var itemShare = (decimal)pmt.Amount * ((decimal)item.AmountPaid / (decimal)totalAmount);
                if (itemShare <= 0) continue;
                var bookPriceShare = item.ItemType == "Course" && item.AmountPaid > 0
                    ? item.BookPrice * (itemShare / (decimal)item.AmountPaid) : 0m;
                var calc = CalcNetBase(itemShare, pmt.MethodType, cfg, bookPriceShare);
                if (calc.NetBase <= 0) continue;

                // Lookup course finish date if applicable
                string? finishDate = null;
                if (item.ItemType == "Course" && item.CourseId.HasValue
                    && enrollments.TryGetValue((pmt.PcId, item.CourseId.Value), out var enrLookup))
                    finishDate = enrLookup.DateFinished;

                var detail = new CommissionDetail(
                    pmt.PurchaseId, pmt.PurchaseDate,
                    pmt.MethodType, pmt.Amount, pmt.PaymentDate,
                    item.ItemType, item.ItemName, item.AmountPaid, (int)totalAmount,
                    itemShare, calc.Vat, calc.Cc, bookPriceShare, calc.NetAfter, calc.Reserve, calc.NetBase, 0, finishDate);

                double regPct, refPct;
                string category;

                if (item.ItemType == "Auditing" || item.ItemType == "Book")
                {
                    // ── Category 1: Auditing & Books ──
                    regPct = cfg.AuditRegistrarPct;
                    refPct = cfg.AuditReferralPct;
                    category = "Auditing";
                }
                else if (item.ItemType == "Course" && item.CourseType == CourseTypes.Academy)
                {
                    // ── Category 2: PC Courses ──
                    regPct = cfg.CourseRegistrarPct;
                    refPct = cfg.CourseReferralPct;
                    category = "PC Course";

                    var key = (pmt.PcId, item.CourseId!.Value);
                    var finishedBefore = enrollments.TryGetValue(key, out var enr)
                        && string.Compare(enr.DateFinished, fromStr, StringComparison.Ordinal) < 0;

                    // Academy Instructor Start — always on in-range payments
                    foreach (var instr in academyInstructors)
                        if (instr.PersonId > 0)
                            Emit(instr.PersonId, pmt.PcName, "Acad. Start", category, calc.NetBase, instr.StartCommPct, detail);

                    // Academy Instructor Finish — only if finished BEFORE this period
                    if (finishedBefore)
                        foreach (var instr in academyInstructors)
                            if (instr.PersonId > 0)
                                Emit(instr.PersonId, pmt.PcName, "Acad. Finish", category, calc.NetBase, instr.FinishCommPct, detail);
                }
                else if (item.ItemType == "Course" && CourseTypes.IsAdvanced(item.CourseType))
                {
                    // ── Category 3: OT Courses ──
                    regPct = cfg.CourseRegistrarPct;
                    refPct = cfg.CourseReferralPct;
                    category = "OT Course";

                    var key = (pmt.PcId, item.CourseId!.Value);
                    var finishedBefore = enrollments.TryGetValue(key, out var enr)
                        && string.Compare(enr.DateFinished, fromStr, StringComparison.Ordinal) < 0;

                    // OT Instructor + CS — only if finished BEFORE this period
                    if (finishedBefore)
                    {
                        if (enr.InstructorId is > 0)
                            Emit(enr.InstructorId.Value, pmt.PcName, "OT Instructor", category, calc.NetBase, cfg.InstructorOtPct, detail);
                        if (enr.CsId is > 0)
                            Emit(enr.CsId.Value, pmt.PcName, "OT CS", category, calc.NetBase, cfg.CsOtPct, detail);
                    }
                }
                else continue; // unknown type

                // Registrar (all categories)
                if (pmt.RegistrarId is > 0)
                    Emit(pmt.RegistrarId.Value, pmt.PcName, "Registrar", category, calc.NetBase, regPct, detail);

                // Referral (all categories)
                if (pmt.ReferralId is > 0)
                    Emit(pmt.ReferralId.Value, pmt.PcName, "Referral", category, calc.NetBase, refPct, detail);
                else if (pmt.ReferralId == -1)
                    EmitUnassigned(pmt.PcName, category, calc.NetBase, refPct, pmt.Notes, detail);
            }
        }

        // ── Step F: Catch-up for courses finishing in this period ──
        // Find ALL courses with DateFinished in [from, to]
        var finishingCourses = new List<(int PcId, int CourseId, int? InstructorId, int? CsId, string CourseType, string DateFinished)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT sc.PersonId, sc.CourseId, sc.InstructorId, sc.CsId,
                       COALESCE(lc.CourseType, 'Academy') AS CourseType, sc.DateFinished
                FROM acad_student_courses sc
                INNER JOIN (
                    SELECT PersonId, CourseId, MAX(DateFinished) AS MDF
                    FROM acad_student_courses
                    WHERE DateFinished IS NOT NULL
                    GROUP BY PersonId, CourseId
                ) latest ON sc.PersonId = latest.PersonId
                         AND sc.CourseId = latest.CourseId
                         AND sc.DateFinished = latest.MDF
                JOIN lkp_courses lc ON lc.CourseId = sc.CourseId
                WHERE sc.DateFinished >= @from AND sc.DateFinished <= @to";
            cmd.Parameters.AddWithValue("@from", fromStr);
            cmd.Parameters.AddWithValue("@to", toStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                finishingCourses.Add((r.GetInt32(0), r.GetInt32(1),
                    r.IsDBNull(2) ? null : r.GetInt32(2),
                    r.IsDBNull(3) ? null : r.GetInt32(3),
                    r.GetString(4), r.GetString(5)));
        }

        if (finishingCourses.Count > 0)
        {
            // Collect in-range PaymentMethodIds to exclude from catch-up (already handled in Step E)
            var inRangeIds = new HashSet<int>(payments.Select(p => p.PaymentMethodId));

            // For each finishing course, find all relevant purchases
            foreach (var fc in finishingCourses)
            {
                // Find all purchases for this PcId + CourseId
                var catchupPurchaseIds = new List<int>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT DISTINCT p.PurchaseId
                        FROM fin_purchases p
                        JOIN fin_purchase_items pi ON p.PurchaseId = pi.PurchaseId
                        WHERE p.PcId = @pcId AND pi.CourseId = @courseId AND pi.ItemType = 'Course'
                          AND p.IsDeleted = 0 AND p.TransferPurchaseId IS NULL
                          AND p.ApprovedStatus = 'Approved'";
                    cmd.Parameters.AddWithValue("@pcId", fc.PcId);
                    cmd.Parameters.AddWithValue("@courseId", fc.CourseId);
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) catchupPurchaseIds.Add(r.GetInt32(0));
                }

                foreach (var cpid in catchupPurchaseIds)
                {
                    // Load items if not already loaded
                    if (!itemsByPurchase.TryGetValue(cpid, out var citems))
                    {
                        citems = new();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $@"
                            SELECT pi.PurchaseId, pi.PurchaseItemId, pi.ItemType, pi.CourseId, pi.AmountPaid,
                                   COALESCE(lc.CourseType, 'Academy') AS CourseType,
                                   CASE pi.ItemType
                                     WHEN 'Course' THEN COALESCE(lc.Name, 'Course')
                                     WHEN 'Book' THEN COALESCE(lb.Name, 'Book')
                                     ELSE 'Auditing'
                                   END AS ItemName,
                                   COALESCE(lc.BookPrice, 0) AS BookPrice
                            FROM fin_purchase_items pi
                            LEFT JOIN lkp_courses lc ON pi.CourseId = lc.CourseId
                            LEFT JOIN lkp_books lb ON pi.BookId = lb.BookId
                            WHERE pi.PurchaseId = @pid";
                        cmd.Parameters.AddWithValue("@pid", cpid);
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                            citems.Add((r.GetInt32(1), r.GetString(2),
                                r.IsDBNull(3) ? null : r.GetInt32(3), r.GetInt32(4), r.GetString(5), r.GetString(6),
                                r.GetDecimal(7)));
                        itemsByPurchase[cpid] = citems;
                    }

                    var totalAmount = citems.Sum(i => (long)i.AmountPaid);
                    if (totalAmount == 0) continue;

                    var courseItem = citems.FirstOrDefault(i => i.ItemType == "Course" && i.CourseId == fc.CourseId);
                    if (courseItem.AmountPaid <= 0) continue; // negative/zero amounts → no commission

                    // Load PC name + purchase date for this purchase
                    string pcName = "", purchaseDate = "";
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')), p.PurchaseDate
                            FROM fin_purchases p
                            JOIN core_persons pc ON pc.PersonId = p.PcId
                            WHERE p.PurchaseId = @pid";
                        cmd.Parameters.AddWithValue("@pid", cpid);
                        using var r = cmd.ExecuteReader();
                        if (r.Read()) { pcName = r.GetString(0); purchaseDate = r.GetString(1); }
                    }

                    // Get ALL "in the bank" payments for this purchase (no date filter)
                    var pastPayments = new List<(int PaymentMethodId, int Amount, string MethodType, string PaymentDate)>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT pm.PaymentMethodId, pm.Amount, pm.MethodType, COALESCE(pm.PaymentDate,'')
                            FROM fin_payment_methods pm
                            WHERE pm.PurchaseId = @pid
                              AND pm.IsMoneyInBank = 1
                              AND pm.MethodType NOT IN ('Credit', 'ToBePaid')";
                        cmd.Parameters.AddWithValue("@pid", cpid);
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                            pastPayments.Add((r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3)));
                    }

                    foreach (var pp in pastPayments)
                    {
                        var itemShare = (decimal)pp.Amount * ((decimal)courseItem.AmountPaid / (decimal)totalAmount);
                        if (itemShare <= 0) continue; // negative/zero → no commission
                        var bookPriceShare = courseItem.BookPrice * (itemShare / (decimal)courseItem.AmountPaid);
                        var calc = CalcNetBase(itemShare, pp.MethodType, cfg, bookPriceShare);
                        if (calc.NetBase <= 0) continue;

                        var detail = new CommissionDetail(
                            cpid, purchaseDate,
                            pp.MethodType, pp.Amount, pp.PaymentDate,
                            courseItem.ItemType, courseItem.ItemName, courseItem.AmountPaid, (int)totalAmount,
                            itemShare, calc.Vat, calc.Cc, bookPriceShare, calc.NetAfter, calc.Reserve, calc.NetBase, 0, fc.DateFinished);

                        if (fc.CourseType == CourseTypes.Academy)
                        {
                            foreach (var instr in academyInstructors)
                                if (instr.PersonId > 0)
                                    Emit(instr.PersonId, pcName, "Acad. Finish", "PC Course", calc.NetBase, instr.FinishCommPct, detail);
                        }
                        else if (CourseTypes.IsAdvanced(fc.CourseType))
                        {
                            if (fc.InstructorId is > 0)
                                Emit(fc.InstructorId.Value, pcName, "OT Instructor", "OT Course", calc.NetBase, cfg.InstructorOtPct, detail);
                            if (fc.CsId is > 0)
                                Emit(fc.CsId.Value, pcName, "OT CS", "OT Course", calc.NetBase, cfg.CsOtPct, detail);
                        }
                    }
                }
            }
        }

        return (commByUser, unassigned);
    }

    // ── Salary Report ──────────────────────────────────────────────────────────
    public SalaryReport GetSalaryReport(DateOnly from, DateOnly to)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr   = to.ToString("yyyy-MM-dd");

        // Sessions per auditor
        var sessCmd = conn.CreateCommand();
        sessCmd.CommandText = @"
            SELECT s.AuditorId,
                   s.SessionDate,
                   TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')) AS PcName,
                   COALESCE(s.LengthSeconds,0) + COALESCE(s.AdminSeconds,0) AS DurationSec,
                   s.AuditorSalaryCentsPerHour,
                   s.VerifiedStatus
            FROM sess_sessions s
            JOIN core_persons pc ON pc.PersonId = s.PcId
            WHERE s.AuditorId IS NOT NULL
              AND s.SessionDate >= @from AND s.SessionDate <= @to
              AND COALESCE(s.IsImported,0) = 0
            ORDER BY s.AuditorId, s.SessionDate, s.SequenceInDay";
        sessCmd.Parameters.AddWithValue("@from", fromStr);
        sessCmd.Parameters.AddWithValue("@to",   toStr);

        var sessionsByUser = new Dictionary<int, List<SalarySessionRow>>();
        using (var r = sessCmd.ExecuteReader())
        {
            while (r.Read())
            {
                var audId      = r.GetInt32(0);
                var isApproved = r.GetString(5) == "Approved";
                var chargeSec  = r.IsDBNull(3) ? 0 : r.GetInt32(3);
                var rateCents  = r.IsDBNull(4) ? 0 : r.GetInt32(4);
                var payment    = isApproved ? (long)rateCents * chargeSec / 3600L : 0L;
                var row = new SalarySessionRow(
                    r.GetString(1), r.GetString(2),
                    chargeSec, rateCents, payment, isApproved);
                if (!sessionsByUser.ContainsKey(audId)) sessionsByUser[audId] = new();
                sessionsByUser[audId].Add(row);
            }
        }

        // CS reviews per CS user
        var csCmd = conn.CreateCommand();
        csCmd.CommandText = @"
            SELECT cr.CsId,
                   s.SessionDate,
                   TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')) AS PcName,
                   cr.ReviewLengthSeconds,
                   cr.CsSalaryCentsPerHour,
                   cr.Status
            FROM cs_reviews cr
            JOIN sess_sessions s  ON s.SessionId  = cr.SessionId
            JOIN core_persons  pc ON pc.PersonId  = s.PcId
            WHERE s.SessionDate >= @from AND s.SessionDate <= @to
              AND COALESCE(s.IsImported,0) = 0
            ORDER BY cr.CsId, s.SessionDate";
        csCmd.Parameters.AddWithValue("@from", fromStr);
        csCmd.Parameters.AddWithValue("@to",   toStr);

        var csByUser = new Dictionary<int, List<SalaryCsRow>>();
        using (var r = csCmd.ExecuteReader())
        {
            while (r.Read())
            {
                var csId       = r.GetInt32(0);
                var isApproved = r.GetString(5) == "Approved";
                var durSec     = r.IsDBNull(3) ? 0 : r.GetInt32(3);
                var rateCents  = r.IsDBNull(4) ? 0 : r.GetInt32(4);
                var payment    = isApproved ? (long)rateCents * durSec / 3600L : 0L;
                var row = new SalaryCsRow(
                    r.GetString(1), r.GetString(2),
                    durSec, rateCents, payment, isApproved);
                if (!csByUser.ContainsKey(csId)) csByUser[csId] = new();
                csByUser[csId].Add(row);
            }
        }

        // Commission calculation
        var (commByUser, unassignedReferrals) = GetCommissionData(conn, fromStr, toStr);

        // Load non-solo staff users who appear in sessions, CS reviews, OR commissions
        var allPersonIds = sessionsByUser.Keys.Union(csByUser.Keys).Union(commByUser.Keys).ToHashSet();
        if (allPersonIds.Count == 0) return new SalaryReport(new(), unassignedReferrals);

        var inClause = string.Join(",", allPersonIds);
        var userCmd  = conn.CreateCommand();
        userCmd.CommandText = $@"
            SELECT cu.PersonId,
                   TRIM(cp.FirstName || ' ' || COALESCE(NULLIF(cp.LastName,''), '')) AS FullName
            FROM core_users cu
            JOIN core_persons cp ON cp.PersonId = cu.PersonId
            WHERE cu.PersonId IN ({inClause})
              AND cu.StaffRole != 'Solo'
            ORDER BY cp.FirstName, cp.LastName";

        var result = new List<UserSalaryGroup>();
        // Track which PersonIds we've seen from core_users
        var seenPersonIds = new HashSet<int>();
        using (var r = userCmd.ExecuteReader())
        {
            while (r.Read())
            {
                var personId = r.GetInt32(0);
                var fullName = r.GetString(1);
                seenPersonIds.Add(personId);
                result.Add(new UserSalaryGroup(
                    personId, fullName,
                    sessionsByUser.GetValueOrDefault(personId) ?? new(),
                    csByUser.GetValueOrDefault(personId) ?? new(),
                    commByUser.GetValueOrDefault(personId) ?? new()));
            }
        }

        // Add commission-only earners who might not be in core_users (e.g., external referrals)
        foreach (var pid in commByUser.Keys.Where(id => !seenPersonIds.Contains(id)))
        {
            using var nameCmd = conn.CreateCommand();
            nameCmd.CommandText = @"
                SELECT TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''), ''))
                FROM core_persons WHERE PersonId = @pid";
            nameCmd.Parameters.AddWithValue("@pid", pid);
            var name = nameCmd.ExecuteScalar()?.ToString() ?? $"Person #{pid}";
            result.Add(new UserSalaryGroup(pid, name, new(), new(),
                commByUser.GetValueOrDefault(pid) ?? new()));
        }

        return new SalaryReport(result, unassignedReferrals);
    }

    // ── Session Manager ─────────────────────────────────────────────────────

    public (List<SessionListItem> Items, int TotalCount) GetSessionsForPcPaged(int pcId, int page, int pageSize = 50)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cntCmd = conn.CreateCommand();
        cntCmd.CommandText = "SELECT COUNT(*) FROM sess_sessions WHERE PcId = @pcId";
        cntCmd.Parameters.AddWithValue("@pcId", pcId);
        var total = Convert.ToInt32(cntCmd.ExecuteScalar());

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT s.SessionId, s.Name, s.SessionDate,
                   COALESCE({FullNameExpr}, '') AS AuditorName,
                   s.LengthSeconds, s.AdminSeconds, s.IsFreeSession,
                   s.VerifiedStatus, s.IsImported
            FROM sess_sessions s
            LEFT JOIN core_persons p ON p.PersonId = s.AuditorId
            WHERE s.PcId = @pcId
            ORDER BY s.SessionDate DESC, s.SessionId DESC
            LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@limit", pageSize);
        cmd.Parameters.AddWithValue("@offset", page * pageSize);
        var list = new List<SessionListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SessionListItem(
                r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetString(2),
                r.GetString(3), r.GetInt32(4), r.GetInt32(5),
                r.GetInt32(6) != 0, r.GetString(7), r.GetInt32(8) != 0));
        return (list, total);
    }

    public SessionDetailModel? GetSessionDetail(int sessionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT s.SessionId, s.PcId, s.AuditorId,
                   COALESCE(TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), '')), '') AS AuditorName,
                   s.SessionDate, s.SequenceInDay, s.LengthSeconds, s.AdminSeconds,
                   s.IsFreeSession, s.ChargeSeconds, s.ChargedRateCentsPerHour,
                   s.AuditorSalaryCentsPerHour, s.Name, s.VerifiedStatus,
                   s.ApprovedNotes, s.IsImported, s.CreatedAt, s.CreatedByUserId,
                   cr.CsReviewId, cr.CsId,
                   COALESCE(TRIM(pc.FirstName || ' ' || COALESCE(NULLIF(pc.LastName,''), '')), '') AS CsName,
                   cr.ReviewLengthSeconds, cr.ReviewedAt, cr.Status AS CsStatus,
                   cr.Notes AS CsNotes, cr.CsSalaryCentsPerHour, cr.ChargedCentsRatePerHour
            FROM sess_sessions s
            LEFT JOIN core_persons pa ON pa.PersonId = s.AuditorId
            LEFT JOIN cs_reviews cr ON cr.SessionId = s.SessionId
            LEFT JOIN core_persons pc ON pc.PersonId = cr.CsId
            WHERE s.SessionId = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SessionDetailModel(
            r.GetInt32(0), r.GetInt32(1),
            r.IsDBNull(2) ? null : r.GetInt32(2),
            r.GetString(3), r.GetString(4), r.GetInt32(5),
            r.GetInt32(6), r.GetInt32(7),
            r.GetInt32(8) != 0, r.GetInt32(9), r.GetInt32(10),
            r.GetInt32(11), r.IsDBNull(12) ? "" : r.GetString(12), r.GetString(13),
            r.IsDBNull(14) ? null : r.GetString(14), r.GetInt32(15) != 0,
            r.GetString(16), r.IsDBNull(17) ? null : (int?)r.GetInt32(17),
            r.IsDBNull(18) ? null : (int?)r.GetInt32(18),
            r.IsDBNull(19) ? null : (int?)r.GetInt32(19),
            r.IsDBNull(20) ? null : r.GetString(20),
            r.IsDBNull(21) ? null : (int?)r.GetInt32(21),
            r.IsDBNull(22) ? null : r.GetString(22),
            r.IsDBNull(23) ? null : r.GetString(23),
            r.IsDBNull(24) ? null : r.GetString(24),
            r.IsDBNull(25) ? null : (int?)r.GetInt32(25),
            r.IsDBNull(26) ? null : (int?)r.GetInt32(26));
    }

    public void UpdateSessionAdmin(int sessionId, string name, string sessionDate,
        int lengthSec, int adminSec, bool isFree, string verifiedStatus,
        string? approvedNotes, int chargeSec, int chargedRate, int auditorSalaryRate)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_sessions SET
                Name = @name, SessionDate = @date,
                LengthSeconds = @len, AdminSeconds = @admin,
                IsFreeSession = @free, VerifiedStatus = @vs,
                ApprovedNotes = @notes, ChargeSeconds = @charge,
                ChargedRateCentsPerHour = @chargedRate,
                AuditorSalaryCentsPerHour = @audSalary
            WHERE SessionId = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@date", sessionDate);
        cmd.Parameters.AddWithValue("@len", lengthSec);
        cmd.Parameters.AddWithValue("@admin", adminSec);
        cmd.Parameters.AddWithValue("@free", isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@vs", verifiedStatus);
        cmd.Parameters.AddWithValue("@notes", (object?)approvedNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@charge", chargeSec);
        cmd.Parameters.AddWithValue("@chargedRate", chargedRate);
        cmd.Parameters.AddWithValue("@audSalary", auditorSalaryRate);
        cmd.ExecuteNonQuery();

        using var fsCmd = conn.CreateCommand();
        fsCmd.CommandText = "UPDATE sess_folder_summary SET CreatedAt = @date WHERE SessionId = @sid";
        fsCmd.Parameters.AddWithValue("@sid", sessionId);
        fsCmd.Parameters.AddWithValue("@date", sessionDate);
        fsCmd.ExecuteNonQuery();

        Console.WriteLine($"[SessionManager] Updated session {sessionId}");
    }

    public void UpdateCsReviewAdmin(int csReviewId, int reviewLenSec, string status,
        string? notes, int csSalaryRate, int csChargedRate)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE cs_reviews SET
                ReviewLengthSeconds = @len, Status = @status,
                Notes = @notes, CsSalaryCentsPerHour = @salary,
                ChargedCentsRatePerHour = @charged
            WHERE CsReviewId = @id";
        cmd.Parameters.AddWithValue("@id", csReviewId);
        cmd.Parameters.AddWithValue("@len", reviewLenSec);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@salary", csSalaryRate);
        cmd.Parameters.AddWithValue("@charged", csChargedRate);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[SessionManager] Updated cs_review {csReviewId}");
    }

    public SessionDeleteInfo LoadSessionDeleteInfo(int sessionId, int pcId, string sessName, FolderService folderSvc)
    {
        var fkTables = new List<SessionFkTable>();
        foreach (var (tbl, idCol) in new[]
        {
            ("cs_reviews",          "CsReviewId"),
            ("sess_folder_summary", "Id"),
            ("sess_next_cs",        "Id"),
            ("sess_questions",      "SessionId"),
        })
        {
            var cols = new List<string>();
            var rows = new List<List<string>>();
            try
            {
                using var conn2 = new SqliteConnection(_connectionString);
                conn2.Open();
                using var cmd2 = conn2.CreateCommand();
                cmd2.CommandText = $"SELECT * FROM {tbl} WHERE SessionId = @sid";
                cmd2.Parameters.AddWithValue("@sid", sessionId);
                using var r = cmd2.ExecuteReader();
                for (int i = 0; i < r.FieldCount; i++) cols.Add(r.GetName(i));
                while (r.Read())
                {
                    var rr = new List<string>();
                    for (int i = 0; i < r.FieldCount; i++)
                        rr.Add(r.IsDBNull(i) ? "" : r.GetValue(i)?.ToString() ?? "");
                    rows.Add(rr);
                }
            }
            catch { }
            fkTables.Add(new SessionFkTable(tbl, cols, rows));
        }

        var files = new List<string>();
        if (!string.IsNullOrWhiteSpace(sessName))
        {
            foreach (var finder in new Func<int, string?>[] {
                id => folderSvc.FindPcFolder(id),
                id => folderSvc.FindSoloPcFolder(id) })
            {
                var folder = pcId > 0 ? finder(pcId) : null;
                if (folder == null) continue;
                var wsPath = Path.Combine(folder, "WorkSheets");
                if (!Directory.Exists(wsPath)) continue;
                var nameNoExt = Path.GetFileNameWithoutExtension(sessName);
                foreach (var f in Directory.GetFiles(wsPath, "*.pdf"))
                {
                    var fn = Path.GetFileNameWithoutExtension(f);
                    if (string.Equals(fn, nameNoExt, StringComparison.OrdinalIgnoreCase) ||
                        fn.StartsWith(nameNoExt + "_att_", StringComparison.OrdinalIgnoreCase))
                        files.Add(f);
                }
            }
        }
        return new SessionDeleteInfo(sessionId, pcId, sessName, fkTables, files);
    }

    public void ExecuteSessionDeletion(SessionDeleteInfo info, string adminUser, FolderService folderSvc)
    {
        Console.WriteLine($"[DeleteSession] ====== START — Admin='{adminUser}' SessionId={info.SessionId} PcId={info.PcId} Name='{info.SessionName}' ======");

        // ── Backup files before deleting ──
        var pcFoldersRoot = folderSvc.GetPcFoldersRoot();
        var backupRoot    = Path.Combine(pcFoldersRoot, "_deleted_sessions");
        var backupFolder  = Path.Combine(backupRoot, $"{info.SessionId}_{DateTime.Now:yyyyMMdd_HHmmss}");

        if (Directory.Exists(backupRoot))
        {
            foreach (var old in Directory.GetDirectories(backupRoot))
            {
                try
                {
                    if (Directory.GetCreationTime(old) < DateTime.Now.AddDays(-10))
                    {
                        Directory.Delete(old, recursive: true);
                        Console.WriteLine($"[DeleteSession]   BACKUP PURGED (>10 days): {old}");
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[DeleteSession]   BACKUP PURGE ERROR: {old} — {ex.Message}"); }
            }
        }

        Console.WriteLine($"[DeleteSession] Files to backup+delete: {info.Files.Count}");
        if (info.Files.Count > 0) Directory.CreateDirectory(backupFolder);
        foreach (var f in info.Files)
        {
            try
            {
                var dest = Path.Combine(backupFolder, Path.GetFileName(f));
                File.Copy(f, dest, overwrite: true);
                Console.WriteLine($"[DeleteSession]   FILE BACKED UP: {f} → {dest}");
                File.Delete(f);
                Console.WriteLine($"[DeleteSession]   FILE DELETED: {f}");
            }
            catch (Exception ex) { Console.WriteLine($"[DeleteSession]   FILE ERROR (skipped): {f} — {ex.Message}"); }
        }

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        foreach (var fkTbl in info.FkTables)
        {
            Console.WriteLine($"[DeleteSession] Table {fkTbl.Table}: {fkTbl.Rows.Count} row(s) to delete");
            foreach (var row in fkTbl.Rows)
            {
                var rowDesc = string.Join(", ", fkTbl.Cols.Zip(row, (c, v) => $"{c}={v}"));
                Console.WriteLine($"[DeleteSession]   ROW: {rowDesc}");
            }
            cmd.CommandText = $"DELETE FROM {fkTbl.Table} WHERE SessionId = @sid";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@sid", info.SessionId);
            var n = cmd.ExecuteNonQuery();
            Console.WriteLine($"[DeleteSession]   → {n} row(s) deleted from {fkTbl.Table}");
        }

        cmd.CommandText = "DELETE FROM sess_sessions WHERE SessionId = @sid";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@sid", info.SessionId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[DeleteSession]   ROW: SessionId={info.SessionId}, PcId={info.PcId}, Name='{info.SessionName}'");
        Console.WriteLine($"[DeleteSession]   → 1 row deleted from sess_sessions");
        Console.WriteLine($"[DeleteSession] ====== END — Admin='{adminUser}' SessionId={info.SessionId} ======");
    }
}
