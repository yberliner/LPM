using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace LPM.Services;

/// <summary>
/// Nightly background job that prunes unbounded log/audit tables and sweeps
/// stale in-memory state. Runs every 24 hours; first sweep 5 minutes after
/// startup (so startup is not further burdened).
/// </summary>
public class MaintenanceService : BackgroundService
{
    private readonly string _connectionString;
    private readonly UserActivityService _activitySvc;

    public MaintenanceService(IConfiguration config, UserActivityService activitySvc)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        _activitySvc = activitySvc;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run an immediate one-shot prune so existing oversized tables drop straight
        // after a deploy — without waiting 5 minutes (and another 24 hours after that)
        // for the regular cycle. Wrapped in its own try so a startup failure here can't
        // prevent the regular maintenance loop from running.
        try { RunPruneJobs(); }
        catch (Exception ex) { Console.WriteLine($"[Maintenance] Startup prune error: {ex.Message}"); }

        // Delay subsequent runs so startup finishes cleanly.
        try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { RunPruneJobs(); }
            catch (Exception ex) { Console.WriteLine($"[Maintenance] Prune error: {ex.Message}"); }

            try { _activitySvc.SweepStaleInteractions(); }
            catch (Exception ex) { Console.WriteLine($"[Maintenance] Sweep error: {ex.Message}"); }

            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    // Hard cap on sys_activity_log row count. The Diagnosis "Database" tab loads this
    // table into RAM as a List<List<string>> per circuit; once it gets above ~10K rows
    // the per-circuit memory cost climbs into double-digit MB. Capping at 5K keeps
    // diagnostic value (a few days of activity for a busy install) without that cost.
    private const int ActivityLogMaxRows = 5_000;

    private void RunPruneJobs()
    {
        // Retention windows — tune via these constants if needed.
        var jobs = new (string Label, string Sql)[]
        {
            // Row-count cap (replaces the old 90-day rule — that let busy installs
            // accumulate 100K+ rows). Keeps the latest 5K by Id, deletes the rest.
            ($"sys_activity_log keep latest {ActivityLogMaxRows}",
                $"DELETE FROM sys_activity_log WHERE Id NOT IN (SELECT Id FROM sys_activity_log ORDER BY Id DESC LIMIT {ActivityLogMaxRows})"),
            ("sys_file_audit > 365d",
                "DELETE FROM sys_file_audit WHERE CreatedAt < datetime('now', '-365 days')"),
            ("sys_shrunk_files > 365d",
                "DELETE FROM sys_shrunk_files WHERE ShrunkAt < datetime('now', '-365 days')"),
            ("sys_staff_messages acked > 730d",
                "DELETE FROM sys_staff_messages WHERE AcknowledgedAt IS NOT NULL AND AcknowledgedAt < datetime('now', '-730 days')"),
            ("sys_magic_links used/expired > 30d",
                "DELETE FROM sys_magic_links WHERE (UsedAt IS NOT NULL AND UsedAt < datetime('now', '-30 days')) OR ExpiresAt < datetime('now', '-30 days')"),
            ("sys_trusted_devices > 180d",
                "DELETE FROM sys_trusted_devices WHERE CreatedAt < datetime('now', '-180 days')"),
        };

        Console.WriteLine($"[Maintenance] {DateTime.Now:yyyy-MM-dd HH:mm:ss} — RunPruneJobs starting ({jobs.Length} jobs)");
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        int totalDeleted = 0;
        foreach (var (label, sql) in jobs)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                var deleted = cmd.ExecuteNonQuery();
                totalDeleted += deleted;
                // Always log every job — including the 0-deleted no-ops — so it's
                // visible in journalctl/stdout that the maintenance pass is firing
                // and which retention rule did or didn't trim anything.
                Console.WriteLine(deleted > 0
                    ? $"[Maintenance]   ✓ {label}: deleted {deleted} row(s)"
                    : $"[Maintenance]   • {label}: nothing to delete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Maintenance]   ✗ {label}: FAILED — {ex.Message}");
            }
        }
        Console.WriteLine($"[Maintenance] {DateTime.Now:yyyy-MM-dd HH:mm:ss} — RunPruneJobs done (total deleted: {totalDeleted})");
    }
}
