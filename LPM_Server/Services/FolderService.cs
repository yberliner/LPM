using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace LPM.Services;

public record FolderFileItem(string FileName, string Section, string RelativePath, DateTime? DateParsed);
public record WorkSheetItem(FolderFileItem File, List<FolderFileItem> Attachments);
public record FolderTreeNode(string Name, string RelativePath, bool IsFolder, DateTime? DateParsed, List<FolderTreeNode> Children);
public record PcFolderInfo(int PcId, string PcName, string FolderPath, List<FolderFileItem> FrontCover, List<FolderFileItem> BackCover, List<WorkSheetItem> WorkSheets, FolderTreeNode FrontCoverTree, FolderTreeNode BackCoverTree);

public static class BackupProgress
{
    public static volatile int Current;
    public static volatile bool Running;
    public static volatile bool CancelRequested;
    public static string CurrentFile = "";
    public static string? AuthToken;
    public static DateTime AuthExpiry;
    public static string? LastError;      // set on failure, cleared at start
    public static volatile bool WasStarted; // true once Running is first set to true

    // Brute-force protection: IP → (failCount, lockedUntil)
    static readonly Dictionary<string, (int Fails, DateTime LockedUntil)> _ipLocks = new();
    static readonly object _lockObj = new();
    const int MaxAttempts = 5;
    static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public static bool IsLockedOut(string ip)
    {
        lock (_lockObj)
        {
            if (!_ipLocks.TryGetValue(ip, out var entry)) return false;
            if (entry.LockedUntil > DateTime.UtcNow) return true;
            if (entry.LockedUntil != default && entry.LockedUntil <= DateTime.UtcNow)
                _ipLocks.Remove(ip); // lock expired
            return false;
        }
    }

    /// <summary>Records a failure and returns the number of attempts remaining.</summary>
    public static int RecordFailure(string ip)
    {
        lock (_lockObj)
        {
            var entry = _ipLocks.GetValueOrDefault(ip);
            var fails = entry.Fails + 1;
            var locked = fails >= MaxAttempts ? DateTime.UtcNow.Add(LockDuration) : default;
            _ipLocks[ip] = (fails, locked);
            return Math.Max(0, MaxAttempts - fails);
        }
    }

    public static void ClearFailures(string ip)
    {
        lock (_lockObj) { _ipLocks.Remove(ip); }
    }
}

public class FolderService
{
    private readonly string _basePath;
    private readonly string _connectionString;
    private readonly string? _ghostscriptExe;
    private readonly string? _libreOfficePath;
    private readonly byte[]? _encKey;

    public FolderService(IConfiguration config)
    {
        _basePath = Path.Combine(Directory.GetCurrentDirectory(), "PC-Folders");
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        _ghostscriptExe = config["GhostscriptExe"];
        _libreOfficePath = config["LibreOfficePath"];
        var keyStr = config["EncryptionKey"];
        if (!string.IsNullOrEmpty(keyStr))
            _encKey = Convert.FromBase64String(keyStr);
    }

    /// <summary>Returns free space in bytes on the drive where PC-Folders is located.</summary>
    public long GetFreeSpaceBytes()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(_basePath));
        if (string.IsNullOrEmpty(root)) return 0;
        var drive = new DriveInfo(root);
        return drive.AvailableFreeSpace;
    }

    public static readonly HashSet<string> ConvertibleExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".doc", ".docx", ".odt", ".rtf" };

    public bool CanConvertToPdf => !string.IsNullOrEmpty(_libreOfficePath) && File.Exists(_libreOfficePath);

    /// <summary>Convert a doc/docx file to PDF using LibreOffice. Returns the PDF bytes, or null on failure.</summary>
    public byte[]? ConvertToPdf(byte[] docBytes, string originalFileName)
    {
        if (!CanConvertToPdf) return null;

        var tempDir = Path.Combine(Path.GetTempPath(), $"lpm_convert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var inputPath = Path.Combine(tempDir, originalFileName);
            File.WriteAllBytes(inputPath, docBytes);

            var args = $"--headless --convert-to pdf --outdir \"{tempDir}\" \"{inputPath}\"";
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _libreOfficePath!,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process.Start();
            process.WaitForExit(60_000);

            var pdfName = Path.GetFileNameWithoutExtension(originalFileName) + ".pdf";
            var pdfPath = Path.Combine(tempDir, pdfName);

            if (process.ExitCode == 0 && File.Exists(pdfPath))
            {
                var pdfBytes = File.ReadAllBytes(pdfPath);
                Console.WriteLine($"[Doc→PDF] {originalFileName} → {pdfBytes.Length / 1024}KB");
                return pdfBytes;
            }

            var err = process.StandardError.ReadToEnd();
            Console.WriteLine($"[Doc→PDF] Failed for {originalFileName}: exit={process.ExitCode} {err}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Doc→PDF] Error: {ex.Message}");
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public string? GetPcName(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''), '')) FROM core_persons WHERE PersonId = @id";
        cmd.Parameters.AddWithValue("@id", pcId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Find the PC's folder on disk (pattern: {pcId}-{name})</summary>
    public string? FindPcFolder(int pcId)
    {
        if (!Directory.Exists(_basePath)) return null;
        var prefix = $"{pcId}-";
        return Directory.GetDirectories(_basePath)
            .FirstOrDefault(d => Path.GetFileName(d).StartsWith(prefix));
    }

    public PcFolderInfo? GetPcFolder(int pcId)
    {
        var pcName = GetPcName(pcId) ?? $"PC {pcId}";
        var folder = FindPcFolder(pcId);
        if (folder == null)
        {
            // Create folder if it doesn't exist
            folder = Path.Combine(_basePath, $"{pcId}-{pcName}");
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "Front_Cover"));
            Directory.CreateDirectory(Path.Combine(folder, "Back_Cover"));
            Directory.CreateDirectory(Path.Combine(folder, "WorkSheets"));
        }

        var frontCover = GetFilesForSection(folder, "Front_Cover");
        var backCover = GetFilesForSection(folder, "Back_Cover");
        var workSheets = GetWorkSheets(folder);
        var frontTree = BuildSectionTree(folder, "Front_Cover");
        var backTree = BuildSectionTree(folder, "Back_Cover");

        return new PcFolderInfo(pcId, pcName, folder, frontCover, backCover, workSheets, frontTree, backTree);
    }

    private List<WorkSheetItem> GetWorkSheets(string pcFolder)
    {
        var wsPath = Path.Combine(pcFolder, "WorkSheets");
        if (!Directory.Exists(wsPath)) return [];

        var allPdfs = Directory.GetFiles(wsPath, "*.pdf").Select(Path.GetFileName).ToList();

        // Session files = PDFs that don't contain _att_ in their name
        var files = allPdfs
            .Where(name => !name!.Contains("_att_", StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                var relativePath = $"WorkSheets/{name}";
                return new FolderFileItem(name!, "WorkSheets", relativePath, null);
            })
            .OrderByDescending(f => f.FileName)
            .ToList();

        var items = new List<WorkSheetItem>();
        foreach (var file in files)
        {
            // Attachments are files matching {sessionNameNoExt}_att_*.pdf
            var nameNoExt = Path.GetFileNameWithoutExtension(file.FileName);
            var prefix = $"{nameNoExt}_att_";
            var attachments = allPdfs
                .Where(n => n!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(n =>
                {
                    var relPath = $"WorkSheets/{n}";
                    return new FolderFileItem(n!, "WorkSheets", relPath, null);
                })
                .OrderBy(af => af.FileName)
                .ToList();
            items.Add(new WorkSheetItem(file, attachments));
        }
        return items;
    }

    private List<FolderFileItem> GetFilesForSection(string pcFolder, string section)
    {
        var sectionPath = Path.Combine(pcFolder, section);
        if (!Directory.Exists(sectionPath)) return [];

        var extensions = new[] { "*.pdf", "*.xlsx" };
        return extensions
            .SelectMany(ext => Directory.GetFiles(sectionPath, ext, SearchOption.AllDirectories))
            .Select(f =>
            {
                var name = Path.GetFileName(f);
                var dateParsed = TryParseDatePrefix(name);
                var relativePath = Path.GetRelativePath(pcFolder, f).Replace('\\', '/');
                return new FolderFileItem(name, section, relativePath, dateParsed);
            })
            .ToList();
    }

    /// <summary>Parse date prefix from filename. Supports: yy-MM-dd_, yyMMdd, yyMMdd.</summary>
    private static DateTime? TryParseDatePrefix(string fileName)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var ds = System.Globalization.DateTimeStyles.None;

        // Format: yy-MM-dd_Name.pdf (e.g. 26-03-14_session.pdf)
        if (fileName.Length >= 9 && fileName[2] == '-' && fileName[5] == '-' && fileName[8] == '_')
        {
            if (DateTime.TryParseExact("20" + fileName[..8], "yyyy-MM-dd", ci, ds, out var dt1))
                return dt1;
        }

        // Format: yyMMdd followed by non-digit (e.g. 250310.pdf, 250310 something.pdf)
        if (fileName.Length >= 6 && fileName[..6].All(char.IsDigit))
        {
            var sep = fileName.Length > 6 ? fileName[6] : '.';
            if (!char.IsDigit(sep))
            {
                if (DateTime.TryParseExact("20" + fileName[..6], "yyyyMMdd", ci, ds, out var dt2))
                    return dt2;
            }
        }

        return null;
    }

    /// <summary>Get the absolute path for a file given pcId and relative path</summary>
    /// <summary>Get the absolute path for a file (for existence checks only).</summary>
    public string? GetFilePath(int pcId, string relativePath)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return null;

        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null) return null;

        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>Read a file and return decrypted bytes. Handles both encrypted and legacy unencrypted files.</summary>
    public byte[]? ReadFileBytes(int pcId, string relativePath, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return null;

        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return null;

        return DecryptBytes(File.ReadAllBytes(fullPath));
    }

    /// <summary>Save annotated PDF bytes back to disk (encrypted)</summary>
    public bool SaveFile(int pcId, string relativePath, byte[] pdfBytes, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;

        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null) return false;

        File.WriteAllBytes(fullPath, EncryptBytes(pdfBytes));
        Console.WriteLine($"[FolderService] Saved file PC {pcId}: {relativePath}");
        return true;
    }

    // ── Backup ────────────────────────────────────────────────

    /// <summary>Copy a PC file to _backups/ before modifying it.</summary>
    public void BackupFile(int pcId, string relativePath, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return;
        BackupFromFolder(pcId, folder, relativePath);
    }

    private void BackupFromFolder(int pcId, string folder, string relativePath)
    {

        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return;

        var backupDir = Path.Combine(_basePath, "_backups");
        Directory.CreateDirectory(backupDir);

        // Clean up files older than 10 days
        foreach (var old in Directory.GetFiles(backupDir))
        {
            if (File.GetCreationTime(old) < DateTime.Now.AddDays(-10))
                try { File.Delete(old); } catch { }
        }

        // Build backup filename: pcId_filename
        var fileName = Path.GetFileName(fullPath);
        var backupName = $"{pcId}_{fileName}";
        var backupPath = Path.Combine(backupDir, backupName);

        // Add postfix if already exists
        if (File.Exists(backupPath))
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(backupName);
            var ext = Path.GetExtension(backupName);
            var counter = 2;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(backupDir, $"{nameNoExt}_{counter}{ext}");
                counter++;
            }
        }

        File.Copy(fullPath, backupPath);
        Console.WriteLine($"[FolderService] Backed up file PC {pcId}: {relativePath}");
    }

    /// <summary>Backup raw bytes (e.g. original upload before shrink/encrypt) to _backups/.</summary>
    public void BackupBytes(int pcId, string fileName, byte[] bytes)
    {
        var backupDir = Path.Combine(_basePath, "_backups");
        Directory.CreateDirectory(backupDir);

        // Clean up files older than 10 days
        foreach (var old in Directory.GetFiles(backupDir))
        {
            if (File.GetCreationTime(old) < DateTime.Now.AddDays(-10))
                try { File.Delete(old); } catch { }
        }

        var backupName = $"{pcId}_{fileName}";
        var backupPath = Path.Combine(backupDir, backupName);

        if (File.Exists(backupPath))
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(backupName);
            var ext = Path.GetExtension(backupName);
            var counter = 2;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(backupDir, $"{nameNoExt}_{counter}{ext}");
                counter++;
            }
        }

        File.WriteAllBytes(backupPath, bytes);
        Console.WriteLine($"[FolderService] Backed up bytes '{fileName}' for PC {pcId}");
    }

    // ── DB health ─────────────────────────────────────────────

    /// <summary>
    /// Run once at startup: enables WAL journal mode and NORMAL sync level.
    /// WAL mode survives crashes without corruption; NORMAL is safe with WAL and faster than FULL.
    /// </summary>
    public void InitializeDb()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Runs SQLite's built-in integrity check.
    /// Returns "ok" if the database is healthy, or an error description if corrupt.
    /// </summary>
    public string CheckIntegrity()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return cmd.ExecuteScalar()?.ToString() ?? "error";
    }

    /// <summary>
    /// Creates a consistent point-in-time copy of the DB using SQLite's own backup API.
    /// Safer than raw file copy — works correctly even while the DB is being written to.
    /// </summary>
    public void BackupDbTo(string destinationPath)
    {
        using var source = new SqliteConnection(_connectionString);
        source.Open();
        // Pooling=False ensures the file handle is released immediately on Dispose,
        // not returned to the connection pool (which would keep the file locked).
        using var dest = new SqliteConnection($"Data Source={destinationPath};Pooling=False");
        dest.Open();
        source.BackupDatabase(dest);
    }

    /// <summary>Runs integrity check on any arbitrary SQLite file (e.g. a backup).</summary>
    public string CheckBackupIntegrity(string dbFilePath)
    {
        using var conn = new SqliteConnection($"Data Source={dbFilePath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return cmd.ExecuteScalar()?.ToString() ?? "error";
    }

    /// <summary>
    /// Replaces the live DB with a backup file.
    /// Removes WAL/SHM files first so the restored DB starts clean.
    /// </summary>
    public void RestoreFromBackup(string backupPath)
    {
        var dbPath = GetDbFilePath();
        foreach (var ext in new[] { "-wal", "-shm" })
        {
            var f = dbPath + ext;
            if (File.Exists(f)) try { File.Delete(f); } catch { }
        }
        File.Copy(backupPath, dbPath, overwrite: true);
    }

    // ── Full backup (DB + PC-Folders) ─────────────────────────

    /// <summary>Returns the absolute path to the lifepower.db file.</summary>
    public string GetDbFilePath()
    {
        // _connectionString is "Data Source=lifepower.db" (or custom path)
        var src = _connectionString.Replace("Data Source=", "");
        return Path.GetFullPath(src);
    }

    /// <summary>
    /// Returns the absolute path to the auto-backup folder (creates it if missing).
    /// Pass the configured relative/absolute path from appsettings (e.g. "db-backups").
    /// </summary>
    public string GetAutoBackupFolder(string? configuredPath = null)
    {
        var folder = configuredPath ?? "db-backups";
        if (!Path.IsPathRooted(folder))
        {
            var dbDir = Path.GetDirectoryName(GetDbFilePath()) ?? ".";
            folder = Path.Combine(dbDir, folder);
        }
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Returns (dbSizeBytes, pcFoldersSizeBytes, totalFiles).</summary>
    public (long DbSize, long PcFoldersSize, int TotalFiles) GetBackupSizeInfo()
    {
        long dbSize = 0;
        var dbPath = GetDbFilePath();
        if (File.Exists(dbPath)) dbSize = new FileInfo(dbPath).Length;

        long pcSize = 0;
        int totalFiles = 1; // start with 1 for the DB file
        if (Directory.Exists(_basePath))
        {
            foreach (var f in Directory.GetFiles(_basePath, "*", SearchOption.AllDirectories))
            {
                // Skip _backups folder
                if (f.Contains(Path.Combine(_basePath, "_backups"))) continue;
                pcSize += new FileInfo(f).Length;
                totalFiles++;
            }
        }
        return (dbSize, pcSize, totalFiles);
    }

    /// <summary>
    /// Enumerate all PC-Folder files (excluding _backups). Returns (relativePath, fullPath) pairs.
    /// </summary>
    public IEnumerable<(string RelativePath, string FullPath)> EnumerateBackupFiles()
    {
        if (!Directory.Exists(_basePath)) yield break;
        var backupsDir = Path.Combine(_basePath, "_backups");
        foreach (var f in Directory.GetFiles(_basePath, "*", SearchOption.AllDirectories))
        {
            if (f.StartsWith(backupsDir, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = Path.GetRelativePath(_basePath, f).Replace('\\', '/');
            yield return ($"PC-Folders/{rel}", f);
        }
    }

    /// <summary>Decrypt a raw file from disk (for backup streaming).</summary>
    public byte[] DecryptFileForBackup(string fullPath)
    {
        var raw = File.ReadAllBytes(fullPath);
        return DecryptBytes(raw);
    }

    // ── Import helpers ────────────────────────────────────────

    public static readonly HashSet<string> ValidSections = new(StringComparer.OrdinalIgnoreCase)
        { "Front_Cover", "Back_Cover", "WorkSheets" };

    /// <summary>Check if a file already exists in a PC section folder.</summary>
    public bool SectionFileExists(int pcId, string section, string fileName)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return false;
        var path = Path.Combine(folder, section, fileName);
        return File.Exists(path);
    }

    /// <summary>Check if a worksheet with the given session name exists.</summary>
    public bool SessionFileExistsByName(int pcId, string sessionName, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var wsPath = Path.Combine(folder, "WorkSheets");
        if (!Directory.Exists(wsPath)) return false;
        foreach (var f in Directory.GetFiles(wsPath, "*.pdf"))
        {
            var name = Path.GetFileNameWithoutExtension(Path.GetFileName(f));
            if (name.Contains("_att_", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals(sessionName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Check if an attachment file already exists.</summary>
    public bool AttachmentFileExists(int pcId, string sessionFileName, string attFileName)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return false;
        var sessionNoExt = Path.GetFileNameWithoutExtension(sessionFileName);
        var flatName = $"{sessionNoExt}_att_{attFileName}";
        var path = Path.Combine(folder, "WorkSheets", flatName);
        return File.Exists(path);
    }

    /// <summary>Save an imported file to a PC section (Front_Cover, Back_Cover, WorkSheets). Keeps original name. Shrinks + encrypts.</summary>
    public void SaveSectionFile(int pcId, string section, string fileName, byte[] fileBytes)
    {
        if (!ValidSections.Contains(section)) return;

        var folder = GetPcFolder(pcId);
        if (folder == null) return;

        var sectionPath = Path.Combine(folder.FolderPath, section);
        Directory.CreateDirectory(sectionPath);

        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var fullPath = Path.Combine(sectionPath, safeName);

        // Skip if already exists
        if (File.Exists(fullPath)) return;

        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Saved section file '{fileName}' to {section} for PC {pcId}");
    }

    /// <summary>Overwrite an existing section file. Backs up the old file first.</summary>
    public void OverwriteSectionFile(int pcId, string section, string fileName, byte[] fileBytes)
    {
        if (!ValidSections.Contains(section)) return;

        var folder = GetPcFolder(pcId);
        if (folder == null) return;

        var sectionPath = Path.Combine(folder.FolderPath, section);
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var fullPath = Path.Combine(sectionPath, safeName);

        // Backup the existing file before overwriting
        if (File.Exists(fullPath))
            BackupFile(pcId, $"{section}/{safeName}");

        // Backup the original upload
        BackupBytes(pcId, safeName, fileBytes);

        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Overwrote section file '{fileName}' in {section} for PC {pcId}");
    }

    /// <summary>Save an imported attachment file. Flat naming in WorkSheets. Shrinks + encrypts.</summary>
    public void SaveImportedAttachment(int pcId, string sessionFileName, string attFileName, byte[] fileBytes)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return;

        var wsPath = Path.Combine(folder, "WorkSheets");
        Directory.CreateDirectory(wsPath);

        var sessionNoExt = Path.GetFileNameWithoutExtension(sessionFileName);
        var safeName = string.Join("_", attFileName.Split(Path.GetInvalidFileNameChars()));
        var flatName = $"{sessionNoExt}_att_{safeName}";
        var fullPath = Path.Combine(wsPath, flatName);

        if (File.Exists(fullPath)) return;

        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Saved imported attachment '{attFileName}' for session '{sessionFileName}', PC {pcId}");
    }

    // ── Solo PC folder helpers ─────────────────────────────────────

    /// <summary>Returns the display folder name for a Solo PC: "{pcId}-{name} Solo"</summary>
    public string GetSoloFolderName(int pcId)
    {
        var name = GetPcName(pcId) ?? $"PC {pcId}";
        return $"{pcId}-{name.Trim()} Solo";
    }

    /// <summary>Return PcFolderInfo for a PC folder. Regular folder is created if missing; solo folder is not.</summary>
    public PcFolderInfo? GetFolderInfo(int pcId, bool solo = false)
    {
        var pcName = GetPcName(pcId) ?? $"PC {pcId}";
        string? folder;
        if (solo)
        {
            folder = FindSoloPcFolder(pcId);
            if (folder == null) return null;
        }
        else
        {
            folder = FindPcFolder(pcId);
            if (folder == null)
            {
                folder = Path.Combine(_basePath, $"{pcId}-{pcName}");
                Directory.CreateDirectory(folder);
                Directory.CreateDirectory(Path.Combine(folder, "Front_Cover"));
                Directory.CreateDirectory(Path.Combine(folder, "Back_Cover"));
                Directory.CreateDirectory(Path.Combine(folder, "WorkSheets"));
            }
        }

        var frontCover  = GetFilesForSection(folder, "Front_Cover");
        var backCover   = GetFilesForSection(folder, "Back_Cover");
        var workSheets  = GetWorkSheets(folder);
        var frontTree   = BuildSectionTree(folder, "Front_Cover");
        var backTree    = BuildSectionTree(folder, "Back_Cover");
        var displayName = solo ? $"{pcName} [Solo]" : pcName;

        return new PcFolderInfo(pcId, displayName, folder, frontCover, backCover, workSheets, frontTree, backTree);
    }

    /// <summary>Find the PC's Solo folder on disk (starts with "{pcId}-", ends with " Solo").</summary>
    public string? FindSoloPcFolder(int pcId)
    {
        if (!Directory.Exists(_basePath)) return null;
        var prefix = $"{pcId}-";
        return Directory.GetDirectories(_basePath)
            .FirstOrDefault(d =>
            {
                var n = Path.GetFileName(d);
                return n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && n.EndsWith(" Solo", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>Get or create the Solo folder path: "{basePath}/{pcId}-{name} Solo"</summary>
    public string GetOrCreateSoloPcFolderPath(int pcId)
    {
        var existing = FindSoloPcFolder(pcId);
        if (existing != null) return existing;
        var path = Path.Combine(_basePath, GetSoloFolderName(pcId));
        Directory.CreateDirectory(path);
        return path;
    }

    public bool SoloSectionFileExists(int pcId, string section, string fileName)
    {
        var folder = FindSoloPcFolder(pcId);
        if (folder == null) return false;
        return File.Exists(Path.Combine(folder, section, fileName));
    }

    public void SaveSoloSectionFile(int pcId, string section, string fileName, byte[] fileBytes)
    {
        if (!ValidSections.Contains(section)) return;
        var folderPath = GetOrCreateSoloPcFolderPath(pcId);
        var sectionPath = Path.Combine(folderPath, section);
        Directory.CreateDirectory(sectionPath);
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var fullPath = Path.Combine(sectionPath, safeName);
        if (File.Exists(fullPath)) return;
        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Saved solo section file '{fileName}' to {section} for PC {pcId}");
    }

    public void OverwriteSoloSectionFile(int pcId, string section, string fileName, byte[] fileBytes)
    {
        if (!ValidSections.Contains(section)) return;
        var folderPath = GetOrCreateSoloPcFolderPath(pcId);
        var sectionPath = Path.Combine(folderPath, section);
        Directory.CreateDirectory(sectionPath);
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var fullPath = Path.Combine(sectionPath, safeName);
        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Overwrote solo section file '{fileName}' in {section} for PC {pcId}");
    }

    public bool SoloAttachmentFileExists(int pcId, string sessionFileName, string attFileName)
    {
        var folder = FindSoloPcFolder(pcId);
        if (folder == null) return false;
        var sessionNoExt = Path.GetFileNameWithoutExtension(sessionFileName);
        var flatName = $"{sessionNoExt}_att_{attFileName}";
        return File.Exists(Path.Combine(folder, "WorkSheets", flatName));
    }

    public void SaveSoloImportedAttachment(int pcId, string sessionFileName, string attFileName, byte[] fileBytes)
    {
        var folderPath = GetOrCreateSoloPcFolderPath(pcId);
        var wsPath = Path.Combine(folderPath, "WorkSheets");
        Directory.CreateDirectory(wsPath);
        var sessionNoExt = Path.GetFileNameWithoutExtension(sessionFileName);
        var safeName = string.Join("_", attFileName.Split(Path.GetInvalidFileNameChars()));
        var flatName = $"{sessionNoExt}_att_{safeName}";
        var fullPath = Path.Combine(wsPath, flatName);
        if (File.Exists(fullPath)) return;
        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Saved solo imported attachment '{attFileName}' for session '{sessionFileName}', PC {pcId}");
    }

    /// <summary>Save an uploaded session file to WorkSheets. Returns the saved filename.</summary>
    public string? SaveUploadedFile(int pcId, string fileName, byte[] fileBytes)
    {
        var folder = GetPcFolder(pcId);
        if (folder == null) return null;

        var wsPath = Path.Combine(folder.FolderPath, "WorkSheets");
        Directory.CreateDirectory(wsPath);

        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var finalName = safeName;
        var fullPath = Path.Combine(wsPath, finalName);

        // Backup the original upload before shrink+encrypt
        BackupBytes(pcId, finalName, fileBytes);

        // Write plaintext first so Ghostscript can shrink it
        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        // Now encrypt the (possibly shrunk) file in-place
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Saved uploaded file '{fileName}' for PC {pcId}");
        return Path.GetFileName(fullPath);
    }

    /// <summary>Save an uploaded session file to the solo folder WorkSheets. Returns the saved filename.</summary>
    public string? SaveSoloUploadedFile(int pcId, string fileName, byte[] fileBytes)
    {
        var folderPath = GetOrCreateSoloPcFolderPath(pcId);
        var wsPath = Path.Combine(folderPath, "WorkSheets");
        Directory.CreateDirectory(wsPath);

        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var fullPath = Path.Combine(wsPath, safeName);

        var counter = 2;
        var nameNoExt = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        while (File.Exists(fullPath))
        {
            fullPath = Path.Combine(wsPath, $"{nameNoExt}({counter}){ext}");
            counter++;
        }

        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Saved solo uploaded file '{fileName}' for PC {pcId}");
        return Path.GetFileName(fullPath);
    }

    /// <summary>Save an attachment file as flat file in WorkSheets: {session}_att_{name}.</summary>
    public void SaveAttachment(int pcId, string sessionFileName, string attFileName, byte[] fileBytes, bool solo = false)
    {
        var folder = solo ? GetOrCreateSoloPcFolderPath(pcId) : FindPcFolder(pcId);
        if (folder == null) return;

        var wsPath = Path.Combine(folder, "WorkSheets");
        Directory.CreateDirectory(wsPath);

        var sessionNoExt = Path.GetFileNameWithoutExtension(sessionFileName);
        var safeName = string.Join("_", attFileName.Split(Path.GetInvalidFileNameChars()));
        var flatName = $"{sessionNoExt}_att_{safeName}";
        var fullPath = Path.Combine(wsPath, flatName);

        var counter = 2;
        var nameNoExt = Path.GetFileNameWithoutExtension(flatName);
        var ext = Path.GetExtension(flatName);
        while (File.Exists(fullPath))
        {
            fullPath = Path.Combine(wsPath, $"{nameNoExt}({counter}){ext}");
            counter++;
        }

        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Saved attachment '{attFileName}' for session '{sessionFileName}', PC {pcId}");
    }

    /// <summary>
    /// Returns which special attachment types exist for a session.
    /// Possible values: "PinkSheet", "Instruct", "Cramming".
    /// </summary>
    public HashSet<string> GetSessionAttachmentTypes(int pcId, string sessionFileName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folder = FindPcFolder(pcId);
        if (folder == null || string.IsNullOrEmpty(sessionFileName)) return result;

        var wsPath = Path.Combine(folder, "WorkSheets");
        if (!Directory.Exists(wsPath)) return result;

        var prefix = Path.GetFileNameWithoutExtension(sessionFileName) + "_att_";
        foreach (var file in Directory.EnumerateFiles(wsPath, prefix + "*.pdf"))
        {
            var lower = Path.GetFileName(file).ToLowerInvariant();
            if      (lower.Contains("pinksheet")) result.Add("PinkSheet");
            else if (lower.Contains("instruct"))  result.Add("Instruct");
            else if (lower.Contains("cramming"))  result.Add("Cramming");
        }
        return result;
    }

    /// <summary>Overwrites the existing arf.pdf attachment for a session. Returns false if not found.</summary>
    public bool TryOverwriteArfPdf(int pcId, string sessionFileName, byte[] pdfBytes, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var sessionNoExt = Path.GetFileNameWithoutExtension(sessionFileName);
        var fullPath = Path.Combine(folder, "WorkSheets", $"{sessionNoExt}_att_arf.pdf");
        if (!File.Exists(fullPath)) return false;
        File.WriteAllBytes(fullPath, pdfBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Overwrote arf.pdf for session '{sessionFileName}', PC {pcId}");
        return true;
    }

    /// <summary>Read a file from disk, encrypt its contents, and write back.</summary>
    private void EncryptFileInPlace(string path)
    {
        if (_encKey == null || !File.Exists(path)) return;
        var plain = File.ReadAllBytes(path);
        File.WriteAllBytes(path, EncryptBytes(plain));
    }

    /// <summary>Shrink a PDF in-place using Ghostscript. Keeps the original if shrinking fails or produces a larger file.</summary>
    private void TryShrinkPdf(string pdfPath)
    {
        if (string.IsNullOrEmpty(_ghostscriptExe) || !File.Exists(_ghostscriptExe)) return;
        if (!File.Exists(pdfPath)) return;

        var originalSize = new FileInfo(pdfPath).Length;
        var tempOutput = pdfPath + ".shrunk";

        try
        {
            var args = string.Join(' ', new[]
            {
                "-sDEVICE=pdfwrite",
                "-dCompatibilityLevel=1.4",
                "-dNOPAUSE",
                "-dQUIET",
                "-dBATCH",
                "-dDetectDuplicateImages=true",
                "-dCompressFonts=true",
                "-dDownsampleColorImages=true",
                "-dDownsampleGrayImages=true",
                "-dDownsampleMonoImages=true",
                "-dColorImageResolution=150",
                "-dGrayImageResolution=150",
                "-dMonoImageResolution=300",
                "-dAutoFilterColorImages=false",
                "-dAutoFilterGrayImages=false",
                "-dColorImageFilter=/DCTEncode",
                "-dGrayImageFilter=/DCTEncode",
                "-dJPEGQ=60",
                $"-sOutputFile=\"{tempOutput}\"",
                $"\"{pdfPath}\""
            });

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _ghostscriptExe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process.Start();
            process.WaitForExit(30_000);

            if (process.ExitCode == 0 && File.Exists(tempOutput))
            {
                var shrunkSize = new FileInfo(tempOutput).Length;
                if (shrunkSize > 0 && shrunkSize < originalSize)
                {
                    File.Delete(pdfPath);
                    File.Move(tempOutput, pdfPath);
                    Console.WriteLine($"[PDF Shrink] {Path.GetFileName(pdfPath)}: {originalSize / 1024}KB → {shrunkSize / 1024}KB ({100 - shrunkSize * 100 / originalSize}% smaller)");
                }
                else
                {
                    File.Delete(tempOutput);
                }
            }
            else
            {
                if (File.Exists(tempOutput)) File.Delete(tempOutput);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PDF Shrink] Error: {ex.Message}");
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    // ── AES-256-CBC encryption ──────────────────────────────────
    // File format: [16-byte IV][encrypted data]
    // Legacy unencrypted files (starting with %PDF) are returned as-is.

    private byte[] EncryptBytes(byte[] plain)
    {
        if (_encKey == null) return plain;
        using var aes = Aes.Create();
        aes.Key = _encKey;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
        var result = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
        return result;
    }

    private byte[] DecryptBytes(byte[] raw)
    {
        if (_encKey == null) return raw;
        // Detect unencrypted legacy files — PDF starts with %PDF, ZIP/XLSX starts with PK
        if (raw.Length >= 4 && raw[0] == '%' && raw[1] == 'P' && raw[2] == 'D' && raw[3] == 'F')
            return raw;
        if (raw.Length >= 4 && raw[0] == 'P' && raw[1] == 'K')
            return raw;
        if (raw.Length < 17) return raw; // too small to be encrypted (16 IV + at least 1 block)
        using var aes = Aes.Create();
        aes.Key = _encKey;
        var iv = new byte[16];
        Buffer.BlockCopy(raw, 0, iv, 0, 16);
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(raw, 16, raw.Length - 16);
    }

    /// <summary>Resolve relative path safely — prevents traversal and absolute path injection</summary>
    // ── Tree & File Management ──────────────────────────────

    private FolderTreeNode BuildSectionTree(string pcFolder, string section)
    {
        var sectionPath = Path.Combine(pcFolder, section);
        if (!Directory.Exists(sectionPath))
            return new FolderTreeNode(section, section, true, null, []);
        return BuildTreeRecursive(pcFolder, sectionPath, section);
    }

    private FolderTreeNode BuildTreeRecursive(string pcFolder, string dirPath, string sectionName)
    {
        var relativePath = Path.GetRelativePath(pcFolder, dirPath).Replace('\\', '/');
        var name = Path.GetFileName(dirPath);
        var children = new List<FolderTreeNode>();

        // Subfolders first (sorted alphabetically)
        foreach (var subDir in Directory.GetDirectories(dirPath).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
        {
            children.Add(BuildTreeRecursive(pcFolder, subDir, sectionName));
        }

        // Files (sorted alphabetically) — include both .pdf and .xlsx
        var sectionFiles = new[] { "*.pdf", "*.xlsx" }
            .SelectMany(ext => Directory.GetFiles(dirPath, ext))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
        foreach (var file in sectionFiles)
        {
            var fileName = Path.GetFileName(file);
            var fileRelPath = Path.GetRelativePath(pcFolder, file).Replace('\\', '/');
            var dateParsed = TryParseDatePrefix(fileName);
            children.Add(new FolderTreeNode(fileName, fileRelPath, false, dateParsed, []));
        }

        return new FolderTreeNode(name, relativePath, true, null, children);
    }

    public bool CreateSubfolder(int pcId, string parentRelativePath, string folderName, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var sanitized = SanitizeName(folderName);
        if (string.IsNullOrWhiteSpace(sanitized)) return false;
        var parentPath = SafeResolvePath(folder, parentRelativePath);
        if (parentPath == null || !Directory.Exists(parentPath)) return false;
        var newPath = Path.Combine(parentPath, sanitized);
        if (Directory.Exists(newPath)) return false;
        Directory.CreateDirectory(newPath);
        Console.WriteLine($"[FolderService] Created subfolder '{sanitized}' in {parentRelativePath} for PC {pcId}");
        return true;
    }

    public bool MoveFile(int pcId, string sourceRelativePath, string destFolderRelativePath, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var srcPath = SafeResolvePath(folder, sourceRelativePath);
        if (srcPath == null || !File.Exists(srcPath)) return false;
        var destDir = SafeResolvePath(folder, destFolderRelativePath);
        if (destDir == null || !Directory.Exists(destDir)) return false;
        var fileName = Path.GetFileName(srcPath);
        var destPath = Path.Combine(destDir, fileName);
        if (File.Exists(destPath)) return false; // no silent overwrite
        File.Move(srcPath, destPath);
        Console.WriteLine($"[FolderService] Moved '{sourceRelativePath}' → '{destFolderRelativePath}/{fileName}' for PC {pcId}");
        return true;
    }

    public bool RenameFile(int pcId, string relativePath, string newName, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var sanitized = SanitizeName(newName);
        if (string.IsNullOrWhiteSpace(sanitized)) return false;
        if (!sanitized.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            sanitized += ".pdf";
        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return false;
        var dir = Path.GetDirectoryName(fullPath)!;
        var destPath = Path.Combine(dir, sanitized);
        if (File.Exists(destPath)) return false;
        File.Move(fullPath, destPath);
        Console.WriteLine($"[FolderService] Renamed '{relativePath}' → '{sanitized}' for PC {pcId}");
        return true;
    }

    public bool RenameFolder(int pcId, string relativePath, string newName, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var sanitized = SanitizeName(newName);
        if (string.IsNullOrWhiteSpace(sanitized)) return false;
        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !Directory.Exists(fullPath)) return false;
        var parentDir = Path.GetDirectoryName(fullPath)!;
        var destPath = Path.Combine(parentDir, sanitized);
        if (Directory.Exists(destPath)) return false;
        Directory.Move(fullPath, destPath);
        Console.WriteLine($"[FolderService] Renamed folder '{relativePath}' → '{sanitized}' for PC {pcId}");
        return true;
    }

    public bool DeleteFileToBackup(int pcId, string relativePath, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return false;
        BackupFile(pcId, relativePath, solo);
        File.Delete(fullPath);
        Console.WriteLine($"[FolderService] Deleted (backed up) '{relativePath}' for PC {pcId}");
        return true;
    }

    public bool DeleteEmptyFolder(int pcId, string relativePath, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !Directory.Exists(fullPath)) return false;
        if (Directory.GetFileSystemEntries(fullPath).Length > 0) return false; // not empty
        Directory.Delete(fullPath);
        Console.WriteLine($"[FolderService] Deleted empty folder '{relativePath}' for PC {pcId}");
        return true;
    }

    public byte[] CreatePinkPdf()
    {
        using var ms = new MemoryStream();
        var doc = new PdfSharpCore.Pdf.PdfDocument();
        var page = doc.AddPage();
        var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(new PdfSharpCore.Drawing.XSolidBrush(
            PdfSharpCore.Drawing.XColor.FromArgb(252, 231, 243)), // #fce7f3 pink
            0, 0, page.Width, page.Height);
        gfx.Dispose();
        doc.Save(ms);
        return ms.ToArray();
    }

    public bool RenameAttachment(int pcId, string relativePath, string newSuffix, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return false;
        var fileName = Path.GetFileName(fullPath);
        // Find the _att_ prefix
        var attIdx = fileName.IndexOf("_att_", StringComparison.OrdinalIgnoreCase);
        if (attIdx < 0) return false; // not an attachment
        var prefix = fileName[..(attIdx + 5)]; // includes "_att_"
        var sanitized = SanitizeName(newSuffix);
        if (string.IsNullOrWhiteSpace(sanitized)) return false;
        if (!sanitized.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            sanitized += ".pdf";
        var newName = prefix + sanitized;
        var dir = Path.GetDirectoryName(fullPath)!;
        var destPath = Path.Combine(dir, newName);
        if (File.Exists(destPath)) return false;
        File.Move(fullPath, destPath);
        Console.WriteLine($"[FolderService] Renamed attachment '{fileName}' → '{newName}' for PC {pcId}");
        return true;
    }

    public byte[] CreateEmptyPdf()
    {
        using var ms = new System.IO.MemoryStream();
        var doc = new PdfSharpCore.Pdf.PdfDocument();
        doc.AddPage(); // blank A4 page
        doc.Save(ms);
        return ms.ToArray();
    }

    public bool SectionFileExistsAtPath(int pcId, string relativeFolder, string fileName, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var targetDir = SafeResolvePath(folder, relativeFolder);
        if (targetDir == null) return false;
        var safeName = SanitizeName(fileName);
        if (!safeName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            safeName += ".pdf";
        return File.Exists(Path.Combine(targetDir, safeName));
    }

    public bool SaveSectionFileToPath(int pcId, string relativeFolder, string fileName, byte[] fileBytes, bool overwrite = false, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var targetDir = SafeResolvePath(folder, relativeFolder);
        if (targetDir == null) return false;
        Directory.CreateDirectory(targetDir);
        var safeName = SanitizeName(fileName);
        if (!safeName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            safeName += ".pdf";
        var fullPath = Path.Combine(targetDir, safeName);
        if (File.Exists(fullPath))
        {
            if (!overwrite) return false;
            var relPath = Path.GetRelativePath(folder, fullPath).Replace('\\', '/');
            BackupFile(pcId, relPath, solo);
            File.Delete(fullPath);
        }
        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        EncryptFileInPlace(fullPath);
        Console.WriteLine($"[FolderService] Saved '{safeName}' to {relativeFolder} for PC {pcId}{(overwrite ? " (overwrite)" : "")}");
        return true;
    }

    // ── Program Insert Templates ──────────────────────────────

    private static readonly string ProgramInsertsDir = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "ProgramInserts");

    public List<string> GetProgramInsertFiles()
    {
        if (!Directory.Exists(ProgramInsertsDir)) return new();
        return Directory.GetFiles(ProgramInsertsDir, "*.pdf")
            .Select(f => Path.GetFileName(f))
            .OrderBy(f => f)
            .ToList();
    }

    public byte[]? GetProgramInsertFileBytes(string fileName)
    {
        var safe = SanitizeName(fileName);
        if (string.IsNullOrWhiteSpace(safe)) return null;
        var fullPath = Path.Combine(ProgramInsertsDir, safe);
        if (!fullPath.StartsWith(ProgramInsertsDir, StringComparison.OrdinalIgnoreCase)) return null;
        if (!File.Exists(fullPath)) return null;
        return File.ReadAllBytes(fullPath);
    }

    public static void EnsureDummyProgramInserts()
    {
        if (!Directory.Exists(ProgramInsertsDir))
            Directory.CreateDirectory(ProgramInsertsDir);
        if (Directory.GetFiles(ProgramInsertsDir, "*.pdf").Length > 0) return;

        var names = new[] {
            "Intake Form", "Treatment Plan", "Progress Notes",
            "Assessment Report", "Discharge Summary", "Consent Form",
            "Referral Letter", "Session Log"
        };
        foreach (var name in names)
        {
            var path = Path.Combine(ProgramInsertsDir, name + ".pdf");
            using var doc = new PdfSharpCore.Pdf.PdfDocument();
            for (int p = 1; p <= 3; p++)
            {
                var page = doc.AddPage();
                var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
                var font = new PdfSharpCore.Drawing.XFont("Arial", 24);
                var smallFont = new PdfSharpCore.Drawing.XFont("Arial", 14);
                gfx.DrawString(name, font, PdfSharpCore.Drawing.XBrushes.DarkBlue,
                    new PdfSharpCore.Drawing.XRect(0, 80, page.Width, 40),
                    PdfSharpCore.Drawing.XStringFormats.Center);
                gfx.DrawString($"Page {p} of 3", smallFont, PdfSharpCore.Drawing.XBrushes.Gray,
                    new PdfSharpCore.Drawing.XRect(0, 130, page.Width, 30),
                    PdfSharpCore.Drawing.XStringFormats.Center);
                gfx.DrawString("[ Template placeholder content ]", smallFont, PdfSharpCore.Drawing.XBrushes.LightGray,
                    new PdfSharpCore.Drawing.XRect(0, 300, page.Width, 30),
                    PdfSharpCore.Drawing.XStringFormats.Center);
                gfx.Dispose();
            }
            doc.Save(path);
        }
        Console.WriteLine($"[FolderService] Generated {names.Length} dummy program insert PDFs in {ProgramInsertsDir}");
    }

    // ── TAA Action Excel ──────────────────────────────

    private const string TaaFileName = "TAA Action.xlsx";
    private static readonly string TaaTemplatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "TAA_Action_Template.xlsx");

    /// <summary>
    /// Ensures the TAA Action.xlsx exists in the PC's Front_Cover folder.
    /// If not, copies from template and sets the PC name in B3.
    /// Returns the full path to the file, or null on failure.
    /// </summary>
    public string? EnsureTaaFile(int pcId)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return null;
        var frontCover = Path.Combine(folder, "Front_Cover");
        Directory.CreateDirectory(frontCover);
        var taaPath = Path.Combine(frontCover, TaaFileName);

        if (!File.Exists(taaPath))
        {
            if (!File.Exists(TaaTemplatePath))
            {
                Console.WriteLine($"[FolderService] TAA template not found at {TaaTemplatePath}");
                return null;
            }
            File.Copy(TaaTemplatePath, taaPath);

            // Set PC name in B3
            var pcName = GetPcName(pcId) ?? $"PC {pcId}";
            try
            {
                using var wb = new ClosedXML.Excel.XLWorkbook(taaPath);
                var ws = wb.Worksheets.First();
                ws.Cell("B3").Value = pcName;
                wb.Save();
                Console.WriteLine($"[FolderService] Created TAA Action file for PC {pcId} ({pcName})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FolderService] Error setting PC name in TAA file: {ex.Message}");
            }
        }

        return taaPath;
    }

    /// <summary>
    /// Appends a row to the TAA Action.xlsx for the given PC.
    /// date: session date string (dd.M.yy), minutes: session length in minutes (no admin), totalTa: Total TA value.
    /// </summary>
    public void AppendTaaRow(int pcId, string dateStr, int minutes, string totalTa)
    {
        var taaPath = EnsureTaaFile(pcId);
        if (taaPath == null) return;

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(taaPath);
            var ws = wb.Worksheets.First();

            // Find first empty row starting from row 7
            int newRow = 7;
            while (ws.Cell(newRow, 1).GetString() != "") newRow++;

            // A = Date
            ws.Cell(newRow, 1).Value = dateStr;

            // B = Minutes in session — 0 or negative treated as 0
            ws.Cell(newRow, 2).Value = minutes > 0 ? minutes : 0;

            // C = TAA (Total TA) — blank treated as 0
            if (string.IsNullOrWhiteSpace(totalTa))
                ws.Cell(newRow, 3).Value = 0;
            else if (double.TryParse(totalTa, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var taVal))
                ws.Cell(newRow, 3).Value = taVal;
            else
                ws.Cell(newRow, 3).Value = 0;

            // D = TAA/Hour formula: =IFERROR(C{n}*60/B{n},0)
            ws.Cell(newRow, 4).FormulaA1 = $"IFERROR(C{newRow}*60/B{newRow},0)";

            // E = Time on Grade formula: =TEXT(SUM($B$7:B{n})/1440, "[h]:mm")
            ws.Cell(newRow, 5).FormulaA1 = $"TEXT(SUM($B$7:B{newRow})/1440, \"[h]:mm\")";

            // F = Remark (empty)

            wb.Save();
            Console.WriteLine($"[FolderService] Appended TAA row for PC {pcId}: date={dateStr}, min={minutes}, ta={totalTa} (row {newRow})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderService] Error appending TAA row for PC {pcId}: {ex.Message}");
        }
    }

    // ── Excel read/write ──────────────────────────────

    public record ExcelCellData(int Row, int Col, string Value, bool IsFormula, bool IsHeader);

    public record ExcelSheetData(
        int RowCount, int ColCount,
        List<string> ColumnHeaders,      // row 6 headers
        string PcName,                   // B3
        string Action,                   // B4
        string TotalTimeOnGrade,         // B5 (formula result)
        List<ExcelCellData> Cells        // all cells
    );

    public ExcelSheetData? ReadExcelFile(int pcId, string relativePath, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return null;
        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return null;

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(fullPath);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 6;
            var lastCol = Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 6, 6);

            var headers = new List<string>();
            for (int c = 1; c <= lastCol; c++)
                headers.Add(ws.Cell(6, c).GetString());

            var pcName = ws.Cell("B3").GetString();
            var action = ws.Cell("B4").GetString();
            var totalTime = ws.Cell("B5").GetString();

            var cells = new List<ExcelCellData>();
            for (int r = 1; r <= lastRow; r++)
            {
                for (int c = 1; c <= lastCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    var val = cell.HasFormula ? cell.CachedValue.ToString() : cell.GetString();
                    cells.Add(new ExcelCellData(r, c, val, cell.HasFormula, r <= 6));
                }
            }

            return new ExcelSheetData(lastRow, lastCol, headers, pcName, action, totalTime, cells);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderService] Error reading Excel file: {ex.Message}");
            return null;
        }
    }

    public record ExcelCellUpdate(int Row, int Col, string Value);

    public bool SaveExcelChanges(int pcId, string relativePath, List<ExcelCellUpdate> updates, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return false;

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(fullPath);
            var ws = wb.Worksheets.First();

            foreach (var u in updates)
            {
                // Allow editing B4 (Action field); skip other header rows
                if (u.Row < 7 && !(u.Row == 4 && u.Col == 2)) continue;

                switch (u.Col)
                {
                    case 1: // A = Date (string)
                        ws.Cell(u.Row, u.Col).Value = u.Value;
                        break;
                    case 2: // B = Minutes (number)
                        if (double.TryParse(u.Value, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var numB))
                            ws.Cell(u.Row, u.Col).Value = numB;
                        else
                            ws.Cell(u.Row, u.Col).Value = u.Value;
                        break;
                    case 3: // C = TAA (number)
                        if (double.TryParse(u.Value, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var numC))
                            ws.Cell(u.Row, u.Col).Value = numC;
                        else
                            ws.Cell(u.Row, u.Col).Value = u.Value;
                        break;
                    case 4: // D = formula (recalc)
                        ws.Cell(u.Row, u.Col).FormulaA1 = $"IFERROR(C{u.Row}*60/B{u.Row},0)";
                        break;
                    case 5: // E = formula (recalc)
                        ws.Cell(u.Row, u.Col).FormulaA1 = $"TEXT(SUM($B$7:B{u.Row})/1440, \"[h]:mm\")";
                        break;
                    case 6: // F = Remark (string)
                        ws.Cell(u.Row, u.Col).Value = u.Value;
                        break;
                    default:
                        ws.Cell(u.Row, u.Col).Value = u.Value;
                        break;
                }
            }

            wb.Save();
            Console.WriteLine($"[FolderService] Saved {updates.Count} Excel changes to '{relativePath}' for PC {pcId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderService] Error saving Excel changes: {ex.Message}");
            return false;
        }
    }

    public bool AddExcelRow(int pcId, string relativePath, bool solo = false)
    {
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return false;

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(fullPath);
            var ws = wb.Worksheets.First();
            int newRow = 7;
            while (ws.Cell(newRow, 1).GetString() != "") newRow++;
            // Just create the formulas — user fills A, B, C, F
            ws.Cell(newRow, 4).FormulaA1 = $"IFERROR(C{newRow}*60/B{newRow},0)";
            ws.Cell(newRow, 5).FormulaA1 = $"TEXT(SUM($B$7:B{newRow})/1440, \"[h]:mm\")";
            wb.Save();
            Console.WriteLine($"[FolderService] Added empty Excel row {newRow} in '{relativePath}' for PC {pcId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderService] Error adding Excel row: {ex.Message}");
            return false;
        }
    }

    public bool DeleteExcelRow(int pcId, string relativePath, int row, bool solo = false)
    {
        if (row < 7) return false; // can't delete header rows
        var folder = solo ? FindSoloPcFolder(pcId) : FindPcFolder(pcId);
        if (folder == null) return false;
        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return false;

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(fullPath);
            var ws = wb.Worksheets.First();
            ws.Row(row).Delete();
            // Recalculate formulas for remaining rows
            int r = 7;
            while (ws.Cell(r, 1).GetString() != "" || ws.Cell(r, 2).GetString() != "")
            {
                ws.Cell(r, 4).FormulaA1 = $"IFERROR(C{r}*60/B{r},0)";
                ws.Cell(r, 5).FormulaA1 = $"TEXT(SUM($B$7:B{r})/1440, \"[h]:mm\")";
                r++;
            }
            wb.Save();
            Console.WriteLine($"[FolderService] Deleted Excel row {row} in '{relativePath}' for PC {pcId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderService] Error deleting Excel row: {ex.Message}");
            return false;
        }
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid)).Trim();
    }

    private static string? SafeResolvePath(string baseFolder, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Contains("..") || Path.IsPathRooted(normalized)) return null;
        var fullPath = Path.GetFullPath(Path.Combine(baseFolder, normalized));
        var baseFull = Path.GetFullPath(baseFolder);
        if (!fullPath.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)) return null;
        return fullPath;
    }
}
