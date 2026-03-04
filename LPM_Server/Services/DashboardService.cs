using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LPM.Services;

public record PcInfo(int PcId, string FullName, string WorkCapacity, bool IsSolo = false);
public record SessionRow(int SessionId, int LengthSec, int AdminSec, bool IsFree, string? Summary, string CreatedAt, string AuditorName);
public record CsReviewRow(int CsReviewId, int SessionId, int ReviewSec, string Status, string? Notes);
public record CsWorkRow(int CsWorkLogId, int LengthSec, string? Notes, string CreatedAt);
public record WeekTotal(DateOnly WeekStart, int TotalSeconds)
{
    public string WeekLabel => WeekStart.ToString("dd/MM", CultureInfo.InvariantCulture);
}
public record DayDetail(List<SessionRow> Sessions, List<CsReviewRow> Reviews, List<CsWorkRow>? GeneralWork = null);

public class DashboardService
{
    private readonly string _connectionString;

    // Reusable SQL expression for a person's display name (requires alias p for Persons)
    private const string FullNameExpr =
        "TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), ''))";

    public DashboardService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        RunMigrations();
    }

    private void RunMigrations()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Add Type column to Auditors if not yet present
        // Type: 0=InActive, 1=RegularOnly, 2=SoloOnly, 3=RegularAndSolo
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Auditors') WHERE name='Type'";
        var typeExists = (long)(checkCmd.ExecuteScalar() ?? 0L) > 0;
        if (!typeExists)
        {
            // Default all auditors to RegularOnly (1)
            using var addCol = conn.CreateCommand();
            addCol.CommandText = "ALTER TABLE Auditors ADD COLUMN Type INTEGER NOT NULL DEFAULT 1";
            addCol.ExecuteNonQuery();

            // Tami (PersonId=1) and Aviv (PersonId=6) → RegularAndSolo
            using var tavCmd = conn.CreateCommand();
            tavCmd.CommandText = "UPDATE Auditors SET Type = 3 WHERE AuditorId IN (1, 6)";
            tavCmd.ExecuteNonQuery();

            // 'Solo' user (matched by Persons.FirstName) → SoloOnly
            using var soloCmd = conn.CreateCommand();
            soloCmd.CommandText = @"
                UPDATE Auditors SET Type = 2
                WHERE AuditorId = (
                    SELECT PersonId FROM Persons WHERE LOWER(FirstName) = 'solo' LIMIT 1
                )
                AND AuditorId NOT IN (1, 6)";
            soloCmd.ExecuteNonQuery();

            // Inactive auditors → InActive type
            using var inactCmd = conn.CreateCommand();
            inactCmd.CommandText = "UPDATE Auditors SET Type = 0 WHERE IsActive = 0";
            inactCmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Returns the PersonId for the given username (matches Persons.FirstName case-insensitively).
    /// </summary>
    public int? GetUserIdByUsername(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PersonId FROM Persons WHERE LOWER(FirstName) = LOWER(@u) LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : null;
    }

    public bool IsAuditor(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Auditors WHERE AuditorId = @id AND IsActive = 1 AND Type IN (1, 3)";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() is not null;
    }

    public bool IsCS(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM CaseSupervisors WHERE CsId = @id AND IsActive = 1";
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
                   spl.IsSolo
            FROM StaffPcList spl
            JOIN PCs     pc ON pc.PcId    = spl.PcId
            JOIN Persons p  ON p.PersonId = pc.PcId
            WHERE spl.UserId = @uid
            ORDER BY p.FirstName, p.LastName, spl.IsSolo";
        cmd.Parameters.AddWithValue("@uid", userId);
        var list = new List<PcInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcInfo(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3) == 1));
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
                   {FullNameExpr} AS FullName
            FROM PCs     pc
            JOIN Persons p ON p.PersonId = pc.PcId
            ORDER BY p.FirstName, p.LastName";
        var list = new List<PcInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcInfo(r.GetInt32(0), r.GetString(1), "Auditor", false));

        // Extra solo entries for PCs that are also solo auditors (Type IN (2,3) in Auditors)
        using var soloCmd = conn.CreateCommand();
        soloCmd.CommandText = $@"
            SELECT pc.PcId,
                   {FullNameExpr} AS FullName
            FROM Auditors a
            JOIN PCs      pc ON pc.PcId    = a.AuditorId
            JOIN Persons  p  ON p.PersonId = a.AuditorId
            WHERE a.Type IN (2, 3) AND a.IsActive = 1
            ORDER BY p.FirstName, p.LastName";
        using var rs = soloCmd.ExecuteReader();
        while (rs.Read())
            list.Add(new PcInfo(rs.GetInt32(0), rs.GetString(1) + " (Solo)", "CS", true));

        return list;
    }

    public void AddUserPc(int userId, int pcId, bool isSolo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO StaffPcList (UserId, PcId, WorkCapacity, IsSolo) VALUES (@uid, @pcId, @cap, @solo)";
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@cap",  isSolo ? "CS" : "Auditor");
        cmd.Parameters.AddWithValue("@solo", isSolo ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void RemoveUserPc(int userId, int pcId, bool isSolo = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM StaffPcList WHERE UserId = @uid AND PcId = @pcId AND IsSolo = @solo";
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@solo", isSolo ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void SetUserPcRole(int userId, int pcId, string role)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // IsSolo=0 guard: solo columns are always CS, their role is not changed here
        cmd.CommandText = "UPDATE StaffPcList SET WorkCapacity = @role WHERE UserId = @uid AND PcId = @pcId AND IsSolo = 0";
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

        var auditorPcIds = userPcs.Where(p => p.WorkCapacity == "Auditor"       && !p.IsSolo).Select(p => p.PcId).ToList();
        var csPcIds      = userPcs.Where(p => p.WorkCapacity == "CS"            && !p.IsSolo).Select(p => p.PcId).ToList();
        var soloCSPcIds  = userPcs.Where(p => p.WorkCapacity == "CS"            &&  p.IsSolo).Select(p => p.PcId).ToList();
        var miscPcIds    = userPcs.Where(p => p.WorkCapacity == "Miscellaneous" && !p.IsSolo).Select(p => p.PcId).ToList();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        if (auditorPcIds.Count > 0)
        {
            var pcList = string.Join(",", auditorPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT PcId, SessionDate, SUM(LengthSeconds + AdminSeconds)
                FROM Sessions
                WHERE AuditorId = @uid AND PcId IN ({pcList}) AND SessionDate IN ({dateList}) AND IsSolo = 0
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
                FROM CsReviews cr
                JOIN Sessions s ON s.SessionId = cr.SessionId
                WHERE cr.CsId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate IN ({dateList}) AND s.IsSolo = 0
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
                FROM CsWorkLog
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

        // Solo CS columns: CS reviewing sessions where the PC is the auditor (IsSolo=1)
        // Grid key uses -pcId to distinguish from the same person's regular column
        if (soloCSPcIds.Count > 0)
        {
            var pcList = string.Join(",", soloCSPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.PcId, s.SessionDate, SUM(cr.ReviewLengthSeconds)
                FROM CsReviews cr
                JOIN Sessions s ON s.SessionId = cr.SessionId
                WHERE cr.CsId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate IN ({dateList}) AND s.IsSolo = 1
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
                FROM MiscCharge
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
    public DayDetail GetDayDetail(int userId, int pcId, DateOnly date, string role, bool isSolo = false)
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
                       p.FirstName
                FROM Sessions s
                JOIN Persons p ON p.PersonId = s.AuditorId
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
                    r.IsDBNull(6) ? ""   : r.GetString(6)));
            }
        }
        else if (role == "Miscellaneous")
        {
            // MiscCharge entries are per-auditor — each auditor sees only their own rows
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT MiscChargeId, LengthSeconds, AdminSeconds,
                       IsFree, Summary, CreatedAt
                FROM MiscCharge
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
                       IsFreeSession, SessionSummaryHtml, CreatedAt
                FROM Sessions
                WHERE AuditorId = @uid AND IsSolo = 1 AND SessionDate = @date
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
                    ""));
            }
        }
        else  // CS role
        {
            // All sessions for this PC+date from any auditor, with auditor's first name
            // For solo CS column: only IsSolo=1 sessions; for regular CS: only IsSolo=0
            using var sessCmd = conn.CreateCommand();
            sessCmd.CommandText = $@"
                SELECT s.SessionId, s.LengthSeconds, s.AdminSeconds,
                       s.IsFreeSession, s.SessionSummaryHtml, s.CreatedAt,
                       p.FirstName
                FROM Sessions s
                JOIN Persons p ON p.PersonId = s.AuditorId
                WHERE s.PcId = @pcId AND s.SessionDate = @date AND s.IsSolo = {(isSolo ? 1 : 0)}
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
                    rs.GetString(6)));
            }

            // All reviews for those sessions (by any CS worker — UNIQUE per session anyway)
            using var revCmd = conn.CreateCommand();
            revCmd.CommandText = @"
                SELECT cr.CsReviewId, cr.SessionId, cr.ReviewLengthSeconds,
                       cr.Status, cr.Notes
                FROM CsReviews cr
                JOIN Sessions s ON s.SessionId = cr.SessionId
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
                FROM CsWorkLog
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
            FROM Sessions
            WHERE PcId = @pcId AND SessionDate = @date";
        seqCmd.Parameters.AddWithValue("@pcId", pcId);
        seqCmd.Parameters.AddWithValue("@date", dateStr);
        var seq = (long)(seqCmd.ExecuteScalar() ?? 1L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Sessions
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
            FROM MiscCharge
            WHERE AuditorId = @uid AND PcId = @pcId AND ChargeDate = @date";
        seqCmd.Parameters.AddWithValue("@uid",  auditorId);
        seqCmd.Parameters.AddWithValue("@pcId", pcId);
        seqCmd.Parameters.AddWithValue("@date", dateStr);
        var seq = (long)(seqCmd.ExecuteScalar() ?? 1L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO MiscCharge
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
            INSERT INTO CsReviews
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
        var auditorPcIds = userPcs.Where(p => p.WorkCapacity == "Auditor"       && !p.IsSolo).Select(p => p.PcId).ToList();
        var csPcIds      = userPcs.Where(p => p.WorkCapacity == "CS"            && !p.IsSolo).Select(p => p.PcId).ToList();
        var miscPcIds    = userPcs.Where(p => p.WorkCapacity == "Miscellaneous" && !p.IsSolo).Select(p => p.PcId).ToList();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        void Accumulate(string dateStr, int secs)
        {
            if (!DateOnly.TryParse(dateStr, out var d)) return;
            var ws = GetWeekStart(d);
            if (result.ContainsKey(ws)) result[ws] += secs;
        }

        if (auditorPcIds.Count > 0)
        {
            var pcList = string.Join(",", auditorPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT SessionDate, SUM(LengthSeconds + AdminSeconds)
                FROM Sessions
                WHERE AuditorId = @uid AND PcId IN ({pcList}) AND SessionDate >= @start AND IsSolo = 0
                GROUP BY SessionDate";
            cmd.Parameters.AddWithValue("@uid",   userId);
            cmd.Parameters.AddWithValue("@start", startStr);
            using var r = cmd.ExecuteReader();
            while (r.Read()) Accumulate(r.GetString(0), r.GetInt32(1));
        }

        // CS columns intentionally excluded from weekly totals graph

        if (miscPcIds.Count > 0)
        {
            var pcList = string.Join(",", miscPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT ChargeDate, SUM(LengthSeconds + AdminSeconds)
                FROM MiscCharge
                WHERE AuditorId = @uid AND PcId IN ({pcList}) AND ChargeDate >= @start
                GROUP BY ChargeDate";
            cmd.Parameters.AddWithValue("@uid",   userId);
            cmd.Parameters.AddWithValue("@start", startStr);
            using var r = cmd.ExecuteReader();
            while (r.Read()) Accumulate(r.GetString(0), r.GetInt32(1));
        }

        return weeks.Select(w => new WeekTotal(w, result[w])).ToList();
    }

    public int AddCsWork(int csId, int pcId, DateOnly date, int lengthSec, string? notes)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO CsWorkLog (CsId, PcId, WorkDate, LengthSeconds, Notes, CreatedAt)
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

    /// Grid-key convention: solo-CS columns use -PcId so same person can appear in both columns.
    public static int GKey(PcInfo pc) => pc.IsSolo ? -(pc.PcId) : pc.PcId;

    public HashSet<(int pcId, int dayIndex)> GetPendingCsMarkers(
    int csId, DateOnly weekStart, List<PcInfo> userPcs)
    {
        var result = new HashSet<(int pcId, int dayIndex)>();

        var regularPcIds = userPcs
            .Where(p => p.WorkCapacity != "Miscellaneous" && !p.IsSolo)
            .Select(p => p.PcId)
            .ToList();
        var soloPcIds = userPcs
            .Where(p => p.WorkCapacity != "Miscellaneous" && p.IsSolo)
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
                FROM Sessions s
                LEFT JOIN CsReviews cr ON cr.SessionId = s.SessionId
                WHERE s.PcId IN ({pcList})
                  AND s.SessionDate IN ({dateList})
                  AND s.IsSolo = {(solo ? 1 : 0)}
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

    // ── Solo Auditor methods ──

    public bool IsSoloAuditor(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Auditors WHERE AuditorId = @id AND IsActive = 1 AND Type IN (2, 3)";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() is not null;
    }

    public int GetAuditorType(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Type FROM Auditors WHERE AuditorId = @id LIMIT 1";
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
            FROM Persons WHERE PersonId = @id";
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
            FROM Sessions
            WHERE AuditorId = @uid AND IsSolo = 1 AND SessionDate IN ({dateList})
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
            FROM Sessions
            WHERE AuditorId = @uid AND IsSolo = 1 AND SessionDate >= @start
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

        return weeks.Select(w => new WeekTotal(w, result[w])).ToList();
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
        pcCmd.CommandText = "INSERT OR IGNORE INTO PCs (PcId) VALUES (@id)";
        pcCmd.Parameters.AddWithValue("@id", auditorId);
        pcCmd.ExecuteNonQuery();

        using var seqCmd = conn.CreateCommand();
        seqCmd.CommandText = @"
            SELECT COALESCE(MAX(SequenceInDay), 0) + 1
            FROM Sessions
            WHERE AuditorId = @uid AND IsSolo = 1 AND SessionDate = @date";
        seqCmd.Parameters.AddWithValue("@uid",  auditorId);
        seqCmd.Parameters.AddWithValue("@date", dateStr);
        var seq = (long)(seqCmd.ExecuteScalar() ?? 1L);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Sessions
              (PcId, AuditorId, SessionDate, SequenceInDay,
               LengthSeconds, AdminSeconds, IsFreeSession,
               ChargeSeconds, ChargedRateCentsPerHour,
               SessionSummaryHtml, CreatedAt, IsSolo)
            VALUES
              (@pcId, @audId, @date, @seq,
               @len, @adm, @free,
               0, 0,
               @sum, datetime('now'), 1)";
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
                FROM CsReviews cr2
                JOIN Sessions s2 ON s2.SessionId = cr2.SessionId
                WHERE s2.PcId IN ({pcList})
                GROUP BY s2.PcId
            ) latest
            JOIN CsReviews cr ON cr.CsReviewId = latest.MaxId
            JOIN Sessions s   ON s.SessionId   = cr.SessionId
            JOIN Persons p    ON p.PersonId    = cr.CsId";

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
            FROM Sessions
            WHERE AuditorId = @uid AND IsSolo = 1 AND SessionDate IN ({dateList})
            LIMIT 1";
            cmd.Parameters.AddWithValue("@uid", userId);
            return cmd.ExecuteScalar() is not null;
        }

        // Regular mode: sessions + misc (CS excluded anyway from graph)
        var auditorPcIds = userPcs.Where(p => p.WorkCapacity == "Auditor" && !p.IsSolo).Select(p => p.PcId).ToList();
        var miscPcIds = userPcs.Where(p => p.WorkCapacity == "Miscellaneous" && !p.IsSolo).Select(p => p.PcId).ToList();

        if (auditorPcIds.Count > 0)
        {
            var pcList = string.Join(",", auditorPcIds);
            using var sCmd = conn.CreateCommand();
            sCmd.CommandText = $@"
            SELECT 1
            FROM Sessions
            WHERE AuditorId = @uid AND IsSolo = 0 AND PcId IN ({pcList}) AND SessionDate IN ({dateList})
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
            FROM MiscCharge
            WHERE AuditorId = @uid AND PcId IN ({pcList}) AND ChargeDate IN ({dateList})
            LIMIT 1";
            mCmd.Parameters.AddWithValue("@uid", userId);
            if (mCmd.ExecuteScalar() is not null) return true;
        }

        return false;
    }
}
