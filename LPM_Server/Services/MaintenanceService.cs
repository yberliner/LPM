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
        // Delay first run so startup finishes cleanly.
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

    private void RunPruneJobs()
    {
        // Retention windows — tune via these constants if needed.
        var jobs = new (string Label, string Sql)[]
        {
            ("sys_activity_log > 90d",
                "DELETE FROM sys_activity_log WHERE ActivityAt < datetime('now', '-90 days')"),
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

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        foreach (var (label, sql) in jobs)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                var deleted = cmd.ExecuteNonQuery();
                if (deleted > 0)
                    Console.WriteLine($"[Maintenance] Pruned {deleted} row(s) — {label}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Maintenance] Prune job failed ({label}): {ex.Message}");
            }
        }
    }
}
