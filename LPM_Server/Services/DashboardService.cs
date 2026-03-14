using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LPM.Services;

public record PcInfo(int PcId, string FullName, string WorkCapacity, string Nick = "");
public record SessionRow(int SessionId, int LengthSec, int AdminSec, bool IsFree, string? Summary, string CreatedAt, string AuditorName, string VerifiedStatus = "Pending", string? Name = null);
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
    string SessionDate, int ReviewLengthSeconds, int CsSalaryCentsPerHour, string CsStatus);
public record PcCsGroup(int PcId, string PcName, List<AdminCsRow> Reviews);
public record CsReviewerGroup(int CsId, string CsName, List<PcCsGroup> PcGroups);

public record StaffMember(int PersonId, string FullName);
public record StaffMessage(int Id, int FromId, string FromName, int ToId, string ToName, string MsgText, string CreatedAt, string? AcknowledgedAt);

public record PermissionRequest(int Id, int AuditorId, string AuditorName, int PcId, string PcName, string RequestedAt);
public record ApprovedPcEntry(int Id, int PcId, string PcName);
public record AuditorPermGroup(int AuditorId, string AuditorName, bool AllowAll, List<ApprovedPcEntry> ApprovedPcs);

public class DashboardService
{
    private readonly string _connectionString;
    private readonly MessageNotifier _messageNotifier;

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

        // Remove duplicate Persons rows (keep lowest PersonId per FirstName)
        using var dedupCmd = conn.CreateCommand();
        dedupCmd.CommandText = @"
            DELETE FROM core_persons
            WHERE PersonId NOT IN (
                SELECT MIN(PersonId) FROM core_persons GROUP BY LOWER(FirstName)
            )
            AND PersonId NOT IN (SELECT AuditorId FROM sess_auditors)
            AND PersonId NOT IN (SELECT CsId FROM cs_case_supervisors)
            AND PersonId NOT IN (SELECT PcId FROM core_pcs)";
        var removed = dedupCmd.ExecuteNonQuery();
        if (removed > 0)
            Console.WriteLine($"[Startup] Removed {removed} duplicate Persons row(s).");

        // Ensure every active User has a matching Persons row (matched by Username → FirstName)
        using var ensurePersonsCmd = conn.CreateCommand();
        ensurePersonsCmd.CommandText = @"
            INSERT INTO core_persons (FirstName, LastName)
            SELECT u.Username, ''
            FROM core_users u
            WHERE u.IsActive = 1
              AND NOT EXISTS (
                  SELECT 1 FROM core_persons p WHERE LOWER(p.FirstName) = LOWER(u.Username)
              )";
        var inserted = ensurePersonsCmd.ExecuteNonQuery();
        if (inserted > 0)
            Console.WriteLine($"[Startup] Created {inserted} missing Persons row(s) for active Users.");

        // Ensure all staff (Auditors + CaseSupervisors) exist in PCs table
        using var ensurePcsCmd = conn.CreateCommand();
        ensurePcsCmd.CommandText = @"
            INSERT OR IGNORE INTO core_pcs (PcId)
            SELECT AuditorId FROM sess_auditors
            UNION
            SELECT CsId FROM cs_case_supervisors";
        var pcsInserted = ensurePcsCmd.ExecuteNonQuery();
        if (pcsInserted > 0)
            Console.WriteLine($"[Startup] Added {pcsInserted} staff member(s) to PCs table.");


    }

    // ── Staff Permissions ─────────────────────────────────────────

    /// <summary>
    /// Called when an auditor adds a PC to their dashboard. Returns true if the auditor is
    /// permitted (AllowAll=1 or existing approved permission). Returns false if a pending
    /// request was created and the PC should appear grayed out.
    /// </summary>
    public bool CheckOrRequestPermission(int auditorId, int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Check AllowAll flag
        using var aaCmd = conn.CreateCommand();
        aaCmd.CommandText = "SELECT AllowAll FROM sess_auditors WHERE AuditorId = @id";
        aaCmd.Parameters.AddWithValue("@id", auditorId);
        var allowAll = aaCmd.ExecuteScalar() is long aa && aa == 1;
        if (allowAll) return true;

        // Check existing permission
        using var chkCmd = conn.CreateCommand();
        chkCmd.CommandText = "SELECT IsApproved FROM sys_auditor_pc_permissions WHERE AuditorId = @aud AND PcId = @pc";
        chkCmd.Parameters.AddWithValue("@aud", auditorId);
        chkCmd.Parameters.AddWithValue("@pc",  pcId);
        var existing = chkCmd.ExecuteScalar();
        if (existing is long approved) return approved == 1;

        // No existing record — create a pending request
        using var insCmd = conn.CreateCommand();
        insCmd.CommandText = @"
            INSERT OR IGNORE INTO sys_auditor_pc_permissions (AuditorId, PcId, IsApproved)
            VALUES (@aud, @pc, 0)";
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
        // Find Admin-role users → resolve their PersonId via Persons.FirstName match on Username
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PersonId
            FROM core_persons p
            JOIN core_users u ON LOWER(u.Username) = LOWER(p.FirstName)
            JOIN core_user_roles ur ON ur.UserId = u.Id
            JOIN lkp_roles r ON r.RoleId = ur.RoleId
            WHERE r.Code = 'Admin'";
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
    }

    /// Returns all PcIds in the auditor's StaffPcList that are NOT explicitly approved.
    /// Includes PCs added before the permission system existed.
    public HashSet<int> GetUnapprovedPcIds(int auditorId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // AllowAll=1 → nothing blocked
        using var aaCmd = conn.CreateCommand();
        aaCmd.CommandText = "SELECT COALESCE(AllowAll, 0) FROM sess_auditors WHERE AuditorId = @id";
        aaCmd.Parameters.AddWithValue("@id", auditorId);
        if (aaCmd.ExecuteScalar() is long aa && aa == 1) return [];

        // Any PC in StaffPcList without an IsApproved=1 entry is unapproved
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT spl.PcId
            FROM sys_staff_pc_list spl
            WHERE spl.UserId = @aud AND spl.WorkCapacity != 'CSSolo'
              AND NOT EXISTS (
                  SELECT 1 FROM sys_auditor_pc_permissions ap
                  WHERE ap.AuditorId = @aud AND ap.PcId = spl.PcId AND ap.IsApproved = 1
              )";
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
            SELECT ap.Id, ap.AuditorId,
                   TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), '')) AS AuditorName,
                   ap.PcId,
                   TRIM(pp.FirstName || ' ' || COALESCE(NULLIF(pp.LastName,''), '')) AS PcName,
                   ap.RequestedAt
            FROM sys_auditor_pc_permissions ap
            JOIN core_persons pa ON pa.PersonId = ap.AuditorId
            JOIN core_persons pp ON pp.PersonId = ap.PcId
            WHERE ap.IsApproved = 0
            ORDER BY ap.RequestedAt DESC, pa.FirstName";
        var list = new List<PermissionRequest>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PermissionRequest(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2),
                r.GetInt32(3), r.GetString(4), r.GetString(5)));
        return list;
    }

    public void ApprovePermissionRequest(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sys_auditor_pc_permissions SET IsApproved = 1 WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void RejectPermissionRequest(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // Remove from StaffPcList too so it disappears from the auditor's dashboard
        using var getCmd = conn.CreateCommand();
        getCmd.CommandText = "SELECT AuditorId, PcId FROM sys_auditor_pc_permissions WHERE Id = @id";
        getCmd.Parameters.AddWithValue("@id", id);
        using var r = getCmd.ExecuteReader();
        if (!r.Read()) return;
        int auditorId = r.GetInt32(0);
        int pcId      = r.GetInt32(1);
        r.Close();

        using var delPerm = conn.CreateCommand();
        delPerm.CommandText = "DELETE FROM sys_auditor_pc_permissions WHERE Id = @id";
        delPerm.Parameters.AddWithValue("@id", id);
        delPerm.ExecuteNonQuery();

        using var delSpl = conn.CreateCommand();
        delSpl.CommandText = "DELETE FROM sys_staff_pc_list WHERE UserId = @uid AND PcId = @pc AND WorkCapacity != 'CSSolo'";
        delSpl.Parameters.AddWithValue("@uid", auditorId);
        delSpl.Parameters.AddWithValue("@pc",  pcId);
        delSpl.ExecuteNonQuery();
    }

    public List<AuditorPermGroup> GetAuditorPermGroups()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Get all active auditors with AllowAll
        using var audCmd = conn.CreateCommand();
        audCmd.CommandText = @"
            SELECT a.AuditorId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS Name,
                   COALESCE(a.AllowAll, 0)
            FROM sess_auditors a
            JOIN core_persons p ON p.PersonId = a.AuditorId
            WHERE a.IsActive = 1 AND a.Type != 0
            ORDER BY p.FirstName, p.LastName";
        var auditors = new List<(int Id, string Name, bool AllowAll)>();
        using var ar = audCmd.ExecuteReader();
        while (ar.Read())
            auditors.Add((ar.GetInt32(0), ar.GetString(1), ar.GetInt32(2) == 1));

        // Get approved permissions per auditor
        using var permCmd = conn.CreateCommand();
        permCmd.CommandText = @"
            SELECT ap.Id, ap.AuditorId, ap.PcId,
                   TRIM(pp.FirstName || ' ' || COALESCE(NULLIF(pp.LastName,''), '')) AS PcName
            FROM sys_auditor_pc_permissions ap
            JOIN core_persons pp ON pp.PersonId = ap.PcId
            WHERE ap.IsApproved = 1
            ORDER BY pp.FirstName, pp.LastName";
        var permsByAuditor = new Dictionary<int, List<ApprovedPcEntry>>();
        using var pr = permCmd.ExecuteReader();
        while (pr.Read())
        {
            int audId = pr.GetInt32(1);
            if (!permsByAuditor.ContainsKey(audId)) permsByAuditor[audId] = new();
            permsByAuditor[audId].Add(new ApprovedPcEntry(pr.GetInt32(0), pr.GetInt32(2), pr.GetString(3)));
        }

        return auditors.Select(a => new AuditorPermGroup(
            a.Id, a.Name, a.AllowAll,
            permsByAuditor.GetValueOrDefault(a.Id) ?? new())).ToList();
    }

    public void SetAuditorAllowAll(int auditorId, bool allow)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sess_auditors SET AllowAll = @v WHERE AuditorId = @id";
        cmd.Parameters.AddWithValue("@v",  allow ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", auditorId);
        cmd.ExecuteNonQuery();
    }

    /// Admin adds a PC permission directly (approved).
    public void AddApprovedPermission(int auditorId, int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sys_auditor_pc_permissions (AuditorId, PcId, IsApproved)
            VALUES (@aud, @pc, 1)
            ON CONFLICT(AuditorId, PcId) DO UPDATE SET IsApproved = 1";
        cmd.Parameters.AddWithValue("@aud", auditorId);
        cmd.Parameters.AddWithValue("@pc",  pcId);
        cmd.ExecuteNonQuery();
    }

    public void RemovePermission(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sys_auditor_pc_permissions WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns the PersonId for the given username (matches Persons.FirstName case-insensitively).
    /// </summary>
    public int? GetUserIdByUsername(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PersonId FROM core_persons WHERE LOWER(FirstName) = LOWER(@u) LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : null;
    }


    /// <summary>Check if a user (by PersonId) can access a PC's folder.
    /// Admins always can. Otherwise requires approved permission or AllowAll.</summary>
    public bool CanAccessPcFolder(int personId, int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Check AllowAll flag on the auditor
        using var cmdAllow = conn.CreateCommand();
        cmdAllow.CommandText = "SELECT AllowAll FROM sess_auditors WHERE AuditorId = @aid AND IsActive = 1";
        cmdAllow.Parameters.AddWithValue("@aid", personId);
        var allowAll = cmdAllow.ExecuteScalar();
        if (allowAll is long a && a == 1) return true;

        // Check approved permission
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sys_auditor_pc_permissions WHERE AuditorId = @aid AND PcId = @pid AND IsApproved = 1";
        cmd.Parameters.AddWithValue("@aid", personId);
        cmd.Parameters.AddWithValue("@pid", pcId);
        return cmd.ExecuteScalar() is not null;
    }

    public bool IsAuditor(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sess_auditors WHERE AuditorId = @id AND IsActive = 1 AND Type IN (1, 3)";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() is not null;
    }

    public bool IsCS(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM cs_case_supervisors WHERE CsId = @id AND IsActive = 1";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() is not null;
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
            ORDER BY p.FirstName, p.LastName, spl.WorkCapacity";
        cmd.Parameters.AddWithValue("@uid", userId);
        var list = new List<PcInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcInfo(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
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
                   COALESCE(p.Nick, '') AS Nick
            FROM core_pcs     pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            ORDER BY p.FirstName, p.LastName";
        var list = new List<PcInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcInfo(r.GetInt32(0), r.GetString(1), "Auditor", r.GetString(2)));

        return list;
    }

    public void AddUserPc(int userId, int pcId, string workCapacity = "Auditor")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO sys_staff_pc_list (UserId, PcId, WorkCapacity) VALUES (@uid, @pcId, @cap)";
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@cap",  workCapacity);
        cmd.ExecuteNonQuery();
    }

    public void RemoveUserPc(int userId, int pcId, string workCapacity = "Auditor")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sys_staff_pc_list WHERE UserId = @uid AND PcId = @pcId AND WorkCapacity = @cap";
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@cap",  workCapacity);
        cmd.ExecuteNonQuery();
    }

    public void SetUserPcRole(int userId, int pcId, string role)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // CSSolo columns are not changed here — only Auditor/CS/Miscellaneous columns
        cmd.CommandText = "UPDATE sys_staff_pc_list SET WorkCapacity = @role WHERE UserId = @uid AND PcId = @pcId AND WorkCapacity NOT IN ('CSSolo')";
        cmd.Parameters.AddWithValue("@role", role);
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.ExecuteNonQuery();
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

        var auditorPcIds = userPcs.Where(p => p.WorkCapacity == "Auditor").Select(p => p.PcId).ToList();
        var csPcIds      = userPcs.Where(p => p.WorkCapacity == "CS").Select(p => p.PcId).ToList();
        var soloCSPcIds  = userPcs.Where(p => p.WorkCapacity == "CSSolo").Select(p => p.PcId).ToList();
        var miscPcIds    = userPcs.Where(p => p.WorkCapacity == "Miscellaneous").Select(p => p.PcId).ToList();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        if (auditorPcIds.Count > 0)
        {
            var pcList = string.Join(",", auditorPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT PcId, SessionDate, SUM(LengthSeconds + AdminSeconds)
                FROM sess_sessions
                WHERE AuditorId = @uid AND PcId IN ({pcList}) AND SessionDate IN ({dateList}) AND PcId != AuditorId
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
                WHERE cr.CsId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate IN ({dateList}) AND s.PcId != s.AuditorId
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

        // Solo CS columns: CS reviewing sessions where PcId = AuditorId (solo)
        // Grid key uses -pcId to distinguish from the same person's regular column
        if (soloCSPcIds.Count > 0)
        {
            var pcList = string.Join(",", soloCSPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.PcId, s.SessionDate, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE cr.CsId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate IN ({dateList}) AND s.PcId = s.AuditorId
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
                var key = (-pcId, dayIdx);   // negative key for solo column
                result[key] = result.GetValueOrDefault(key) + secs;
            }
        }

        if (miscPcIds.Count > 0)
        {
            var pcList = string.Join(",", miscPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT PcId, ChargeDate, SUM(LengthSeconds + AdminSeconds)
                FROM sess_misc_charges
                WHERE AuditorId = @uid AND PcId IN ({pcList}) AND ChargeDate IN ({dateList})
                GROUP BY PcId, ChargeDate";
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

        if (role == "Auditor")
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT s.SessionId, s.LengthSeconds, s.AdminSeconds,
                       s.IsFreeSession, s.SessionSummaryHtml, s.CreatedAt,
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
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? ""   : r.GetString(5),
                    r.IsDBNull(6) ? ""   : r.GetString(6),
                    r.IsDBNull(7) ? "Pending" : r.GetString(7),
                    r.IsDBNull(8) ? null : r.GetString(8)));
            }
        }
        else if (role == "Miscellaneous")
        {
            // MiscCharge entries are per-auditor — each auditor sees only their own rows
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT MiscChargeId, LengthSeconds, AdminSeconds,
                       IsFree, Summary, CreatedAt
                FROM sess_misc_charges
                WHERE AuditorId = @uid AND PcId = @pcId AND ChargeDate = @date
                ORDER BY SequenceInDay";
            cmd.Parameters.AddWithValue("@uid",  userId);
            cmd.Parameters.AddWithValue("@pcId", pcId);
            cmd.Parameters.AddWithValue("@date", dateStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                sessions.Add(new SessionRow(
                    r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                    r.GetInt32(3) == 1,
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? ""   : r.GetString(5),
                    ""));
            }
        }
        else if (role == "SoloAuditor")
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT SessionId, LengthSeconds, AdminSeconds,
                       IsFreeSession, SessionSummaryHtml, CreatedAt,
                       VerifiedStatus, Name
                FROM sess_sessions
                WHERE AuditorId = @uid AND PcId = AuditorId AND SessionDate = @date
                ORDER BY SequenceInDay";
            cmd.Parameters.AddWithValue("@uid",  userId);
            cmd.Parameters.AddWithValue("@date", dateStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                sessions.Add(new SessionRow(
                    r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                    r.GetInt32(3) == 1,
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? ""   : r.GetString(5),
                    "",
                    r.IsDBNull(6) ? "Pending" : r.GetString(6),
                    r.IsDBNull(7) ? null : r.GetString(7)));
            }
        }
        else  // CS role
        {
            // All sessions for this PC+date from any auditor, with auditor's first name
            // For CSSolo column: only sessions where PcId=AuditorId; for regular CS: PcId!=AuditorId
            bool isCSSolo = role == "CSSolo";
            using var sessCmd = conn.CreateCommand();
            sessCmd.CommandText = $@"
                SELECT s.SessionId, s.LengthSeconds, s.AdminSeconds,
                       s.IsFreeSession, s.SessionSummaryHtml, s.CreatedAt,
                       p.FirstName, s.VerifiedStatus, s.Name
                FROM sess_sessions s
                JOIN core_persons p ON p.PersonId = s.AuditorId
                WHERE s.PcId = @pcId AND s.SessionDate = @date AND s.PcId {(isCSSolo ? "=" : "!=")} s.AuditorId
                ORDER BY s.SequenceInDay";
            sessCmd.Parameters.AddWithValue("@pcId", pcId);
            sessCmd.Parameters.AddWithValue("@date", dateStr);
            using var rs = sessCmd.ExecuteReader();
            while (rs.Read())
            {
                sessions.Add(new SessionRow(
                    rs.GetInt32(0), rs.GetInt32(1), rs.GetInt32(2),
                    rs.GetInt32(3) == 1,
                    rs.IsDBNull(4) ? null : rs.GetString(4),
                    rs.IsDBNull(5) ? ""   : rs.GetString(5),
                    rs.IsDBNull(6) ? ""   : rs.GetString(6),
                    rs.IsDBNull(7) ? "Pending" : rs.GetString(7),
                    rs.IsDBNull(8) ? null : rs.GetString(8)));
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
               SessionSummaryHtml, CreatedAt)
            VALUES
              (@pcId, @audId, @date, @seq,
               @len, @adm, @free,
               0, 0,
               @sum, datetime('now'))";
        cmd.Parameters.AddWithValue("@pcId",  pcId);
        cmd.Parameters.AddWithValue("@audId", auditorId);
        cmd.Parameters.AddWithValue("@date",  dateStr);
        cmd.Parameters.AddWithValue("@seq",   seq);
        cmd.Parameters.AddWithValue("@len",   lengthSec);
        cmd.Parameters.AddWithValue("@adm",   adminSec);
        cmd.Parameters.AddWithValue("@free",  isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@sum",   (object?)summary ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        using var rowIdCmd = conn.CreateCommand();
        rowIdCmd.CommandText = "SELECT last_insert_rowid()";
        return (int)(long)rowIdCmd.ExecuteScalar()!;
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
               @len, @adm, @free, @sum, datetime('now'))";
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
        return (int)(long)rowIdCmd.ExecuteScalar()!;
    }

    public int AddCsReview(
        int csId, int sessionId, int reviewSec, string status, string? notes)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cs_reviews
              (SessionId, CsId, ReviewLengthSeconds, ReviewedAt, Status, Notes)
            VALUES
              (@sid, @csId, @rev, datetime('now'), @status, @notes)";
        cmd.Parameters.AddWithValue("@sid",    sessionId);
        cmd.Parameters.AddWithValue("@csId",   csId);
        cmd.Parameters.AddWithValue("@rev",    reviewSec);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@notes",  (object?)notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        using var rowIdCmd = conn.CreateCommand();
        rowIdCmd.CommandText = "SELECT last_insert_rowid()";
        return (int)(long)rowIdCmd.ExecuteScalar()!;
    }

    public void UpdateSession(int sessionId, int lengthSec, int adminSec, bool isFree, string? summary)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_sessions
            SET LengthSeconds=@len, AdminSeconds=@adm, IsFreeSession=@free, SessionSummaryHtml=@sum
            WHERE SessionId=@id";
        cmd.Parameters.AddWithValue("@len",  lengthSec);
        cmd.Parameters.AddWithValue("@adm",  adminSec);
        cmd.Parameters.AddWithValue("@free", isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@sum",  (object?)summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id",   sessionId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateCsReview(int csReviewId, int reviewSec, string status, string? notes)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE cs_reviews
            SET ReviewLengthSeconds=@rev, Status=@status, Notes=@notes
            WHERE CsReviewId=@id";
        cmd.Parameters.AddWithValue("@rev",    reviewSec);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@notes",  (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id",     csReviewId);
        cmd.ExecuteNonQuery();
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
        var auditorPcIds = userPcs.Where(p => p.WorkCapacity == "Auditor").Select(p => p.PcId).ToList();
        var csPcIds      = userPcs.Where(p => p.WorkCapacity == "CS").Select(p => p.PcId).ToList();
        var miscPcIds    = userPcs.Where(p => p.WorkCapacity == "Miscellaneous").Select(p => p.PcId).ToList();

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
                WHERE s.AuditorId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate >= @start AND s.PcId != s.AuditorId
                GROUP BY s.SessionDate, s.PcId";
            cmd.Parameters.AddWithValue("@uid",   userId);
            cmd.Parameters.AddWithValue("@start", startStr);
            using var r = cmd.ExecuteReader();
            while (r.Read()) Accumulate(r.GetString(0), r.GetInt32(2), r.GetString(1));
        }

        // CS columns intentionally excluded from weekly totals graph

        if (miscPcIds.Count > 0)
        {
            var pcList = string.Join(",", miscPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT m.ChargeDate,
                       TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS FullName,
                       SUM(m.LengthSeconds + m.AdminSeconds)
                FROM sess_misc_charges m
                JOIN core_persons p ON p.PersonId = m.PcId
                WHERE m.AuditorId = @uid AND m.PcId IN ({pcList}) AND m.ChargeDate >= @start
                GROUP BY m.ChargeDate, m.PcId";
            cmd.Parameters.AddWithValue("@uid",   userId);
            cmd.Parameters.AddWithValue("@start", startStr);
            using var r = cmd.ExecuteReader();
            while (r.Read()) Accumulate(r.GetString(0), r.GetInt32(2), r.GetString(1));
        }

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
            VALUES (@csId, @pcId, @date, @len, @notes, datetime('now'))";
        cmd.Parameters.AddWithValue("@csId", csId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@len",  lengthSec);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        using var rowIdCmd = conn.CreateCommand();
        rowIdCmd.CommandText = "SELECT last_insert_rowid()";
        return (int)(long)rowIdCmd.ExecuteScalar()!;
    }

    public static DateOnly GetWeekStart(DateOnly d)
    {
        int offset = ((int)d.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
        return d.AddDays(-offset);
    }

    public static string Fmt(int s) =>
        s == 0 ? "-" : $"{s / 3600}:{(s % 3600) / 60:D2}";

    /// Returns a formatted H:MM string, or "" for zero/negative values (suitable for input pre-fill).
    public static string FmtOrBlank(int s) =>
        s <= 0 ? "" : $"{s / 3600}:{(s % 3600) / 60:D2}";

    /// Grid-key convention: CSSolo columns use -PcId so same person can appear in both columns.
    public static int GKey(PcInfo pc) => pc.WorkCapacity == "CSSolo" ? -(pc.PcId) : pc.PcId;

    public HashSet<(int pcId, int dayIndex)> GetPendingCsMarkers(
    int csId, DateOnly weekStart, List<PcInfo> userPcs)
    {
        var result = new HashSet<(int pcId, int dayIndex)>();

        var regularPcIds = userPcs
            .Where(p => p.WorkCapacity != "Miscellaneous" && p.WorkCapacity != "CSSolo")
            .Select(p => p.PcId)
            .ToList();
        var soloPcIds = userPcs
            .Where(p => p.WorkCapacity == "CSSolo")
            .Select(p => p.PcId)
            .ToList();

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
                  AND s.PcId {(solo ? "=" : "!=")} s.AuditorId
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
            WHERE s.PcId = s.AuditorId AND cr.CsReviewId IS NULL";
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
            WHERE s.PcId != s.AuditorId AND cr.CsReviewId IS NULL";
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
        cmd.CommandText = "SELECT 1 FROM sess_auditors WHERE AuditorId = @id AND IsActive = 1 AND Type IN (2, 3)";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() is not null;
    }

    public int GetAuditorType(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Type FROM sess_auditors WHERE AuditorId = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : 1; // default RegularOnly if not found
    }

    public PcInfo? GetSoloAuditorInfo(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
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
            WHERE AuditorId = @uid AND PcId = AuditorId AND SessionDate IN ({dateList})
            GROUP BY SessionDate";
        cmd.Parameters.AddWithValue("@uid", userId);
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
            WHERE AuditorId = @uid AND PcId = AuditorId AND SessionDate >= @start
            GROUP BY SessionDate";
        cmd.Parameters.AddWithValue("@uid",   userId);
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
        int auditorId, DateOnly date,
        int lengthSec, int adminSec, bool isFree, string? summary)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var dateStr = date.ToString("yyyy-MM-dd");

        // Ensure auditor exists in PCs (solo sessions use PcId = AuditorId)
        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = "INSERT OR IGNORE INTO core_pcs (PcId) VALUES (@id)";
        pcCmd.Parameters.AddWithValue("@id", auditorId);
        pcCmd.ExecuteNonQuery();

        using var seqCmd = conn.CreateCommand();
        seqCmd.CommandText = @"
            SELECT COALESCE(MAX(SequenceInDay), 0) + 1
            FROM sess_sessions
            WHERE AuditorId = @uid AND PcId = AuditorId AND SessionDate = @date";
        seqCmd.Parameters.AddWithValue("@uid",  auditorId);
        seqCmd.Parameters.AddWithValue("@date", dateStr);
        var seq = (long)(seqCmd.ExecuteScalar() ?? 1L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sess_sessions
              (PcId, AuditorId, SessionDate, SequenceInDay,
               LengthSeconds, AdminSeconds, IsFreeSession,
               ChargeSeconds, ChargedRateCentsPerHour,
               SessionSummaryHtml, CreatedAt)
            VALUES
              (@pcId, @audId, @date, @seq,
               @len, @adm, @free,
               0, 0,
               @sum, datetime('now'))";
        cmd.Parameters.AddWithValue("@pcId",  auditorId);  // PcId = AuditorId for solo
        cmd.Parameters.AddWithValue("@audId", auditorId);
        cmd.Parameters.AddWithValue("@date",  dateStr);
        cmd.Parameters.AddWithValue("@seq",   seq);
        cmd.Parameters.AddWithValue("@len",   lengthSec);
        cmd.Parameters.AddWithValue("@adm",   adminSec);
        cmd.Parameters.AddWithValue("@free",  isFree ? 1 : 0);
        cmd.Parameters.AddWithValue("@sum",   (object?)summary ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        using var rowIdCmd = conn.CreateCommand();
        rowIdCmd.CommandText = "SELECT last_insert_rowid()";
        return (int)(long)rowIdCmd.ExecuteScalar()!;
    }

    /// Returns a map of pcId → first name of the CS who last reviewed a session for that PC.
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

        // Solo mode: only solo sessions
        if (soloMode)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
            SELECT 1
            FROM sess_sessions
            WHERE AuditorId = @uid AND PcId = AuditorId AND SessionDate IN ({dateList})
            LIMIT 1";
            cmd.Parameters.AddWithValue("@uid", userId);
            return cmd.ExecuteScalar() is not null;
        }

        // Regular mode: sessions + misc (CS excluded anyway from graph)
        var auditorPcIds = userPcs.Where(p => p.WorkCapacity == "Auditor").Select(p => p.PcId).ToList();
        var miscPcIds = userPcs.Where(p => p.WorkCapacity == "Miscellaneous").Select(p => p.PcId).ToList();

        if (auditorPcIds.Count > 0)
        {
            var pcList = string.Join(",", auditorPcIds);
            using var sCmd = conn.CreateCommand();
            sCmd.CommandText = $@"
            SELECT 1
            FROM sess_sessions
            WHERE AuditorId = @uid AND PcId != AuditorId AND PcId IN ({pcList}) AND SessionDate IN ({dateList})
            LIMIT 1";
            sCmd.Parameters.AddWithValue("@uid", userId);
            if (sCmd.ExecuteScalar() is not null) return true;
        }

        if (miscPcIds.Count > 0)
        {
            var pcList = string.Join(",", miscPcIds);
            using var mCmd = conn.CreateCommand();
            mCmd.CommandText = $@"
            SELECT 1
            FROM sess_misc_charges
            WHERE AuditorId = @uid AND PcId IN ({pcList}) AND ChargeDate IN ({dateList})
            LIMIT 1";
            mCmd.Parameters.AddWithValue("@uid", userId);
            if (mCmd.ExecuteScalar() is not null) return true;
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
                ? "s.VerifiedStatus IN ('Pending','Approved')"
                : "s.VerifiedStatus != 'Approved'");

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
                   TRIM(pa.FirstName || ' ' || COALESCE(NULLIF(pa.LastName,''), '')) AS AuditorName,
                   s.SessionDate, s.LengthSeconds, s.AdminSeconds, s.IsFreeSession,
                   s.ChargedRateCentsPerHour, s.AuditorSalaryCentsPerHour,
                   s.VerifiedStatus, s.AuditorId
            FROM sess_sessions s
            JOIN core_persons pa ON pa.PersonId = s.AuditorId
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
            includeApproved ? "1=1" : "cr.Status != 'Approved'");

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
                   s.SessionDate, cr.ReviewLengthSeconds, cr.CsSalaryCentsPerHour, cr.Status
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
                r.GetString(6), r.GetInt32(7), r.GetInt32(8),
                r.IsDBNull(9) ? "Pending" : r.GetString(9));

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ChargedRateCentsPerHour, AuditorSalaryCentsPerHour
            FROM sess_sessions
            WHERE AuditorId = @aud AND PcId = @pc AND VerifiedStatus = 'Approved'
            ORDER BY SessionDate DESC, SequenceInDay DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@aud", auditorId);
        cmd.Parameters.AddWithValue("@pc",  pcId);
        using var r = cmd.ExecuteReader();
        if (r.Read())
            return (r.GetInt32(0), r.GetInt32(1));
        return GetLastApprovedDefaults(auditorId);
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
            WHERE s.PcId = @pc AND cr.Status = 'Approved'
            ORDER BY s.SessionDate DESC, s.SequenceInDay DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pc", pcId);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : 0;
    }

    public void ApproveSession(int sessionId, int chargedRateCents, int auditorSalaryCents)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sess_sessions
            SET VerifiedStatus = 'Approved',
                ChargedRateCentsPerHour = @rate,
                AuditorSalaryCentsPerHour = @salary
            WHERE SessionId = @id";
        cmd.Parameters.AddWithValue("@rate",   chargedRateCents);
        cmd.Parameters.AddWithValue("@salary", auditorSalaryCents);
        cmd.Parameters.AddWithValue("@id",     sessionId);
        cmd.ExecuteNonQuery();
    }

    public void ApproveCsReview(int csReviewId, int csSalaryCents)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE cs_reviews
            SET Status = 'Approved', CsSalaryCentsPerHour = @salary
            WHERE CsReviewId = @id";
        cmd.Parameters.AddWithValue("@salary", csSalaryCents);
        cmd.Parameters.AddWithValue("@id",     csReviewId);
        cmd.ExecuteNonQuery();
    }

    /// Returns session IDs that have a cs_review for a given PC.
    public HashSet<int> GetCsedSessionIdsForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT cr.SessionId FROM cs_reviews cr
            JOIN sess_sessions s ON s.SessionId = cr.SessionId
            WHERE s.PcId = @pcId";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var set = new HashSet<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            set.Add(r.GetInt32(0));
        return set;
    }

    /// Returns all sessions for a PC: (SessionId, Name).
    public List<(int SessionId, string Name)> GetSessionsForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT SessionId, COALESCE(Name,'') FROM sess_sessions
            WHERE PcId = @pcId";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<(int, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1)));
        return list;
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

    public record PendingCsSession(
        int SessionId, int PcId, string PcName, string SessionName,
        string SessionDate, int TotalSeconds, bool IsSolo);

    /// Returns all sessions from the last <paramref name="lookbackDays"/> days that have no CS review yet.
    public List<PendingCsSession> GetPendingCsSessions(int lookbackDays = 10)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-lookbackDays)).ToString("yyyy-MM-dd");
        cmd.CommandText = $@"
            SELECT s.SessionId, s.PcId,
                   {FullNameExpr} AS PcName,
                   COALESCE(s.Name, '') AS SessionName,
                   s.SessionDate,
                   COALESCE(s.LengthSeconds,0) + COALESCE(s.AdminSeconds,0) AS TotalSec,
                   CASE WHEN s.PcId = s.AuditorId THEN 1 ELSE 0 END AS IsSolo
            FROM sess_sessions s
            JOIN core_persons p ON p.PersonId = s.PcId
            LEFT JOIN cs_reviews cr ON cr.SessionId = s.SessionId
            WHERE cr.CsReviewId IS NULL
              AND s.SessionDate >= @cutoff
            ORDER BY PcName, s.SessionDate, s.SequenceInDay";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        var list = new List<PendingCsSession>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PendingCsSession(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.IsDBNull(5) ? 0 : r.GetInt32(5),
                r.GetInt32(6) == 1));
        }
        return list;
    }

    // ── Staff Messaging ─────────────────────────────────────────

    public List<StaffMember> GetActiveStaffMembers()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT p.PersonId, {FullNameExpr} AS FullName
            FROM core_persons p
            WHERE EXISTS (SELECT 1 FROM sess_auditors a WHERE a.AuditorId = p.PersonId AND a.IsActive = 1)
               OR EXISTS (SELECT 1 FROM cs_case_supervisors cs WHERE cs.CsId = p.PersonId AND cs.IsActive = 1)
               OR EXISTS (
                   SELECT 1 FROM core_users u
                   JOIN core_user_roles ur ON ur.UserId = u.Id
                   WHERE LOWER(u.Username) = LOWER(p.FirstName) AND u.IsActive = 1
               )
            GROUP BY p.PersonId
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

    public void AcknowledgeMessage(int messageId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sys_staff_messages SET AcknowledgedAt = datetime('now') WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", messageId);
        cmd.ExecuteNonQuery();
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
            VALUES (@aud, @wk, @rem, datetime('now'))
            ON CONFLICT(AuditorId, WeekDate)
            DO UPDATE SET Remarks = @rem, SubmittedAt = datetime('now')";
        cmd.Parameters.AddWithValue("@aud", auditorId);
        cmd.Parameters.AddWithValue("@wk", weekDate);
        cmd.Parameters.AddWithValue("@rem", remarks ?? "");
        cmd.ExecuteNonQuery();
    }
}
