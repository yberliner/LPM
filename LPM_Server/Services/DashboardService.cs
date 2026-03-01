using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LPM.Services;

public record PcInfo(int PcId, string FullName, string Role);
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

    public DashboardService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        EnsureStaffPcListTable();
        EnsureCsWorkLogTable();
        EnsureMiscChargeTable();
    }

    private void EnsureStaffPcListTable()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var create = conn.CreateCommand();
        create.CommandText = @"
            CREATE TABLE IF NOT EXISTS StaffPcList (
              Id     INTEGER PRIMARY KEY AUTOINCREMENT,
              UserId INTEGER NOT NULL,
              PcId   INTEGER NOT NULL,
              UNIQUE (UserId, PcId),
              FOREIGN KEY (UserId) REFERENCES Persons(PersonId) ON DELETE CASCADE,
              FOREIGN KEY (PcId)   REFERENCES PCs(PcId)         ON DELETE CASCADE
            )";
        create.ExecuteNonQuery();

        // Migrate: add Role column to existing tables that don't yet have it
        try
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE StaffPcList ADD COLUMN Role TEXT NOT NULL DEFAULT 'Auditor'";
            alter.ExecuteNonQuery();
        }
        catch { /* column already exists */ }
    }

    private void EnsureCsWorkLogTable()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS CsWorkLog (
              CsWorkLogId   INTEGER PRIMARY KEY AUTOINCREMENT,
              CsId          INTEGER NOT NULL,
              PcId          INTEGER NOT NULL,
              WorkDate      TEXT    NOT NULL,
              LengthSeconds INTEGER NOT NULL DEFAULT 0,
              Notes         TEXT,
              CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now')),
              FOREIGN KEY (CsId) REFERENCES CaseSupervisors(CsId),
              FOREIGN KEY (PcId) REFERENCES PCs(PcId)
            )";
        cmd.ExecuteNonQuery();
    }

    private void EnsureMiscChargeTable()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS MiscCharge (
              MiscChargeId  INTEGER PRIMARY KEY AUTOINCREMENT,
              AuditorId     INTEGER NOT NULL,
              PcId          INTEGER NOT NULL,
              ChargeDate    TEXT    NOT NULL,
              SequenceInDay INTEGER NOT NULL DEFAULT 1,
              LengthSeconds INTEGER NOT NULL DEFAULT 0,
              AdminSeconds  INTEGER NOT NULL DEFAULT 0,
              IsFree        INTEGER NOT NULL DEFAULT 0,
              Summary       TEXT,
              CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now')),
              FOREIGN KEY (AuditorId) REFERENCES Persons(PersonId),
              FOREIGN KEY (PcId)      REFERENCES PCs(PcId)
            )";
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
        cmd.CommandText = "SELECT 1 FROM Auditors WHERE AuditorId = @id AND IsActive = 1";
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
        cmd.CommandText = @"
            SELECT pc.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   spl.Role
            FROM StaffPcList spl
            JOIN PCs     pc ON pc.PcId    = spl.PcId
            JOIN Persons p  ON p.PersonId = pc.PcId
            WHERE spl.UserId = @uid
            ORDER BY p.FirstName, p.LastName";
        cmd.Parameters.AddWithValue("@uid", userId);
        var list = new List<PcInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcInfo(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public List<PcInfo> GetAllPcs()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   'Auditor'
            FROM PCs     pc
            JOIN Persons p ON p.PersonId = pc.PcId
            ORDER BY p.FirstName, p.LastName";
        var list = new List<PcInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcInfo(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public void AddUserPc(int userId, int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO StaffPcList (UserId, PcId, Role) VALUES (@uid, @pcId, 'Auditor')";
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.ExecuteNonQuery();
    }

    public void RemoveUserPc(int userId, int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM StaffPcList WHERE UserId = @uid AND PcId = @pcId";
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.ExecuteNonQuery();
    }

    public void SetUserPcRole(int userId, int pcId, string role)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE StaffPcList SET Role = @role WHERE UserId = @uid AND PcId = @pcId";
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

        var auditorPcIds = userPcs.Where(p => p.Role == "Auditor").Select(p => p.PcId).ToList();
        var csPcIds      = userPcs.Where(p => p.Role == "CS").Select(p => p.PcId).ToList();
        var miscPcIds    = userPcs.Where(p => p.Role == "Miscellaneous").Select(p => p.PcId).ToList();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        if (auditorPcIds.Count > 0)
        {
            var pcList = string.Join(",", auditorPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT PcId, SessionDate, SUM(LengthSeconds + AdminSeconds)
                FROM Sessions
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
                FROM CsReviews cr
                JOIN Sessions s ON s.SessionId = cr.SessionId
                WHERE cr.CsId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate IN ({dateList})
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
                SELECT SessionId, LengthSeconds, AdminSeconds,
                       IsFreeSession, SessionSummaryHtml, CreatedAt
                FROM Sessions
                WHERE AuditorId = @uid AND PcId = @pcId AND SessionDate = @date
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
        else  // CS role
        {
            // All sessions for this PC+date from any auditor, with auditor's first name
            using var sessCmd = conn.CreateCommand();
            sessCmd.CommandText = @"
                SELECT s.SessionId, s.LengthSeconds, s.AdminSeconds,
                       s.IsFreeSession, s.SessionSummaryHtml, s.CreatedAt,
                       p.FirstName
                FROM Sessions s
                JOIN Persons p ON p.PersonId = s.AuditorId
                WHERE s.PcId = @pcId AND s.SessionDate = @date
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
        var auditorPcIds = userPcs.Where(p => p.Role == "Auditor").Select(p => p.PcId).ToList();
        var csPcIds      = userPcs.Where(p => p.Role == "CS").Select(p => p.PcId).ToList();
        var miscPcIds    = userPcs.Where(p => p.Role == "Miscellaneous").Select(p => p.PcId).ToList();

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
                WHERE AuditorId = @uid AND PcId IN ({pcList}) AND SessionDate >= @start
                GROUP BY SessionDate";
            cmd.Parameters.AddWithValue("@uid",   userId);
            cmd.Parameters.AddWithValue("@start", startStr);
            using var r = cmd.ExecuteReader();
            while (r.Read()) Accumulate(r.GetString(0), r.GetInt32(1));
        }

        if (csPcIds.Count > 0)
        {
            var pcList = string.Join(",", csPcIds);

            using var revCmd = conn.CreateCommand();
            revCmd.CommandText = $@"
                SELECT s.SessionDate, SUM(cr.ReviewLengthSeconds)
                FROM CsReviews cr
                JOIN Sessions s ON s.SessionId = cr.SessionId
                WHERE cr.CsId = @uid AND s.PcId IN ({pcList}) AND s.SessionDate >= @start
                GROUP BY s.SessionDate";
            revCmd.Parameters.AddWithValue("@uid",   userId);
            revCmd.Parameters.AddWithValue("@start", startStr);
            using var rr = revCmd.ExecuteReader();
            while (rr.Read()) Accumulate(rr.GetString(0), rr.GetInt32(1));

            using var wCmd = conn.CreateCommand();
            wCmd.CommandText = $@"
                SELECT WorkDate, SUM(LengthSeconds)
                FROM CsWorkLog
                WHERE CsId = @uid AND PcId IN ({pcList}) AND WorkDate >= @start
                GROUP BY WorkDate";
            wCmd.Parameters.AddWithValue("@uid",   userId);
            wCmd.Parameters.AddWithValue("@start", startStr);
            using var wr = wCmd.ExecuteReader();
            while (wr.Read()) Accumulate(wr.GetString(0), wr.GetInt32(1));
        }

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
}
