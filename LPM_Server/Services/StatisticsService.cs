using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LPM.Services;

public record StaffStatRow(int PersonId, string Name, int AuditSec, int SoloCsSec)
{
    public int TotalSec => AuditSec + SoloCsSec;
}

public record DayStat(DateOnly Date, List<StaffStatRow> Staff, int AcademyCount, int BodyInShop, int PcCount);

public record OriginHours(string Origin, int Seconds);

public record WeekStatSummary(DateOnly WeekStart, int TotalAuditCsSec, int AcademyCount, int BodyInShop, int PcCount)
{
    public string WeekRangeLabel =>
        $"{WeekStart:dd/MM} – {WeekStart.AddDays(6):dd/MM}";
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
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // Ensure PCs.Origin column exists (also added by PcService, but we need it here)
        using var ck = conn.CreateCommand();
        ck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('PCs') WHERE name='Origin'";
        if ((long)(ck.ExecuteScalar() ?? 0L) == 0)
        {
            using var alt = conn.CreateCommand();
            alt.CommandText = "ALTER TABLE PCs ADD COLUMN Origin TEXT";
            alt.ExecuteNonQuery();
        }
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

        // ── Active auditors ──────────────────────────────────────────────────
        var auditorNames = new Dictionary<int, string>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT a.AuditorId,
                       TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS Name
                FROM Auditors a
                JOIN Persons p ON p.PersonId = a.AuditorId
                WHERE a.IsActive = 1";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                auditorNames[r.GetInt32(0)] = r.GetString(1);
        }

        // ── Auditing seconds per (auditorId, dayIndex) ───────────────────────
        // Includes both regular and solo (PcId=AuditorId) sessions
        var auditSecs = new Dictionary<(int pid, int day), int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT AuditorId, SessionDate, SUM(LengthSeconds + AdminSeconds)
                FROM Sessions
                WHERE SessionDate >= @s AND SessionDate <= @e
                GROUP BY AuditorId, SessionDate";
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
        // Only for auditors of Type IN (2, 3) = SoloOnly or RegularAndSolo
        var soloCsSecs = new Dictionary<(int pid, int day), int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cr.CsId, s.SessionDate, SUM(cr.ReviewLengthSeconds)
                FROM CsReviews cr
                JOIN Sessions s  ON s.SessionId   = cr.SessionId
                JOIN Auditors a  ON a.AuditorId   = cr.CsId
                WHERE s.SessionDate >= @s AND s.SessionDate <= @e
                  AND s.PcId = s.AuditorId
                  AND a.Type IN (2, 3)
                GROUP BY cr.CsId, s.SessionDate";
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
                FROM Students
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

        // ── Distinct PC count per day (unique PcIds in Sessions) ────────────────
        var pcCounts = new int[7];
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT SessionDate, COUNT(DISTINCT PcId)
                FROM Sessions
                WHERE SessionDate >= @s AND SessionDate <= @e
                GROUP BY SessionDate";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int dayIdx = dates.IndexOf(DateOnly.Parse(r.GetString(0)));
                if (dayIdx >= 0) pcCounts[dayIdx] = r.GetInt32(1);
            }
        }

        // ── Body in shop per day (unique persons: PcId from sessions ∪ PersonId from academy) ──
        var bodyInShop = new int[7];
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT d, COUNT(DISTINCT pid) FROM (
                    SELECT SessionDate AS d, PcId     AS pid FROM Sessions
                    WHERE  SessionDate >= @s AND SessionDate <= @e
                    UNION ALL
                    SELECT VisitDate   AS d, PersonId AS pid FROM Students
                    WHERE  VisitDate   >= @s AND VisitDate   <= @e
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

        // ── Build per-day results ────────────────────────────────────────────
        var allPids = new HashSet<int>(auditorNames.Keys);
        foreach (var k in auditSecs.Keys)  allPids.Add(k.pid);
        foreach (var k in soloCsSecs.Keys) allPids.Add(k.pid);

        return Enumerable.Range(0, 7).Select(dayIdx =>
        {
            var staff = allPids
                .Select(pid =>
                {
                    string name = auditorNames.TryGetValue(pid, out var n) ? n : $"Person {pid}";
                    int audit = auditSecs.GetValueOrDefault((pid, dayIdx));
                    int cs    = soloCsSecs.GetValueOrDefault((pid, dayIdx));
                    return new StaffStatRow(pid, name, audit, cs);
                })
                .Where(s => s.TotalSec > 0)
                .OrderByDescending(s => s.TotalSec)
                .ToList();

            return new DayStat(dates[dayIdx], staff, academyCounts[dayIdx], bodyInShop[dayIdx], pcCounts[dayIdx]);
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
        var academyPersons = weeks.ToDictionary(w => w, _ => new HashSet<int>());
        var bodyPersons    = weeks.ToDictionary(w => w, _ => new HashSet<int>());
        var pcPersons      = weeks.ToDictionary(w => w, _ => new HashSet<int>());

        // All session time per date
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT SessionDate, SUM(LengthSeconds + AdminSeconds)
                FROM Sessions
                WHERE SessionDate >= @s AND SessionDate <= @e
                GROUP BY SessionDate";
            cmd.Parameters.AddWithValue("@s", startStr);
            cmd.Parameters.AddWithValue("@e", endStr);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ws = DashboardService.GetWeekStart(DateOnly.Parse(r.GetString(0)));
                if (auditTotals.ContainsKey(ws)) auditTotals[ws] += r.GetInt32(1);
            }
        }

        // Solo CS time for solo-type auditors per date
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.SessionDate, SUM(cr.ReviewLengthSeconds)
                FROM CsReviews cr
                JOIN Sessions  s ON s.SessionId = cr.SessionId
                JOIN Auditors  a ON a.AuditorId = cr.CsId
                WHERE s.SessionDate >= @s AND s.SessionDate <= @e
                  AND s.PcId = s.AuditorId
                  AND a.Type IN (2, 3)
                GROUP BY s.SessionDate";
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
                SELECT VisitDate, PersonId FROM Students
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
                SELECT SessionDate, PcId FROM Sessions
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

        return weeks.Select(w => new WeekStatSummary(
            w,
            auditTotals[w],
            academyPersons[w].Count,
            bodyPersons[w].Count,
            pcPersons[w].Count
        )).ToList();
    }

    /// <summary>
    /// Returns auditing hours (LengthSeconds + AdminSeconds) grouped by PC Origin for the given week.
    /// Ordered by total descending.
    /// </summary>
    public List<OriginHours> GetWeekOriginHours(DateOnly weekStart)
    {
        var weekEnd  = weekStart.AddDays(6);
        var startStr = weekStart.ToString("yyyy-MM-dd");
        var endStr   = weekEnd.ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(NULLIF(pc.Origin,''), 'Unknown') AS Origin,
                   SUM(s.LengthSeconds + s.AdminSeconds) AS TotalSec
            FROM Sessions s
            JOIN PCs pc ON pc.PcId = s.PcId
            WHERE s.SessionDate >= @s AND s.SessionDate <= @e
            GROUP BY COALESCE(NULLIF(pc.Origin,''), 'Unknown')
            ORDER BY TotalSec DESC";
        cmd.Parameters.AddWithValue("@s", startStr);
        cmd.Parameters.AddWithValue("@e", endStr);

        var list = new List<OriginHours>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OriginHours(r.GetString(0), r.GetInt32(1)));
        return list;
    }
}
