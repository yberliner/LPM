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

        // Prune: keep only the newest MaxBackups files
        var all = Directory.GetFiles(backupFolder, "lifepower_*.db")
                           .OrderByDescending(f => f)
                           .ToList();

        foreach (var old in all.Skip(MaxBackups))
        {
            try   { File.Delete(old); logger.LogInformation("[DbBackup] Deleted old backup: {File}", Path.GetFileName(old)); }
            catch (Exception ex) { logger.LogWarning(ex, "[DbBackup] Could not delete: {File}", Path.GetFileName(old)); }
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
}
