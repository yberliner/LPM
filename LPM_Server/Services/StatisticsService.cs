using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LPM.Services;

public record StaffStatRow(int PersonId, string Name, int AuditSec, int SoloCsSec, int EffortSec = 0)
{
    public int TotalSec => AuditSec + SoloCsSec + EffortSec;
}

public record DayStat(DateOnly Date, List<StaffStatRow> Staff, int AcademyCount, int BodyInShop, int PcCount, int EffortSec = 0);

public record OriginHours(string Origin, int Seconds);

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
    /// </summary>
    public List<DayStat> GetWeekDayStats(DateOnly weekStart)
    {
        var weekEnd  = weekStart.AddDays(6);
        var dates    = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var startStr = weekStart.ToString("yyyy-MM-dd");
        var endStr   = weekEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // ── Active staff names ───────────────────────────────────────────────
        var auditorNames = new Dictionary<int, string>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT u.PersonId,
                       TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS Name
                FROM core_users u
                JOIN core_persons p ON p.PersonId = u.PersonId
                WHERE u.IsActive = 1 AND u.StaffRole != 'None'";
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
                SELECT AuditorId, DATE(CreatedAt) AS d, SUM(LengthSeconds + AdminSeconds)
                FROM sess_sessions
                WHERE DATE(CreatedAt) >= @s AND DATE(CreatedAt) <= @e AND AuditorId IS NOT NULL
                GROUP BY AuditorId, DATE(CreatedAt)";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
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
                SELECT cr.CsId, DATE(cr.ReviewedAt) AS d, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE DATE(cr.ReviewedAt) >= @s AND DATE(cr.ReviewedAt) <= @e
                  AND s.AuditorId IS NULL
                GROUP BY cr.CsId, DATE(cr.ReviewedAt)";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(1)));
                if (dayIdx < 0) continue;
                var key = (r.GetInt32(0), dayIdx);
                soloCsSecs[key] = soloCsSecs.GetValueOrDefault(key) + r.GetInt32(2);
            }
        }

        // ── Academy visit count per day ──────────────────────────────────────
        var academyCounts = new int[7];
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT VisitDate, COUNT(*)
                FROM acad_attendance
                WHERE VisitDate >= @s AND VisitDate <= @e
                GROUP BY VisitDate";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(0)));
                if (dayIdx >= 0) academyCounts[dayIdx] = r.GetInt32(1);
            }
        }

        // ── Distinct PC count per day (unique PcIds in Sessions ∪ Effort) ──
        var pcCounts = new int[7];
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT d, COUNT(DISTINCT pid) FROM (
                    SELECT SessionDate AS d, PcId AS pid FROM sess_sessions
                    WHERE  SessionDate >= @s AND SessionDate <= @e
                    UNION ALL
                    SELECT EffortDate  AS d, PcId AS pid FROM sys_effort_entries
                    WHERE  EffortDate  >= @s AND EffortDate <= @e
                ) GROUP BY d";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(0)));
                if (dayIdx >= 0) pcCounts[dayIdx] = r.GetInt32(1);
            }
        }

        // ── Body in shop per day (sessions ∪ academy ∪ effort) ──
        var bodyInShop = new int[7];
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT d, COUNT(DISTINCT pid) FROM (
                    SELECT SessionDate AS d, PcId     AS pid FROM sess_sessions
                    WHERE  SessionDate >= @s AND SessionDate <= @e
                    UNION ALL
                    SELECT VisitDate   AS d, PersonId AS pid FROM acad_attendance
                    WHERE  VisitDate   >= @s AND VisitDate   <= @e
                    UNION ALL
                    SELECT EffortDate  AS d, PcId     AS pid FROM sys_effort_entries
                    WHERE  EffortDate  >= @s AND EffortDate  <= @e
                ) GROUP BY d";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
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
            cmd.CommandText = @"
                SELECT u.PersonId, e.EffortDate, SUM(e.LengthSeconds)
                FROM sys_effort_entries e
                JOIN core_users u ON u.Id = e.PerformedByUserId
                WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                GROUP BY u.PersonId, e.EffortDate";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
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
        foreach (var k in effortSecs.Keys) allPids.Add(k.pid);

        return Enumerable.Range(0, 7).Select(dayIdx =>
        {
            var staff = allPids
                .Select(pid =>
                {
                    string name = auditorNames.TryGetValue(pid, out var n) ? n : $"Person {pid}";
                    int audit = auditSecs.GetValueOrDefault((pid, dayIdx));
                    int cs    = soloCsSecs.GetValueOrDefault((pid, dayIdx));
                    int eff   = effortSecs.GetValueOrDefault((pid, dayIdx));
                    return new StaffStatRow(pid, name, audit, cs, eff);
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
    public List<WeekStatSummary> GetWeeklySummaries(DateOnly latestWeekStart, int numWeeks = 20)
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

        // All session time per date
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT DATE(CreatedAt) AS d, SUM(LengthSeconds + AdminSeconds)
                FROM sess_sessions
                WHERE DATE(CreatedAt) >= @s AND DATE(CreatedAt) <= @e
                GROUP BY DATE(CreatedAt)";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ws = DashboardService.GetWeekStart(DateOnly.Parse(r.GetString(0)));
                if (auditTotals.ContainsKey(ws)) auditTotals[ws] += r.GetInt32(1);
            }
        }

        // Solo CS time: CS reviews on solo sessions (AuditorId IS NULL) per date
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT DATE(cr.ReviewedAt) AS d, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE DATE(cr.ReviewedAt) >= @s AND DATE(cr.ReviewedAt) <= @e
                  AND s.AuditorId IS NULL
                GROUP BY DATE(cr.ReviewedAt)";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ws = DashboardService.GetWeekStart(DateOnly.Parse(r.GetString(0)));
                if (auditTotals.ContainsKey(ws)) auditTotals[ws] += r.GetInt32(1);
            }
        }

        // Academy visits → unique persons per week
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT VisitDate, PersonId FROM acad_attendance
                WHERE VisitDate >= @s AND VisitDate <= @e";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
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

        // Session PcIds → add to body-in-shop per week
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT SessionDate, PcId FROM sess_sessions
                WHERE SessionDate >= @s AND SessionDate <= @e";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ws  = DashboardService.GetWeekStart(DateOnly.Parse(r.GetString(0)));
                int pc  = r.GetInt32(1);
                if (bodyPersons.ContainsKey(ws)) bodyPersons[ws].Add(pc);
                if (pcPersons.ContainsKey(ws))   pcPersons[ws].Add(pc);
            }
        }

        // Effort rows: feed into BIS, PcCount, and per-week effort totals
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT EffortDate, PcId, LengthSeconds
                FROM sys_effort_entries
                WHERE EffortDate >= @s AND EffortDate <= @e";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
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
    public WeekStatSummary GetMonthSummary(DateOnly monthStart, DateOnly monthEnd)
    {
        var startStr = monthStart.ToString("yyyy-MM-dd");
        var endStr   = monthEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Total audit+CS seconds
        int totalSec = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(LengthSeconds + AdminSeconds), 0)
                FROM sess_sessions
                WHERE DATE(CreatedAt) >= @s AND DATE(CreatedAt) <= @e";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            totalSec = Convert.ToInt32(cmd.ExecuteScalar());
        }
        // Solo CS time: CS reviews on solo sessions (AuditorId IS NULL)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(cr.ReviewLengthSeconds), 0)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE DATE(cr.ReviewedAt) >= @s AND DATE(cr.ReviewedAt) <= @e
                  AND s.AuditorId IS NULL";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            totalSec += Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Unique academy persons
        int academyCount = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(DISTINCT PersonId)
                FROM acad_attendance
                WHERE VisitDate >= @s AND VisitDate <= @e";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            academyCount = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Unique PCs (sessions ∪ effort)
        int pcCount = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(DISTINCT PcId) FROM (
                    SELECT PcId FROM sess_sessions
                    WHERE SessionDate >= @s AND SessionDate <= @e
                    UNION ALL
                    SELECT PcId FROM sys_effort_entries
                    WHERE EffortDate >= @s AND EffortDate <= @e
                )";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            pcCount = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Body in shop (sessions ∪ academy ∪ effort)
        int bodyInShop = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(DISTINCT pid) FROM (
                    SELECT PcId AS pid FROM sess_sessions
                    WHERE SessionDate >= @s AND SessionDate <= @e
                    UNION ALL
                    SELECT PersonId AS pid FROM acad_attendance
                    WHERE VisitDate >= @s AND VisitDate <= @e
                    UNION ALL
                    SELECT PcId AS pid FROM sys_effort_entries
                    WHERE EffortDate >= @s AND EffortDate <= @e
                )";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            bodyInShop = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Effort total
        int effortSec = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(LengthSeconds), 0)
                FROM sys_effort_entries
                WHERE EffortDate >= @s AND EffortDate <= @e";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            effortSec = Convert.ToInt32(cmd.ExecuteScalar());
        }

        return new WeekStatSummary(monthStart, totalSec, academyCount, bodyInShop, pcCount, effortSec);
    }

    /// <summary>
    /// Returns auditing hours grouped by PC Origin for the given date range.
    /// Adds an "Unassigned" bucket for extra effort (which has no organization).
    /// </summary>
    public List<OriginHours> GetMonthOriginHours(DateOnly monthStart, DateOnly monthEnd)
    {
        var startStr = monthStart.ToString("yyyy-MM-dd");
        var endStr   = monthEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var list = new List<OriginHours>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(og.Name, 'Unknown') AS Org,
                       SUM(s.LengthSeconds + s.AdminSeconds) AS TotalSec
                FROM sess_sessions s
                JOIN core_persons p ON p.PersonId = s.PcId
                LEFT JOIN lkp_organizations og ON og.OrgId = p.Org
                WHERE DATE(s.CreatedAt) >= @s AND DATE(s.CreatedAt) <= @e
                GROUP BY COALESCE(og.Name, 'Unknown')
                ORDER BY TotalSec DESC";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new OriginHours(r.GetString(0), r.GetInt32(1)));
        }

        int effortSec = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(LengthSeconds), 0)
                FROM sys_effort_entries
                WHERE EffortDate >= @s AND EffortDate <= @e";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            effortSec = Convert.ToInt32(cmd.ExecuteScalar());
        }
        if (effortSec > 0)
            list.Add(new OriginHours("Unassigned", effortSec));

        return list.OrderByDescending(o => o.Seconds).ToList();
    }

    /// <summary>
    /// Returns staff leaderboard aggregated over a date range (for monthly use).
    /// </summary>
    public List<StaffStatRow> GetMonthStaffLeaderboard(DateOnly monthStart, DateOnly monthEnd)
    {
        var startStr = monthStart.ToString("yyyy-MM-dd");
        var endStr   = monthEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Active staff names
        var auditorNames = new Dictionary<int, string>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT u.PersonId,
                       TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS Name
                FROM core_users u
                JOIN core_persons p ON p.PersonId = u.PersonId
                WHERE u.IsActive = 1 AND u.StaffRole != 'None'";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                auditorNames[r.GetInt32(0)] = r.GetString(1);
        }

        // Audit seconds per auditor (regular sessions only, AuditorId IS NOT NULL)
        var auditSecs = new Dictionary<int, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT AuditorId, SUM(LengthSeconds + AdminSeconds)
                FROM sess_sessions
                WHERE DATE(CreatedAt) >= @s AND DATE(CreatedAt) <= @e AND AuditorId IS NOT NULL
                GROUP BY AuditorId";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                auditSecs[r.GetInt32(0)] = r.GetInt32(1);
        }

        // Solo CS seconds per csId (CS reviews on solo sessions where AuditorId IS NULL)
        var soloCsSecs = new Dictionary<int, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT cr.CsId, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE DATE(cr.ReviewedAt) >= @s AND DATE(cr.ReviewedAt) <= @e
                  AND s.AuditorId IS NULL
                GROUP BY cr.CsId";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                soloCsSecs[r.GetInt32(0)] = r.GetInt32(1);
        }

        // Effort seconds per PersonId
        var effortSecs = new Dictionary<int, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT u.PersonId, SUM(e.LengthSeconds)
                FROM sys_effort_entries e
                JOIN core_users u ON u.Id = e.PerformedByUserId
                WHERE e.EffortDate >= @s AND e.EffortDate <= @e
                GROUP BY u.PersonId";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                effortSecs[r.GetInt32(0)] = r.GetInt32(1);
        }

        var allPids = new HashSet<int>(auditorNames.Keys);
        foreach (var k in auditSecs.Keys)  allPids.Add(k);
        foreach (var k in soloCsSecs.Keys) allPids.Add(k);
        foreach (var k in effortSecs.Keys) allPids.Add(k);

        return allPids
            .Select(pid =>
            {
                string name = auditorNames.TryGetValue(pid, out var n) ? n : $"Person {pid}";
                int audit  = auditSecs.GetValueOrDefault(pid);
                int cs     = soloCsSecs.GetValueOrDefault(pid);
                int effort = effortSecs.GetValueOrDefault(pid);
                return new StaffStatRow(pid, name, audit, cs, effort);
            })
            .Where(s => s.TotalSec > 0)
            .OrderByDescending(s => s.TotalSec)
            .ToList();
    }

    /// <summary>
    /// Returns monthly summaries for the last numMonths months (each month = weeks where Thursday falls in that month).
    /// </summary>
    public List<MonthStatSummary> GetMonthlySummaries(DateOnly currentWeekStart, int numMonths = 12)
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

        // Audit+CS time per date
        var dayTotals = new Dictionary<string, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT DATE(CreatedAt) AS d, SUM(LengthSeconds + AdminSeconds)
                FROM sess_sessions
                WHERE DATE(CreatedAt) >= @s AND DATE(CreatedAt) <= @e
                GROUP BY DATE(CreatedAt)";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                dayTotals[r.GetString(0)] = r.GetInt32(1);
        }
        // Solo CS time
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT DATE(cr.ReviewedAt) AS d, SUM(cr.ReviewLengthSeconds)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE DATE(cr.ReviewedAt) >= @s AND DATE(cr.ReviewedAt) <= @e
                  AND s.AuditorId IS NULL
                GROUP BY DATE(cr.ReviewedAt)";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dt = r.GetString(0);
                dayTotals[dt] = dayTotals.GetValueOrDefault(dt) + r.GetInt32(1);
            }
        }

        // Academy visits per date+person
        var academyByDate = new Dictionary<string, HashSet<int>>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT VisitDate, PersonId FROM acad_attendance
                WHERE VisitDate >= @s AND VisitDate <= @e";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dt = r.GetString(0);
                if (!academyByDate.ContainsKey(dt)) academyByDate[dt] = new();
                academyByDate[dt].Add(r.GetInt32(1));
            }
        }

        // Session PcIds per date
        var sessionsByDate = new Dictionary<string, HashSet<int>>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT SessionDate, PcId FROM sess_sessions
                WHERE SessionDate >= @s AND SessionDate <= @e";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dt = r.GetString(0);
                if (!sessionsByDate.ContainsKey(dt)) sessionsByDate[dt] = new();
                sessionsByDate[dt].Add(r.GetInt32(1));
            }
        }

        // Effort per date (both total seconds and distinct PcIds for BIS/PcCount)
        var effortByDate   = new Dictionary<string, HashSet<int>>();
        var effortTotsDate = new Dictionary<string, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT EffortDate, PcId, LengthSeconds FROM sys_effort_entries
                WHERE EffortDate >= @s AND EffortDate <= @e";
            cmd.Parameters.AddWithValue("@s", globalStart);
            cmd.Parameters.AddWithValue("@e", globalEnd);
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
    public List<OriginHours> GetWeekOriginHours(DateOnly weekStart)
    {
        var weekEnd  = weekStart.AddDays(6);
        var startStr = weekStart.ToString("yyyy-MM-dd");
        var endStr   = weekEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var list = new List<OriginHours>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(og.Name, 'Unknown') AS Org,
                       SUM(s.LengthSeconds + s.AdminSeconds) AS TotalSec
                FROM sess_sessions s
                JOIN core_persons p ON p.PersonId = s.PcId
                LEFT JOIN lkp_organizations og ON og.OrgId = p.Org
                WHERE DATE(s.CreatedAt) >= @s AND DATE(s.CreatedAt) <= @e
                GROUP BY COALESCE(og.Name, 'Unknown')";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new OriginHours(r.GetString(0), r.GetInt32(1)));
        }

        int effortSec = 0;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(LengthSeconds), 0)
                FROM sys_effort_entries
                WHERE EffortDate >= @s AND EffortDate <= @e";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            effortSec = Convert.ToInt32(cmd.ExecuteScalar());
        }
        if (effortSec > 0)
            list.Add(new OriginHours("Unassigned", effortSec));

        return list.OrderByDescending(o => o.Seconds).ToList();
    }
}
