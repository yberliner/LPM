using Microsoft.Data.Sqlite;

namespace LPM.Services;

/// <summary>
/// Background service that runs once per day at 1 AM.
/// Iterates all PDF files under PC-Folders and shrinks those that:
///   1. Were last modified after 2026-03-23 (the feature introduction date).
///   2. Were last modified at least 10 days ago.
///   3. Have not already been shrunk (not present in sys_shrunk_files).
///
/// TEST MODE: Any PC whose person has FirstName = 'admin' (case-sensitive) is a force-shrink
/// PC — all its PDF files are shrunk regardless of age/cutoff, as long as they are not already
/// in sys_shrunk_files.
///
/// Before shrinking each file, backs up the original to db-backups/{yyyyMMdd}_shrink_pdf/.
/// Records every successfully shrunk file in sys_shrunk_files.
/// Prunes shrink backup dirs older than 10 days.
/// </summary>
public class PdfShrinkService(
    IConfiguration config,
    ILogger<PdfShrinkService> logger,
    FolderService folderSvc) : BackgroundService
{
    // Files created on or before this date are never candidates (pre-feature backlog).
    private static readonly DateTime CutoffDate = new(2026, 3, 23);

    private int _running = 0; // 0 = idle, 1 = running (Interlocked guard)

    /// <summary>Manually trigger the shrink cycle (e.g. from the Diagnosis page). No-op if already running.</summary>
    public async Task<string> RunManualAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return "Already running — please wait.";
        try
        {
            await RunCycleAsync(ct);
            return "Done.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public bool IsRunning => _running == 1;

    public override async Task StopAsync(CancellationToken ct)
    {
        Console.WriteLine($"[PdfShrink] {DateTime.Now:yyyy-MM-dd HH:mm:ss} — Service stopping");
        await base.StopAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), ct); // startup grace

        var firstDelay = TimeUntilNext1AM();
        Console.WriteLine($"[PdfShrink] Service started — first run in {firstDelay:hh\\:mm\\:ss}");
        await Task.Delay(firstDelay, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Interlocked.CompareExchange(ref _running, 1, 0) == 0)
                {
                    try { await RunCycleAsync(ct); }
                    finally { Interlocked.Exchange(ref _running, 0); }
                }

            }
            catch (Exception ex) { logger.LogError(ex, "[PdfShrink] Unhandled error in shrink cycle"); }

            await Task.Delay(TimeUntilNext1AM(), ct);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        Console.WriteLine($"[PdfShrink] {now:yyyy-MM-dd HH:mm:ss} — Starting nightly PDF shrink cycle");

        var pcFoldersRoot = folderSvc.GetPcFoldersRoot();
        if (!Directory.Exists(pcFoldersRoot))
        {
            Console.WriteLine($"[PdfShrink] PC-Folders root not found: {pcFoldersRoot} — skipping");
            return;
        }

        var backupBase = folderSvc.GetAutoBackupFolder(config["Database:BackupFolder"]);
        var backupDir  = Path.Combine(backupBase, $"{now:yyyyMMdd}_shrink_pdf");

        var connectionString  = GetConnectionString();
        var alreadyShrunk     = await LoadShrunkPathsAsync(connectionString, ct);
        var forcePcPaths      = await LoadAdminPcPathsAsync(connectionString, pcFoldersRoot, ct);

        if (forcePcPaths.Count > 0)
            Console.WriteLine($"[PdfShrink] Force-shrink (admin) PC folders: {string.Join(", ", forcePcPaths.Select(Path.GetFileName))}");

        var candidates = Directory
            .EnumerateFiles(pcFoldersRoot, "*.pdf", SearchOption.AllDirectories)
            .Where(f => IsEligible(f, pcFoldersRoot, alreadyShrunk, forcePcPaths))
            .ToList();

        Console.WriteLine($"[PdfShrink] Found {candidates.Count} candidate file(s) to process");

        int shrunkCount = 0, skippedCount = 0;

        foreach (var fullPath in candidates)
        {
            if (ct.IsCancellationRequested) break;

            var relativePath = Path.GetRelativePath(pcFoldersRoot, fullPath).Replace('\\', '/');

            // Backup original before touching it
            var backupTarget = Path.Combine(backupDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
                File.Copy(fullPath, backupTarget, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfShrink] Could not back up '{relativePath}': {ex.Message} — skipping");
                skippedCount++;
                continue;
            }

            var (shrunk, originalKb, shrunkKb) = folderSvc.TryShrinkEncryptedPdf(fullPath);

            if (shrunk)
            {
                await RecordShrunkFileAsync(connectionString, relativePath, originalKb, shrunkKb, ct);
                Console.WriteLine($"[PdfShrink] Shrunk: {relativePath}  {originalKb}KB → {shrunkKb}KB  ({100 - shrunkKb * 100 / Math.Max(1, originalKb)}% smaller)");
                shrunkCount++;
            }
            else
            {
                // Not smaller — remove the pointless backup copy
                try { File.Delete(backupTarget); } catch { }
                skippedCount++;
            }
        }

        // Remove backup dir if nothing was actually backed up
        if (shrunkCount == 0 && Directory.Exists(backupDir))
        {
            try { Directory.Delete(backupDir, recursive: true); } catch { }
        }

        Console.WriteLine($"[PdfShrink] Cycle complete — {shrunkCount} shrunk, {skippedCount} unchanged/skipped");

        PruneOldBackupDirs(backupBase);
    }

    private static void PruneOldBackupDirs(string backupBase)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-10);
            foreach (var dir in Directory.EnumerateDirectories(backupBase, "*_shrink_pdf"))
            {
                if (Directory.GetLastWriteTime(dir) < cutoff)
                {
                    Directory.Delete(dir, recursive: true);
                    Console.WriteLine($"[PdfShrink] Deleted old backup dir: {Path.GetFileName(dir)}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PdfShrink] Could not prune old backup dirs: {ex.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsEligible(
        string fullPath,
        string pcFoldersRoot,
        HashSet<string> alreadyShrunk,
        List<string> forcePcPaths)
    {
        try
        {
            var relativePath = Path.GetRelativePath(pcFoldersRoot, fullPath).Replace('\\', '/');

            // Never shrink a file that was already shrunk
            if (alreadyShrunk.Contains(relativePath)) return false;

            // Force-shrink: file is under an admin test PC — skip all date checks
            if (forcePcPaths.Any(fp => fullPath.StartsWith(fp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                    || fullPath.StartsWith(fp + "/", StringComparison.OrdinalIgnoreCase)))
                return true;

            var lastWrite = File.GetLastWriteTime(fullPath);

            // Must be strictly after the cutoff date
            if (lastWrite <= CutoffDate) return false;

            // Must be at least 10 days old
            if ((DateTime.Now - lastWrite).TotalDays < 10) return false;

            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns absolute folder paths for all PCs where core_persons.FirstName = 'admin'
    /// (case-sensitive via GLOB). These PCs bypass date/age eligibility checks.
    /// </summary>
    private async Task<List<string>> LoadAdminPcPathsAsync(
        string connectionString, string pcFoldersRoot, CancellationToken ct)
    {
        var paths = new List<string>();
        try
        {
            using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT c.PcId
                FROM core_pcs c
                JOIN core_persons p ON p.PersonId = c.PcId
                WHERE LOWER(p.FirstName) = 'admin'
                """;
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var pcId   = reader.GetInt32(0);
                var folder = folderSvc.FindPcFolder(pcId);
                if (folder != null) paths.Add(folder);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PdfShrink] Could not load admin test PCs: {ex.Message}");
        }
        return paths;
    }

    private static async Task<HashSet<string>> LoadShrunkPathsAsync(string connectionString, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT FilePath FROM sys_shrunk_files";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(reader.GetString(0));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PdfShrink] Could not load sys_shrunk_files: {ex.Message}");
        }
        return result;
    }

    private static async Task RecordShrunkFileAsync(
        string connectionString, string relativePath,
        long originalKb, long shrunkKb, CancellationToken ct)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sys_shrunk_files (FilePath, ShrunkAt, OriginalSizeKb, ShrunkSizeKb)
                VALUES ($path, $at, $orig, $shrunk)
                """;
            cmd.Parameters.AddWithValue("$path",   relativePath);
            cmd.Parameters.AddWithValue("$at",     DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$orig",   originalKb);
            cmd.Parameters.AddWithValue("$shrunk", shrunkKb);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PdfShrink] Could not record shrunk file '{relativePath}': {ex.Message}");
        }
    }

    private string GetConnectionString()
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        return $"Data Source={dbPath}";
    }

    private static TimeSpan TimeUntilNext1AM()
    {
        var now  = DateTime.Now;
        var next = now.Date.AddHours(1); // today at 01:00
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
