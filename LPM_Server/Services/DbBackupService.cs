using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LPM.Services;

/// <summary>
/// Runs every 4 hours:
///   1. Integrity-checks the live DB.
///   2. If healthy → creates a timestamped backup, prunes to keep last 42 files.
///   3. If corrupt  → finds the newest healthy backup and auto-restores it.
/// </summary>
public class DbBackupService(
    IConfiguration config,
    ILogger<DbBackupService> logger,
    FolderService folderSvc) : BackgroundService
{
    private const int MaxBackups    = 42;
    private const int IntervalHours = 4;

    // For CPU % calculation across cycles
    private static TimeSpan _lastCpuTime  = TimeSpan.Zero;
    private static DateTime _lastWallTime = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Brief startup delay so the rest of the app initialises first
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        // If a recent backup already exists, wait out the remaining interval instead
        // of backing up immediately (prevents a backup on every app restart).
        var firstDelay = TimeUntilNextBackup();
        if (firstDelay > TimeSpan.Zero)
        {
            Console.WriteLine($"[DbBackup] Recent backup found — next backup in {firstDelay:hh\\:mm\\:ss}");
            await Task.Delay(firstDelay, ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try { RunCycle(); }
            catch (Exception ex) { logger.LogError(ex, "[DbBackup] Unhandled error in backup cycle"); }

            await Task.Delay(TimeSpan.FromHours(IntervalHours), ct);
        }
    }

    /// Returns how long to wait before the first backup is due, based on the newest
    /// existing backup file. Returns Zero if no recent backup exists.
    private TimeSpan TimeUntilNextBackup()
    {
        try
        {
            var folder = GetBackupFolder();
            var newest = Directory.GetFiles(folder, "lifepower_*.db")
                                  .OrderByDescending(f => f)
                                  .FirstOrDefault();
            if (newest is null) return TimeSpan.Zero;

            var age = DateTime.Now - File.GetLastWriteTime(newest);
            var remaining = TimeSpan.FromHours(IntervalHours) - age;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        catch { return TimeSpan.Zero; }
    }

    private void RunCycle()
    {
        LogSystemStats();
        var backupFolder = GetBackupFolder();
        var integrity    = folderSvc.CheckIntegrity();

        if (integrity != "ok")
        {
            var msg = $"[DbBackup {DateTime.Now:yyyy-MM-dd HH:mm:ss}] INTEGRITY FAILED: {integrity} — attempting auto-restore";
            logger.LogCritical("[DbBackup] Integrity check FAILED: {Result} — attempting auto-restore", integrity);
            Console.WriteLine(msg);
            TryAutoRestore(backupFolder);
            return;
        }

        // Create backup
        var timestamp  = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var backupPath = Path.Combine(backupFolder, $"lifepower_{timestamp}.db");
        folderSvc.BackupDbTo(backupPath);
        logger.LogInformation("[DbBackup] Backup saved: {File}", Path.GetFileName(backupPath));
        Console.WriteLine($"[DbBackup {DateTime.Now:yyyy-MM-dd HH:mm:ss}] Integrity OK. Backup saved: {Path.GetFileName(backupPath)}");

        // Prune regular backups: keep only the newest MaxBackups files
        var all = Directory.GetFiles(backupFolder, "lifepower_*.db")
                           .OrderByDescending(f => f)
                           .ToList();

        foreach (var old in all.Skip(MaxBackups))
        {
            try   { File.Delete(old); logger.LogInformation("[DbBackup] Deleted old backup: {File}", Path.GetFileName(old)); }
            catch (Exception ex) { logger.LogWarning(ex, "[DbBackup] Could not delete: {File}", Path.GetFileName(old)); }
        }

        // Prune BeforeImport snapshots: keep only the newest MaxBackups files
        var importSnaps = Directory.GetFiles(backupFolder, "*BeforeImport*.db")
                                   .OrderByDescending(f => f)
                                   .ToList();

        foreach (var old in importSnaps.Skip(MaxBackups))
        {
            try   { File.Delete(old); logger.LogInformation("[DbBackup] Deleted old BeforeImport snapshot: {File}", Path.GetFileName(old)); }
            catch (Exception ex) { logger.LogWarning(ex, "[DbBackup] Could not delete BeforeImport snapshot: {File}", Path.GetFileName(old)); }
        }
    }

    private void TryAutoRestore(string backupFolder)
    {
        var candidates = Directory.GetFiles(backupFolder, "lifepower_*.db")
                                  .OrderByDescending(f => f)   // newest first
                                  .ToList();

        if (candidates.Count == 0)
        {
            logger.LogCritical("[DbBackup] DB is corrupt and NO backups exist. Manual intervention required.");
            return;
        }

        foreach (var backup in candidates)
        {
            var check = folderSvc.CheckBackupIntegrity(backup);
            if (check != "ok")
            {
                logger.LogWarning("[DbBackup] Backup {File} is also corrupt, skipping", Path.GetFileName(backup));
                continue;
            }

            folderSvc.RestoreFromBackup(backup);
            logger.LogWarning("[DbBackup] *** AUTO-RESTORED from {File} ***", Path.GetFileName(backup));
            Console.WriteLine($"[DbBackup {DateTime.Now:yyyy-MM-dd HH:mm:ss}] AUTO-RESTORED from {Path.GetFileName(backup)}");
            return;
        }

        logger.LogCritical("[DbBackup] All backups are corrupt. Manual intervention required.");
        Console.WriteLine($"[DbBackup {DateTime.Now:yyyy-MM-dd HH:mm:ss}] CRITICAL: DB corrupt and all backups are corrupt. Manual intervention required.");
    }

    private string GetBackupFolder() =>
        folderSvc.GetAutoBackupFolder(config["Database:BackupFolder"]);

    // ── System stats logging ─────────────────────────────────────────────────

    private void LogSystemStats()
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var (totalMb, usedMb, availMb) = ReadServerMemory();
            string serverLine = usedMb.HasValue
                ? $"[DbBackup {ts}] Server RAM : {totalMb:N0} MB total | {usedMb:N0} MB used ({(double)usedMb / totalMb * 100:F1}%) | {availMb:N0} MB available"
                : $"[DbBackup {ts}] Server RAM : {totalMb:N0} MB total (used/available not available on this OS)";
            Console.WriteLine(serverLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DbBackup {ts}] Server RAM : unavailable ({ex.Message})");
        }

        try
        {
            var proc        = Process.GetCurrentProcess();
            var wsMb        = proc.WorkingSet64       / 1024 / 1024;
            var privateMb   = proc.PrivateMemorySize64 / 1024 / 1024;
            var gcMb        = GC.GetTotalMemory(false) / 1024 / 1024;

            // CPU % = cpu-time delta / (wall-time delta × logical cores)
            var nowCpu  = proc.TotalProcessorTime;
            var nowWall = DateTime.UtcNow;
            string cpuStr;
            if (_lastWallTime == DateTime.MinValue)
            {
                cpuStr = "--% (first run)";
            }
            else
            {
                var cpuDelta  = (nowCpu  - _lastCpuTime).TotalSeconds;
                var wallDelta = (nowWall - _lastWallTime).TotalSeconds;
                var cores     = Environment.ProcessorCount;
                var pct       = wallDelta > 0 ? cpuDelta / (wallDelta * cores) * 100.0 : 0;
                cpuStr = $"{pct:F1}%";
            }
            _lastCpuTime  = nowCpu;
            _lastWallTime = nowWall;

            Console.WriteLine($"[DbBackup {ts}] Process    : {wsMb:N0} MB working set | {privateMb:N0} MB private | {gcMb:N0} MB GC heap | CPU (last {IntervalHours}h avg): {cpuStr}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DbBackup {ts}] Process stats: unavailable ({ex.Message})");
        }
    }

    /// <summary>
    /// Returns (totalMb, usedMb?, availMb?) for the whole server.
    /// On Linux reads /proc/meminfo. On other OSes falls back to GC info (total only).
    /// </summary>
    private static (long totalMb, long? usedMb, long? availMb) ReadServerMemory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            long totalKb = 0, availKb = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:"))
                    totalKb = ParseProcMemLine(line);
                else if (line.StartsWith("MemAvailable:"))
                    availKb = ParseProcMemLine(line);
                if (totalKb > 0 && availKb > 0) break;
            }
            var totalMb = totalKb / 1024;
            var availMb = availKb / 1024;
            return (totalMb, totalMb - availMb, availMb);
        }

        // Fallback: GC knows total RAM but not used/available breakdown
        var gcInfo  = GC.GetGCMemoryInfo();
        var fallbackMb = gcInfo.TotalAvailableMemoryBytes / 1024 / 1024;
        return (fallbackMb, null, null);
    }

    private static long ParseProcMemLine(string line)
    {
        // Format: "MemTotal:       16384000 kB"
        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return 0;
        var numStr = parts[1].Replace("kB", "", StringComparison.OrdinalIgnoreCase).Trim();
        return long.TryParse(numStr, out var v) ? v : 0;
    }
}
