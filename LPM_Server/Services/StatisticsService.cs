using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LPM.Services;

public record StaffStatRow(int PersonId, string Name, int AuditSec, int SoloCsSec, int EffortSec = 0, int CsSec = 0)
{
    // Total intentionally excludes CsSec — CS (reviews on regular, non-solo sessions)
    // is shown informationally in its own column but is not summed into the leaderboard total.
    public int TotalSec => AuditSec + SoloCsSec + EffortSec;
}

public record DayStat(DateOnly Date, List<StaffStatRow> Staff, int AcademyCount, int BodyInShop, int PcCount, int EffortSec = 0);

public record OriginHours(string Origin, int Seconds);

public record StatCellDetailItem(string PcName, string Date, int Seconds);
public record StatCellDetail(List<StatCellDetailItem> Items, int TotalSec);

public record WeekStatSummary(DateOnly WeekStart, int TotalAuditCsSec, int AcademyCount, int BodyInShop, int PcCount, int EffortSec = 0)
{
    public string WeekRangeLabel =>
        $"{WeekStart:dd/MM} – {WeekStart.AddDays(6):dd/MM}";
}

public record MonthStatSummary(DateOnly MonthStart, DateOnly MonthEnd, int TotalAuditCsSec, int AcademyCount, int BodyInShop, int PcCount, int EffortSec = 0)
{
    public string MonthLabel =>
        MonthStart.ToString("MMM yyyy", CultureInfo.InvariantCulture);
}

public class StatisticsService
{
    private readonly string _connectionString;

    public StatisticsService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        // Schema is now managed directly in the DB; no runtime migrations needed.
    }

    /// <summary>
    /// Returns per-day stats for each of the 7 days in the week starting at weekStart.
    /// Staff are sorted by descending total (AuditSec + SoloCsSec) per day.
    /// If orgId is non-null, audit/CS/solo-CS/effort are filtered by staff person.Org,
    /// and academy is filtered by student person.Org.
    /// </summary>
    public List<DayStat> GetWeekDayStats(DateOnly weekStart, int? orgId = null)
    {
        var weekEnd  = weekStart.AddDays(6);
        var dates    = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var startStr = weekStart.ToString("yyyy-MM-dd");
        var endStr   = weekEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // ── Active staff names (filtered by staff person.Org when orgId set) ─
        var auditorNames = new Dictionary<int, string>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT u.PersonId,
                       TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS Name
                FROM core_users u
                JOIN core_persons p ON p.PersonId = u.PersonId
                WHERE u.IsActive = 1 AND u.StaffRole != 'None'"
                + (orgId.HasValue ? " AND p.Org = @org" : "");
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                auditorNames[r.GetInt32(0)] = r.GetString(1);
        }

        // ── Auditing seconds per (auditorId, dayIndex) ───────────────────────
        // Only regular sessions (AuditorId IS NOT NULL); solo sessions tracked separately
        var auditSecs = new Dictionary<(int pid, int day), int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.AuditorId, DATE(s.CreatedAt, 'localtime') AS d, SUM(s.LengthSeconds + s.AdminSeconds)
                FROM sess_sessions s
                { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                WHERE DATE(s.CreatedAt, 'localtime') >= @s AND DATE(s.CreatedAt, 'localtime') <= @e AND s.AuditorId IS NOT NULL
                { (orgId.HasValue ? "AND ap.Org = @org" : "") }
                GROUP BY s.AuditorId, DATE(s.CreatedAt, 'localtime')";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(1)));
                if (dayIdx < 0) continue;
                var key = (r.GetInt32(0), dayIdx);
                auditSecs[key] = auditSecs.GetValueOrDefault(key) + r.GetInt32(2);
            }
        }

        // ── Solo CS seconds per (csId, dayIndex) ─────────────────────────────
        // CS reviews on solo sessions (AuditorId IS NULL)
        var soloCsSecs = new Dictionary<(int pid, int day), int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cr.CsId, DATE(cr.ReviewedAt, 'localtime') AS d, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                { (orgId.HasValue ? "JOIN core_persons cp ON cp.PersonId = cr.CsId" : "") }
                WHERE DATE(cr.ReviewedAt, 'localtime') >= @s AND DATE(cr.ReviewedAt, 'localtime') <= @e
                  AND s.AuditorId IS NULL
                  { (orgId.HasValue ? "AND cp.Org = @org" : "") }
                GROUP BY cr.CsId, DATE(cr.ReviewedAt, 'localtime')";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(1)));
                if (dayIdx < 0) continue;
                var key = (r.GetInt32(0), dayIdx);
                soloCsSecs[key] = soloCsSecs.GetValueOrDefault(key) + r.GetInt32(2);
            }
        }

        // ── CS seconds per (csId, dayIndex) ──────────────────────────────────
        // CS reviews on regular (non-solo) sessions — AuditorId IS NOT NULL
        var csSecs = new Dictionary<(int pid, int day), int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cr.CsId, DATE(cr.ReviewedAt, 'localtime') AS d, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                { (orgId.HasValue ? "JOIN core_persons cp ON cp.PersonId = cr.CsId" : "") }
                WHERE DATE(cr.ReviewedAt, 'localtime') >= @s AND DATE(cr.ReviewedAt, 'localtime') <= @e
                  AND s.AuditorId IS NOT NULL
                  { (orgId.HasValue ? "AND cp.Org = @org" : "") }
                GROUP BY cr.CsId, DATE(cr.ReviewedAt, 'localtime')";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(1)));
                if (dayIdx < 0) continue;
                var key = (r.GetInt32(0), dayIdx);
                csSecs[key] = csSecs.GetValueOrDefault(key) + r.GetInt32(2);
            }
        }

        // ── Academy visit count per day (filtered by student person.Org) ──
        var academyCounts = new int[7];
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT a.VisitDate, COUNT(*)
                FROM acad_attendance a
                { (orgId.HasValue ? "JOIN core_persons sp ON sp.PersonId = a.PersonId" : "") }
                WHERE a.VisitDate >= @s AND a.VisitDate <= @e
                { (orgId.HasValue ? "AND sp.Org = @org" : "") }
                GROUP BY a.VisitDate";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(0)));
                if (dayIdx >= 0) academyCounts[dayIdx] = r.GetInt32(1);
            }
        }

        // ── Distinct PC count per day (sessions by filtered staff ∪ effort by filtered staff) ──
        var pcCounts = new int[7];
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT d, COUNT(DISTINCT pid) FROM (
                    SELECT s.SessionDate AS d, s.PcId AS pid FROM sess_sessions s
                    { (orgId.HasValue ? "LEFT JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                    WHERE  s.SessionDate >= @s AND s.SessionDate <= @e
                    { (orgId.HasValue ? "AND (s.AuditorId IS NOT NULL AND ap.Org = @org)" : "") }
                    UNION ALL
                    SELECT e.EffortDate  AS d, e.PcId AS pid FROM sys_effort_entries e
                    { (orgId.HasValue ? "JOIN core_users eu ON eu.Id = e.PerformedByUserId JOIN core_persons ep ON ep.PersonId = eu.PersonId" : "") }
                    WHERE  e.EffortDate  >= @s AND e.EffortDate <= @e
                    { (orgId.HasValue ? "AND ep.Org = @org" : "") }
                ) GROUP BY d";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(0)));
                if (dayIdx >= 0) pcCounts[dayIdx] = r.GetInt32(1);
            }
        }

        // ── Body in shop per day (sessions by filtered staff ∪ academy by filtered student ∪ effort by filtered staff) ──
        var bodyInShop = new int[7];
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT d, COUNT(DISTINCT pid) FROM (
                    SELECT s.SessionDate AS d, s.PcId     AS pid FROM sess_sessions s
                    { (orgId.HasValue ? "LEFT JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                    WHERE  s.SessionDate >= @s AND s.SessionDate <= @e
                    { (orgId.HasValue ? "AND (s.AuditorId IS NOT NULL AND ap.Org = @org)" : "") }
                    UNION ALL
                    SELECT a.VisitDate   AS d, a.PersonId AS pid FROM acad_attendance a
                    { (orgId.HasValue ? "JOIN core_persons sp ON sp.PersonId = a.PersonId" : "") }
                    WHERE  a.VisitDate   >= @s AND a.VisitDate   <= @e
                    { (orgId.HasValue ? "AND sp.Org = @org" : "") }
                    UNION ALL
                    SELECT e.EffortDate  AS d, e.PcId     AS pid FROM sys_effort_entries e
                    { (orgId.HasValue ? "JOIN core_users eu ON eu.Id = e.PerformedByUserId JOIN core_persons ep ON ep.PersonId = eu.PersonId" : "") }
                    WHERE  e.EffortDate  >= @s AND e.EffortDate  <= @e
                    { (orgId.HasValue ? "AND ep.Org = @org" : "") }
                ) GROUP BY d";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(0)));
                if (dayIdx >= 0) bodyInShop[dayIdx] = r.GetInt32(1);
            }
        }

        // ── Effort seconds per (personId, dayIndex) ──
        var effortSecs   = new Dictionary<(int pid, int day), int>();
        var effortPerDay = new int[7];
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT u.PersonId, e.EffortDate, SUM(e.LengthSeconds)
                FROM sys_effort_entries e
                JOIN core_users u ON u.Id = e.PerformedByUserId
                { (orgId.HasValue ? "JOIN core_persons ep ON ep.PersonId = u.PersonId" : "") }
                WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                { (orgId.HasValue ? "AND ep.Org = @org" : "") }
                GROUP BY u.PersonId, e.EffortDate";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(1)));
                if (dayIdx < 0) continue;
                int pid = r.GetInt32(0);
                int secs = r.GetInt32(2);
                var key = (pid, dayIdx);
                effortSecs[key]         = effortSecs.GetValueOrDefault(key) + secs;
                effortPerDay[dayIdx]   += secs;
            }
        }

        // ── Build per-day results ────────────────────────────────────────────
        var allPids = new HashSet<int>(auditorNames.Keys);
        foreach (var k in auditSecs.Keys)  allPids.Add(k.pid);
        foreach (var k in soloCsSecs.Keys) allPids.Add(k.pid);
        foreach (var k in csSecs.Keys)     allPids.Add(k.pid);
        foreach (var k in effortSecs.Keys) allPids.Add(k.pid);

        return Enumerable.Range(0, 7).Select(dayIdx =>
        {
            var staff = allPids
                .Select(pid =>
                {
                    string name = auditorNames.TryGetValue(pid, out var n) ? n : $"Person {pid}";
                    int audit = auditSecs.GetValueOrDefault((pid, dayIdx));
                    int solo  = soloCsSecs.GetValueOrDefault((pid, dayIdx));
                    int cs    = csSecs.GetValueOrDefault((pid, dayIdx));
                    int eff   = effortSecs.GetValueOrDefault((pid, dayIdx));
                    return new StaffStatRow(pid, name, audit, solo, eff, cs);
                })
                .Where(s => s.TotalSec > 0)
                .OrderByDescending(s => s.TotalSec)
                .ToList();

            return new DayStat(dates[dayIdx], staff, academyCounts[dayIdx], bodyInShop[dayIdx], pcCounts[dayIdx], effortPerDay[dayIdx]);
        }).ToList();
    }

    /// <summary>
    /// Returns weekly summary rows for the last numWeeks weeks ending with latestWeekStart.
    /// Each row has: TotalAuditCsSec (all audit + solo-type CS), AcademyCount (unique persons),
    /// BodyInShop (unique persons who had a session or visited academy).
    /// </summary>
    public List<WeekStatSummary> GetWeeklySummaries(DateOnly latestWeekStart, int numWeeks = 20, int? orgId = null)
    {
        var weeks    = Enumerable.Range(0, numWeeks)
            .Select(i => latestWeekStart.AddDays(-(numWeeks - 1 - i) * 7))
            .ToList();
        var startStr = weeks[0].ToString("yyyy-MM-dd");
        var endStr   = latestWeekStart.AddDays(6).ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var auditTotals    = weeks.ToDictionary(w => w, _ => 0);
        var effortTotals   = weeks.ToDictionary(w => w, _ => 0);
        var academyPersons = weeks.ToDictionary(w => w, _ => new HashSet<int>());
        var bodyPersons    = weeks.ToDictionary(w => w, _ => new HashSet<int>());
        var pcPersons      = weeks.ToDictionary(w => w, _ => new HashSet<int>());

        // Regular-session audit time per date (filtered by auditor person.Org)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT DATE(s.CreatedAt, 'localtime') AS d, SUM(s.LengthSeconds + s.AdminSeconds)
                FROM sess_sessions s
                { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                WHERE DATE(s.CreatedAt, 'localtime') >= @s AND DATE(s.CreatedAt, 'localtime') <= @e
                  { (orgId.HasValue ? "AND s.AuditorId IS NOT NULL AND ap.Org = @org" : "") }
                GROUP BY DATE(s.CreatedAt, 'localtime')";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ws = DashboardService.GetWeekStart(DateOnly.Parse(r.GetString(0)));
                if (auditTotals.ContainsKey(ws)) auditTotals[ws] += r.GetInt32(1);
            }
        }

        // Solo CS time: CS reviews on solo sessions (AuditorId IS NULL) per date (filtered by CS person.Org)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT DATE(cr.ReviewedAt, 'localtime') AS d, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                { (orgId.HasValue ? "JOIN core_persons cp ON cp.PersonId = cr.CsId" : "") }
                WHERE DATE(cr.ReviewedAt, 'localtime') >= @s AND DATE(cr.ReviewedAt, 'localtime') <= @e
                  AND s.AuditorId IS NULL
                  { (orgId.HasValue ? "AND cp.Org = @org" : "") }
                GROUP BY DATE(cr.ReviewedAt, 'localtime')";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ws = DashboardService.GetWeekStart(DateOnly.Parse(r.GetString(0)));
                if (auditTotals.ContainsKey(ws)) auditTotals[ws] += r.GetInt32(1);
            }
        }

        // Academy visits → unique persons per week (filtered by student person.Org)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT a.VisitDate, a.PersonId FROM acad_attendance a
                { (orgId.HasValue ? "JOIN core_persons sp ON sp.PersonId = a.PersonId" : "") }
                WHERE a.VisitDate >= @s AND a.VisitDate <= @e
                { (orgId.HasValue ? "AND sp.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ws  = DashboardService.GetWeekStart(DateOnly.Parse(r.GetString(0)));
                int pid = r.GetInt32(1);
                if (!academyPersons.ContainsKey(ws)) continue;
                academyPersons[ws].Add(pid);
                bodyPersons[ws].Add(pid);
            }
        }

        // Session PcIds → add to body-in-shop per week (filtered by auditor person.Org)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.SessionDate, s.PcId FROM sess_sessions s
                { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                WHERE s.SessionDate >= @s AND s.SessionDate <= @e
                { (orgId.HasValue ? "AND s.AuditorId IS NOT NULL AND ap.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ws  = DashboardService.GetWeekStart(DateOnly.Parse(r.GetString(0)));
                int pc  = r.GetInt32(1);
                if (bodyPersons.ContainsKey(ws)) bodyPersons[ws].Add(pc);
                if (pcPersons.ContainsKey(ws))   pcPersons[ws].Add(pc);
            }
        }

        // Effort rows: feed into BIS, PcCount, and per-week effort totals (filtered by performer person.Org)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT e.EffortDate, e.PcId, e.LengthSeconds
                FROM sys_effort_entries e
                { (orgId.HasValue ? "JOIN core_users eu ON eu.Id = e.PerformedByUserId JOIN core_persons ep ON ep.PersonId = eu.PersonId" : "") }
                WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                { (orgId.HasValue ? "AND ep.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ws = DashboardService.GetWeekStart(DateOnly.Parse(r.GetString(0)));
                int pc = r.GetInt32(1);
                int secs = r.GetInt32(2);
                if (bodyPersons.ContainsKey(ws))   bodyPersons[ws].Add(pc);
                if (pcPersons.ContainsKey(ws))     pcPersons[ws].Add(pc);
                if (effortTotals.ContainsKey(ws))  effortTotals[ws] += secs;
            }
        }

        return weeks.Select(w => new WeekStatSummary(
            w,
            auditTotals[w],
            academyPersons[w].Count,
            bodyPersons[w].Count,
            pcPersons[w].Count,
            effortTotals[w]
        )).ToList();
    }

    /// <summary>
    /// Returns a single WeekStatSummary aggregating all data in the given date range (for monthly use).
    /// </summary>
    public WeekStatSummary GetMonthSummary(DateOnly monthStart, DateOnly monthEnd, int? orgId = null)
    {
        var startStr = monthStart.ToString("yyyy-MM-dd");
        var endStr   = monthEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Regular-session audit seconds (filtered by auditor person.Org)
        int totalSec = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(SUM(s.LengthSeconds + s.AdminSeconds), 0)
                FROM sess_sessions s
                { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                WHERE DATE(s.CreatedAt, 'localtime') >= @s AND DATE(s.CreatedAt, 'localtime') <= @e
                  { (orgId.HasValue ? "AND s.AuditorId IS NOT NULL AND ap.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            totalSec = Convert.ToInt32(cmd.ExecuteScalar());
        }
        // Solo CS time: CS reviews on solo sessions (AuditorId IS NULL), filtered by CS person.Org
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(SUM(cr.ReviewLengthSeconds), 0)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                { (orgId.HasValue ? "JOIN core_persons cp ON cp.PersonId = cr.CsId" : "") }
                WHERE DATE(cr.ReviewedAt, 'localtime') >= @s AND DATE(cr.ReviewedAt, 'localtime') <= @e
                  AND s.AuditorId IS NULL
                  { (orgId.HasValue ? "AND cp.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            totalSec += Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Unique academy persons (filtered by student person.Org)
        int academyCount = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COUNT(DISTINCT a.PersonId)
                FROM acad_attendance a
                { (orgId.HasValue ? "JOIN core_persons sp ON sp.PersonId = a.PersonId" : "") }
                WHERE a.VisitDate >= @s AND a.VisitDate <= @e
                { (orgId.HasValue ? "AND sp.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            academyCount = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Unique PCs (sessions by filtered staff ∪ effort by filtered staff)
        int pcCount = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COUNT(DISTINCT pid) FROM (
                    SELECT s.PcId AS pid FROM sess_sessions s
                    { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                    WHERE s.SessionDate >= @s AND s.SessionDate <= @e
                    { (orgId.HasValue ? "AND s.AuditorId IS NOT NULL AND ap.Org = @org" : "") }
                    UNION ALL
                    SELECT e.PcId AS pid FROM sys_effort_entries e
                    { (orgId.HasValue ? "JOIN core_users eu ON eu.Id = e.PerformedByUserId JOIN core_persons ep ON ep.PersonId = eu.PersonId" : "") }
                    WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                    { (orgId.HasValue ? "AND ep.Org = @org" : "") }
                )";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            pcCount = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Body in shop (sessions by filtered staff ∪ academy by filtered student ∪ effort by filtered staff)
        int bodyInShop = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COUNT(DISTINCT pid) FROM (
                    SELECT s.PcId AS pid FROM sess_sessions s
                    { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                    WHERE s.SessionDate >= @s AND s.SessionDate <= @e
                    { (orgId.HasValue ? "AND s.AuditorId IS NOT NULL AND ap.Org = @org" : "") }
                    UNION ALL
                    SELECT a.PersonId AS pid FROM acad_attendance a
                    { (orgId.HasValue ? "JOIN core_persons sp ON sp.PersonId = a.PersonId" : "") }
                    WHERE a.VisitDate >= @s AND a.VisitDate <= @e
                    { (orgId.HasValue ? "AND sp.Org = @org" : "") }
                    UNION ALL
                    SELECT e.PcId AS pid FROM sys_effort_entries e
                    { (orgId.HasValue ? "JOIN core_users eu ON eu.Id = e.PerformedByUserId JOIN core_persons ep ON ep.PersonId = eu.PersonId" : "") }
                    WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                    { (orgId.HasValue ? "AND ep.Org = @org" : "") }
                )";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            bodyInShop = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Effort total (filtered by performer person.Org)
        int effortSec = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(SUM(e.LengthSeconds), 0)
                FROM sys_effort_entries e
                { (orgId.HasValue ? "JOIN core_users eu ON eu.Id = e.PerformedByUserId JOIN core_persons ep ON ep.PersonId = eu.PersonId" : "") }
                WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                { (orgId.HasValue ? "AND ep.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            effortSec = Convert.ToInt32(cmd.ExecuteScalar());
        }

        return new WeekStatSummary(monthStart, totalSec, academyCount, bodyInShop, pcCount, effortSec);
    }

    /// <summary>
    /// Returns auditing hours grouped by PC Origin for the given date range.
    /// Adds an "Unassigned" bucket for extra effort (which has no organization).
    /// </summary>
    public List<OriginHours> GetMonthOriginHours(DateOnly monthStart, DateOnly monthEnd, int? orgId = null)
    {
        var startStr = monthStart.ToString("yyyy-MM-dd");
        var endStr   = monthEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var list = new List<OriginHours>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(og.Name, 'Unknown') AS Org,
                       SUM(s.LengthSeconds + s.AdminSeconds) AS TotalSec
                FROM sess_sessions s
                JOIN core_persons p ON p.PersonId = s.PcId
                LEFT JOIN lkp_organizations og ON og.OrgId = p.Org
                { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                WHERE DATE(s.CreatedAt, 'localtime') >= @s AND DATE(s.CreatedAt, 'localtime') <= @e
                { (orgId.HasValue ? "AND s.AuditorId IS NOT NULL AND ap.Org = @org" : "") }
                GROUP BY COALESCE(og.Name, 'Unknown')
                ORDER BY TotalSec DESC";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new OriginHours(r.GetString(0), r.GetInt32(1)));
        }

        int effortSec = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(SUM(e.LengthSeconds), 0)
                FROM sys_effort_entries e
                { (orgId.HasValue ? "JOIN core_users eu ON eu.Id = e.PerformedByUserId JOIN core_persons ep ON ep.PersonId = eu.PersonId" : "") }
                WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                { (orgId.HasValue ? "AND ep.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            effortSec = Convert.ToInt32(cmd.ExecuteScalar());
        }
        if (effortSec > 0)
            list.Add(new OriginHours("Unassigned", effortSec));

        return list.OrderByDescending(o => o.Seconds).ToList();
    }

    /// <summary>
    /// Returns staff leaderboard aggregated over a date range (for monthly use).
    /// </summary>
    public List<StaffStatRow> GetMonthStaffLeaderboard(DateOnly monthStart, DateOnly monthEnd, int? orgId = null)
    {
        var startStr = monthStart.ToString("yyyy-MM-dd");
        var endStr   = monthEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Active staff names (filtered by staff person.Org when orgId set)
        var auditorNames = new Dictionary<int, string>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT u.PersonId,
                       TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS Name
                FROM core_users u
                JOIN core_persons p ON p.PersonId = u.PersonId
                WHERE u.IsActive = 1 AND u.StaffRole != 'None'"
                + (orgId.HasValue ? " AND p.Org = @org" : "");
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                auditorNames[r.GetInt32(0)] = r.GetString(1);
        }

        // Audit seconds per auditor (regular sessions only, AuditorId IS NOT NULL)
        var auditSecs = new Dictionary<int, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.AuditorId, SUM(s.LengthSeconds + s.AdminSeconds)
                FROM sess_sessions s
                { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                WHERE DATE(s.CreatedAt, 'localtime') >= @s AND DATE(s.CreatedAt, 'localtime') <= @e AND s.AuditorId IS NOT NULL
                { (orgId.HasValue ? "AND ap.Org = @org" : "") }
                GROUP BY s.AuditorId";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                auditSecs[r.GetInt32(0)] = r.GetInt32(1);
        }

        // Solo CS seconds per csId (CS reviews on solo sessions where AuditorId IS NULL)
        var soloCsSecs = new Dictionary<int, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cr.CsId, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                { (orgId.HasValue ? "JOIN core_persons cp ON cp.PersonId = cr.CsId" : "") }
                WHERE DATE(cr.ReviewedAt, 'localtime') >= @s AND DATE(cr.ReviewedAt, 'localtime') <= @e
                  AND s.AuditorId IS NULL
                  { (orgId.HasValue ? "AND cp.Org = @org" : "") }
                GROUP BY cr.CsId";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                soloCsSecs[r.GetInt32(0)] = r.GetInt32(1);
        }

        // CS seconds per csId (CS reviews on regular sessions where AuditorId IS NOT NULL)
        var csSecs = new Dictionary<int, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cr.CsId, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                { (orgId.HasValue ? "JOIN core_persons cp ON cp.PersonId = cr.CsId" : "") }
                WHERE DATE(cr.ReviewedAt, 'localtime') >= @s AND DATE(cr.ReviewedAt, 'localtime') <= @e
                  AND s.AuditorId IS NOT NULL
                  { (orgId.HasValue ? "AND cp.Org = @org" : "") }
                GROUP BY cr.CsId";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                csSecs[r.GetInt32(0)] = r.GetInt32(1);
        }

        // Effort seconds per PersonId (filtered by performer person.Org)
        var effortSecs = new Dictionary<int, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT u.PersonId, SUM(e.LengthSeconds)
                FROM sys_effort_entries e
                JOIN core_users u ON u.Id = e.PerformedByUserId
                { (orgId.HasValue ? "JOIN core_persons ep ON ep.PersonId = u.PersonId" : "") }
                WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                { (orgId.HasValue ? "AND ep.Org = @org" : "") }
                GROUP BY u.PersonId";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                effortSecs[r.GetInt32(0)] = r.GetInt32(1);
        }

        var allPids = new HashSet<int>(auditorNames.Keys);
        foreach (var k in auditSecs.Keys)  allPids.Add(k);
        foreach (var k in soloCsSecs.Keys) allPids.Add(k);
        foreach (var k in csSecs.Keys)     allPids.Add(k);
        foreach (var k in effortSecs.Keys) allPids.Add(k);

        return allPids
            .Select(pid =>
            {
                string name = auditorNames.TryGetValue(pid, out var n) ? n : $"Person {pid}";
                int audit  = auditSecs.GetValueOrDefault(pid);
                int solo   = soloCsSecs.GetValueOrDefault(pid);
                int cs     = csSecs.GetValueOrDefault(pid);
                int effort = effortSecs.GetValueOrDefault(pid);
                return new StaffStatRow(pid, name, audit, solo, effort, cs);
            })
            .Where(s => s.TotalSec > 0)
            .OrderByDescending(s => s.TotalSec)
            .ToList();
    }

    /// <summary>
    /// Returns monthly summaries for the last numMonths months (each month = weeks where Thursday falls in that month).
    /// </summary>
    public List<MonthStatSummary> GetMonthlySummaries(DateOnly currentWeekStart, int numMonths = 12, int? orgId = null)
    {
        // Build list of months going back numMonths
        var months = new List<(DateOnly start, DateOnly end, DateOnly firstThursday)>();
        var refDate = new DateOnly(currentWeekStart.Year, currentWeekStart.Month, 1);
        for (int i = 0; i < numMonths; i++)
        {
            var mDate = refDate.AddMonths(-(numMonths - 1 - i));
            // Find first Thursday in this month
            var d = mDate;
            while (d.DayOfWeek != DayOfWeek.Thursday) d = d.AddDays(1);
            var firstThu = d;
            // Find last Thursday
            var lastThu = firstThu;
            while (lastThu.AddDays(7).Month == mDate.Month)
                lastThu = lastThu.AddDays(7);
            var monthEnd = lastThu.AddDays(6);
            months.Add((firstThu, monthEnd, firstThu));
        }

        if (months.Count == 0) return new();

        var globalStart = months[0].start.ToString("yyyy-MM-dd");
        var globalEnd   = months[^1].end.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Regular-session audit time per date (filtered by auditor person.Org)
        var dayTotals = new Dictionary<string, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT DATE(s.CreatedAt, 'localtime') AS d, SUM(s.LengthSeconds + s.AdminSeconds)
                FROM sess_sessions s
                { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                WHERE DATE(s.CreatedAt, 'localtime') >= @s AND DATE(s.CreatedAt, 'localtime') <= @e
                { (orgId.HasValue ? "AND s.AuditorId IS NOT NULL AND ap.Org = @org" : "") }
                GROUP BY DATE(s.CreatedAt, 'localtime')";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                dayTotals[r.GetString(0)] = r.GetInt32(1);
        }
        // Solo CS time (filtered by CS person.Org)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT DATE(cr.ReviewedAt, 'localtime') AS d, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                { (orgId.HasValue ? "JOIN core_persons cp ON cp.PersonId = cr.CsId" : "") }
                WHERE DATE(cr.ReviewedAt, 'localtime') >= @s AND DATE(cr.ReviewedAt, 'localtime') <= @e
                  AND s.AuditorId IS NULL
                  { (orgId.HasValue ? "AND cp.Org = @org" : "") }
                GROUP BY DATE(cr.ReviewedAt, 'localtime')";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dt = r.GetString(0);
                dayTotals[dt] = dayTotals.GetValueOrDefault(dt) + r.GetInt32(1);
            }
        }

        // Academy visits per date+person (filtered by student person.Org)
        var academyByDate = new Dictionary<string, HashSet<int>>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT a.VisitDate, a.PersonId FROM acad_attendance a
                { (orgId.HasValue ? "JOIN core_persons sp ON sp.PersonId = a.PersonId" : "") }
                WHERE a.VisitDate >= @s AND a.VisitDate <= @e
                { (orgId.HasValue ? "AND sp.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dt = r.GetString(0);
                if (!academyByDate.ContainsKey(dt)) academyByDate[dt] = new();
                academyByDate[dt].Add(r.GetInt32(1));
            }
        }

        // Session PcIds per date (filtered by auditor person.Org)
        var sessionsByDate = new Dictionary<string, HashSet<int>>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.SessionDate, s.PcId FROM sess_sessions s
                { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                WHERE s.SessionDate >= @s AND s.SessionDate <= @e
                { (orgId.HasValue ? "AND s.AuditorId IS NOT NULL AND ap.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dt = r.GetString(0);
                if (!sessionsByDate.ContainsKey(dt)) sessionsByDate[dt] = new();
                sessionsByDate[dt].Add(r.GetInt32(1));
            }
        }

        // Effort per date (filtered by performer person.Org)
        var effortByDate   = new Dictionary<string, HashSet<int>>();
        var effortTotsDate = new Dictionary<string, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT e.EffortDate, e.PcId, e.LengthSeconds FROM sys_effort_entries e
                { (orgId.HasValue ? "JOIN core_users eu ON eu.Id = e.PerformedByUserId JOIN core_persons ep ON ep.PersonId = eu.PersonId" : "") }
                WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                { (orgId.HasValue ? "AND ep.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dt = r.GetString(0);
                int pc = r.GetInt32(1);
                int secs = r.GetInt32(2);
                if (!effortByDate.ContainsKey(dt)) effortByDate[dt] = new();
                effortByDate[dt].Add(pc);
                effortTotsDate[dt] = effortTotsDate.GetValueOrDefault(dt) + secs;
            }
        }

        // Aggregate per month
        return months.Select(m =>
        {
            int totalSec = 0;
            int effortSec = 0;
            var acadPersons = new HashSet<int>();
            var bisPersons  = new HashSet<int>();
            var pcPersons   = new HashSet<int>();

            for (var d = m.start; d <= m.end; d = d.AddDays(1))
            {
                var ds = d.ToString("yyyy-MM-dd");
                totalSec += dayTotals.GetValueOrDefault(ds);
                effortSec += effortTotsDate.GetValueOrDefault(ds);
                if (academyByDate.TryGetValue(ds, out var ap))
                {
                    foreach (var p in ap) { acadPersons.Add(p); bisPersons.Add(p); }
                }
                if (sessionsByDate.TryGetValue(ds, out var sp))
                {
                    foreach (var p in sp) { pcPersons.Add(p); bisPersons.Add(p); }
                }
                if (effortByDate.TryGetValue(ds, out var ep))
                {
                    foreach (var p in ep) { pcPersons.Add(p); bisPersons.Add(p); }
                }
            }

            return new MonthStatSummary(m.start, m.end, totalSec, acadPersons.Count, bisPersons.Count, pcPersons.Count, effortSec);
        }).ToList();
    }

    /// <summary>
    /// Returns auditing hours (LengthSeconds + AdminSeconds) grouped by PC Origin for the given week.
    /// Adds an "Unassigned" bucket for extra effort. Ordered by total descending.
    /// </summary>
    public List<OriginHours> GetWeekOriginHours(DateOnly weekStart, int? orgId = null)
    {
        var weekEnd  = weekStart.AddDays(6);
        var startStr = weekStart.ToString("yyyy-MM-dd");
        var endStr   = weekEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var list = new List<OriginHours>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(og.Name, 'Unknown') AS Org,
                       SUM(s.LengthSeconds + s.AdminSeconds) AS TotalSec
                FROM sess_sessions s
                JOIN core_persons p ON p.PersonId = s.PcId
                LEFT JOIN lkp_organizations og ON og.OrgId = p.Org
                { (orgId.HasValue ? "JOIN core_persons ap ON ap.PersonId = s.AuditorId" : "") }
                WHERE DATE(s.CreatedAt, 'localtime') >= @s AND DATE(s.CreatedAt, 'localtime') <= @e
                { (orgId.HasValue ? "AND s.AuditorId IS NOT NULL AND ap.Org = @org" : "") }
                GROUP BY COALESCE(og.Name, 'Unknown')";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new OriginHours(r.GetString(0), r.GetInt32(1)));
        }

        int effortSec = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(SUM(e.LengthSeconds), 0)
                FROM sys_effort_entries e
                { (orgId.HasValue ? "JOIN core_users eu ON eu.Id = e.PerformedByUserId JOIN core_persons ep ON ep.PersonId = eu.PersonId" : "") }
                WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                { (orgId.HasValue ? "AND ep.Org = @org" : "") }";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            if (orgId.HasValue) cmd.Parameters.AddWithValue("@org", orgId.Value);
            effortSec = Convert.ToInt32(cmd.ExecuteScalar());
        }
        if (effortSec > 0)
            list.Add(new OriginHours("Unassigned", effortSec));

        return list.OrderByDescending(o => o.Seconds).ToList();
    }

    // ── Per-cell detail breakdowns (for Statistics hover tooltip) ───────────
    // Each method mirrors the corresponding aggregate query: audit buckets by
    // DATE(CreatedAt, 'localtime'), csolo by DATE(ReviewedAt), effort by EffortDate — so the
    // sum of returned items equals the cell value shown in the UI.

    public StatCellDetail GetAuditDetail(int auditorId, DateOnly start, DateOnly end)
    {
        var startStr = start.ToString("yyyy-MM-dd");
        var endStr   = end.ToString("yyyy-MM-dd");
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(TRIM(p.FirstName || ' ' || p.LastName), 'Unknown') AS pcName,
                   DATE(s.CreatedAt, 'localtime') AS d,
                   s.LengthSeconds + s.AdminSeconds AS sec
            FROM sess_sessions s
            LEFT JOIN core_persons p ON p.PersonId = s.PcId
            WHERE s.AuditorId = @aid
              AND DATE(s.CreatedAt, 'localtime') >= @s AND DATE(s.CreatedAt, 'localtime') <= @e
            ORDER BY DATE(s.CreatedAt, 'localtime') DESC, s.CreatedAt DESC";
        cmd.Parameters.AddWithValue("@aid", auditorId);
        cmd.Parameters.AddWithValue("@s", startStr);
        cmd.Parameters.AddWithValue("@e", endStr);
        var items = new List<StatCellDetailItem>();
        int total = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int sec = r.GetInt32(2);
            items.Add(new StatCellDetailItem(r.GetString(0), r.GetString(1), sec));
            total += sec;
        }
        return new StatCellDetail(items, total);
    }

    public StatCellDetail GetSoloCsDetail(int csId, DateOnly start, DateOnly end)
    {
        var startStr = start.ToString("yyyy-MM-dd");
        var endStr   = end.ToString("yyyy-MM-dd");
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(TRIM(p.FirstName || ' ' || p.LastName), 'Unknown') AS pcName,
                   DATE(cr.ReviewedAt, 'localtime') AS d,
                   cr.ReviewLengthSeconds AS sec
            FROM cs_reviews cr
            JOIN sess_sessions s ON s.SessionId = cr.SessionId
            LEFT JOIN core_persons p ON p.PersonId = s.PcId
            WHERE cr.CsId = @cid
              AND DATE(cr.ReviewedAt, 'localtime') >= @s AND DATE(cr.ReviewedAt, 'localtime') <= @e
              AND s.AuditorId IS NULL
            ORDER BY DATE(cr.ReviewedAt, 'localtime') DESC, cr.ReviewedAt DESC";
        cmd.Parameters.AddWithValue("@cid", csId);
        cmd.Parameters.AddWithValue("@s", startStr);
        cmd.Parameters.AddWithValue("@e", endStr);
        var items = new List<StatCellDetailItem>();
        int total = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int sec = r.GetInt32(2);
            items.Add(new StatCellDetailItem(r.GetString(0), r.GetString(1), sec));
            total += sec;
        }
        return new StatCellDetail(items, total);
    }

    public StatCellDetail GetCsDetail(int csId, DateOnly start, DateOnly end)
    {
        var startStr = start.ToString("yyyy-MM-dd");
        var endStr   = end.ToString("yyyy-MM-dd");
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(TRIM(p.FirstName || ' ' || p.LastName), 'Unknown') AS pcName,
                   DATE(cr.ReviewedAt, 'localtime') AS d,
                   cr.ReviewLengthSeconds AS sec
            FROM cs_reviews cr
            JOIN sess_sessions s ON s.SessionId = cr.SessionId
            LEFT JOIN core_persons p ON p.PersonId = s.PcId
            WHERE cr.CsId = @cid
              AND DATE(cr.ReviewedAt, 'localtime') >= @s AND DATE(cr.ReviewedAt, 'localtime') <= @e
              AND s.AuditorId IS NOT NULL
            ORDER BY DATE(cr.ReviewedAt, 'localtime') DESC, cr.ReviewedAt DESC";
        cmd.Parameters.AddWithValue("@cid", csId);
        cmd.Parameters.AddWithValue("@s", startStr);
        cmd.Parameters.AddWithValue("@e", endStr);
        var items = new List<StatCellDetailItem>();
        int total = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int sec = r.GetInt32(2);
            items.Add(new StatCellDetailItem(r.GetString(0), r.GetString(1), sec));
            total += sec;
        }
        return new StatCellDetail(items, total);
    }

    public StatCellDetail GetEffortDetail(int personId, DateOnly start, DateOnly end)
    {
        var startStr = start.ToString("yyyy-MM-dd");
        var endStr   = end.ToString("yyyy-MM-dd");
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(TRIM(p.FirstName || ' ' || p.LastName), 'Unknown') AS pcName,
                   ee.EffortDate AS d,
                   ee.LengthSeconds AS sec
            FROM sys_effort_entries ee
            JOIN core_users u ON u.Id = ee.PerformedByUserId
            LEFT JOIN core_persons p ON p.PersonId = ee.PcId
            WHERE u.PersonId = @pid
              AND ee.EffortDate >= @s AND ee.EffortDate <= @e
            ORDER BY ee.EffortDate DESC, ee.CreatedAt DESC";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@s", startStr);
        cmd.Parameters.AddWithValue("@e", endStr);
        var items = new List<StatCellDetailItem>();
        int total = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int sec = r.GetInt32(2);
            items.Add(new StatCellDetailItem(r.GetString(0), r.GetString(1), sec));
            total += sec;
        }
        return new StatCellDetail(items, total);
    }
}
