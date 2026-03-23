namespace LPM.Services;

public enum ImportStatus { Uploading, Processing, Complete, Failed }

public record ImportFileManifest(
    string RelativePath, string FileName, string Section,
    string? ParentSessionFile, string? LastModified, bool OverrideExisting = false,
    string SubPath = "");

public record ImportPcManifest(
    string FolderName, string PcName, int? ExistingPcId,
    List<ImportFileManifest> Files, bool IsSolo = false);

public record ImportCoverMapping(string FileName, string Section, string PcFolderName, int AssignedItemId);

public record SkippedFileRecord(string PcName, string FileName, string Section, string Reason);

public class ImportJobState
{
    public string JobId { get; init; } = "";
    public int UserId { get; init; }
    public ImportStatus Status { get; set; }
    public int TotalFiles { get; set; }
    public int UploadedFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int SkippedCount { get; set; }
    public int NewPcsCreated { get; set; }
    public int SessionsCreated { get; set; }
    public string CurrentFileName { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public List<string> UnmatchedSoloPcs { get; } = new();
    public List<SkippedFileRecord> SkippedList { get; } = new();
    public DateTime StartedAt { get; init; }
    public DateTime LastActivity { get; set; }
}

public record AdjustResultRow(
    int SessionId, int PcId, string PcName,
    string Part,   // "Auditor" | "CS (non-solo)" | "CS (solo)"
    string Result  // "Set → User X" | "No staff match" | "No review row"
);

public record AdjustResult(
    // Part 1 — auditors
    int P1Found, int P1Set, int P1NoMatch,
    // Part 2 — CS for non-solo imported sessions
    int P2Found, int P2ReviewsFound, int P2Set, int P2NoMatch,
    // Part 3 — CS for solo imported sessions
    int P3Found, int P3ReviewsFound, int P3Set, int P3NoMatch,
    List<AdjustResultRow> Rows
);

public class ImportJobService
{
    private readonly FolderService _folderSvc;
    private readonly PcService _pcSvc;
    private readonly DashboardService _dashSvc;
    private readonly LPM.Auth.UserDb _userDb;
    private readonly string _tempBasePath;
    private readonly string _dbPath;
    private readonly string _dbBackupFolder;
    private readonly object _lock = new();

    public ImportJobState? CurrentJob { get; private set; }
    public event Action? OnProgressChanged;

    private readonly SmsService _smsSvc;

    public ImportJobService(FolderService folderSvc, PcService pcSvc, DashboardService dashSvc,
        LPM.Auth.UserDb userDb, SmsService smsSvc, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _folderSvc = folderSvc;
        _pcSvc = pcSvc;
        _dashSvc = dashSvc;
        _userDb = userDb;
        _smsSvc = smsSvc;
        _tempBasePath = Path.Combine(Directory.GetCurrentDirectory(), "PC-Folders", "_import_temp");
        _dbPath = config["Database:Path"] ?? "lifepower.db";
        _dbBackupFolder = config["Database:BackupFolder"] ?? "db-backups";
    }

    /// <summary>Snapshot the live DB to db-backups/ before any import writes.</summary>
    private void BackupDbBeforeImport()
    {
        try
        {
            Directory.CreateDirectory(_dbBackupFolder);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var destPath = Path.Combine(_dbBackupFolder, $"lifepower-{timestamp}-BeforeImport.db");
            using var src = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
            using var dst = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={destPath}");
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
            Console.WriteLine($"[ImportJobService] DB backed up to '{destPath}' before import");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImportJobService] DB backup before import FAILED: {ex.Message}");
            // Non-fatal — import proceeds but operator is warned in the log
        }
    }

    /// <summary>Start a new upload job. Returns jobId or null if a job is already running.</summary>
    public string? TryStartUpload(int userId, int totalFiles)
    {
        lock (_lock)
        {
            // Auto-clear stale uploading jobs (>30 min inactive)
            if (CurrentJob != null && CurrentJob.Status == ImportStatus.Uploading
                && (DateTime.UtcNow - CurrentJob.LastActivity).TotalMinutes > 30)
            {
                CleanupTempFolder(CurrentJob.JobId);
                CurrentJob = null;
            }

            if (CurrentJob != null && CurrentJob.Status is ImportStatus.Uploading or ImportStatus.Processing)
                return null;

            var jobId = Guid.NewGuid().ToString("N")[..8];
            CurrentJob = new ImportJobState
            {
                JobId = jobId,
                UserId = userId,
                Status = ImportStatus.Uploading,
                TotalFiles = totalFiles,
                StartedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow
            };

            var jobPath = Path.Combine(_tempBasePath, jobId);
            Directory.CreateDirectory(jobPath);

            Console.WriteLine($"[ImportJobService] Started upload job {jobId}, user {userId}, totalFiles: {totalFiles}");
            return jobId;
        }
    }

    /// <summary>Save a single uploaded file to the temp folder.</summary>
    public void RecordFileUploaded(string jobId, string pcFolderName, string section,
        string fileName, byte[] bytes, string? parentSessionFile, string subPath = "")
    {
        if (CurrentJob == null || CurrentJob.JobId != jobId) return;

        string targetDir;
        if (parentSessionFile != null)
        {
            // Attachments — unchanged, _att convention
            var sessionNoExt = Path.GetFileNameWithoutExtension(parentSessionFile);
            targetDir = Path.Combine(_tempBasePath, jobId, pcFolderName, section, $"{sessionNoExt}_att");
        }
        else if (!string.IsNullOrEmpty(subPath))
        {
            targetDir = Path.Combine(_tempBasePath, jobId, pcFolderName, section, subPath);
        }
        else
        {
            targetDir = Path.Combine(_tempBasePath, jobId, pcFolderName, section);
        }

        Directory.CreateDirectory(targetDir);
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        File.WriteAllBytes(Path.Combine(targetDir, safeName), bytes);

        CurrentJob.UploadedFiles++;
        CurrentJob.LastActivity = DateTime.UtcNow;
        OnProgressChanged?.Invoke();
    }

    /// <summary>Start background processing after all files are uploaded.</summary>
    public void StartProcessing(string jobId, List<ImportPcManifest> manifest,
        List<ImportCoverMapping> coverMappings, bool overrideCoverFolders = false)
    {
        if (CurrentJob == null || CurrentJob.JobId != jobId) return;

        CurrentJob.Status = ImportStatus.Processing;
        CurrentJob.ProcessedFiles = 0;
        OnProgressChanged?.Invoke();

        Console.WriteLine($"[ImportJobService] Started processing job {jobId}, {manifest.Count} PCs");
        var userId = CurrentJob.UserId;
        var tempJobPath = Path.Combine(_tempBasePath, jobId);

        _ = Task.Run(() => ProcessInBackground(jobId, tempJobPath, manifest, coverMappings, userId, overrideCoverFolders));
    }

    /// <summary>Read a temp file and convert to PDF if it's a doc/docx. Returns (bytes, finalFileName, skipReason).
    /// skipReason is null on success; "Upload error" if temp file is missing; "Conversion failed" if LibreOffice failed.</summary>
    private (byte[]? Bytes, string FileName, string SkipReason) ReadAndConvert(string tempFilePath, string originalFileName)
    {
        if (!File.Exists(tempFilePath))
        {
            Console.WriteLine($"[Import] Upload error — temp file missing: '{originalFileName}'");
            return (null, originalFileName, "Upload error");
        }

        var ext = Path.GetExtension(originalFileName);
        var bytes = File.ReadAllBytes(tempFilePath);

        if (FolderService.ConvertibleExtensions.Contains(ext))
        {
            var pdfBytes = _folderSvc.ConvertToPdf(bytes, originalFileName);
            if (pdfBytes != null)
            {
                var pdfName = Path.GetFileNameWithoutExtension(originalFileName) + ".pdf";
                File.Delete(tempFilePath);
                return (pdfBytes, pdfName, null!);
            }
            Console.WriteLine($"[Import] Could not convert '{originalFileName}' to PDF — skipping");
            File.Delete(tempFilePath);
            return (null, originalFileName, "Conversion failed");
        }

        File.Delete(tempFilePath);
        return (bytes, originalFileName, null!);
    }

    private void ProcessInBackground(string jobId, string tempJobPath,
        List<ImportPcManifest> manifest, List<ImportCoverMapping> coverMappings, int userId,
        bool overrideCoverFolders = false)
    {
        try
        {
            // Snapshot DB before any writes so import can be rolled back manually if needed
            BackupDbBeforeImport();

            if (overrideCoverFolders)
            {
                Console.WriteLine("[ImportJobService] Override flag set — deleting all Front_Cover and Back_Cover dirs...");
                _folderSvc.DeleteAllCoverDirectories();
            }

            // Process non-solo first, then solo — preserving the table order within each group
            var ordered = manifest
                .Select((pc, idx) => (pc, idx))
                .OrderBy(x => x.pc.IsSolo ? 1 : 0)
                .ThenBy(x => x.idx)
                .Select(x => x.pc)
                .ToList();

            foreach (var pc in ordered)
            {
                UpdateStatus(jobId, $"Processing PC: {pc.PcName}...");

                int pcId;
                bool wasCreated = false;

                if (pc.IsSolo)
                {
                    // Resolve at job time: non-solo PCs already processed so newly created ones are in DB
                    var resolvedId = pc.ExistingPcId ?? _pcSvc.FindPcByName(pc.PcName);
                    if (resolvedId == null)
                    {
                        // No match found — create an empty regular PC automatically
                        Console.WriteLine($"[ImportJobService] Solo PC '{pc.PcName}' has no regular PC — creating empty PC");
                        var (newId, _) = _pcSvc.FindOrCreatePcByName(pc.PcName);
                        resolvedId = newId;
                        if (CurrentJob != null) CurrentJob.NewPcsCreated++;
                    }
                    pcId = resolvedId.Value;

                    // Create solo user account if needed
                    var nameParts = pc.PcName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var fn = nameParts.Length > 0 ? NormalizeToAscii(nameParts[0]) : "unknown";
                    var ln = nameParts.Length > 1 ? NormalizeToAscii(nameParts[1]) : "unknown";
                    var username = $"{fn}.{ln}";
                    var password = fn.Length > 0 ? char.ToUpper(fn[0]) + fn[1..] + "1992" : "User1992";
                    if (!_userDb.UsernameExists(username))
                    {
                        var newUserId = _userDb.CreateUser(pcId, username, password, "Solo", "Standard", null, false);
                        _userDb.SetContactConfirmNeeded(newUserId);
                        Console.WriteLine($"[ImportJobService] Created Solo user '{username}' for PC {pcId}");

                        // Send welcome SMS if phone is on file
                        var (_, phone) = _pcSvc.GetPersonContact(pcId);
                        if (!string.IsNullOrWhiteSpace(phone))
                        {
                            var msg = $"Welcome to LPM!\nUsername: {username}\nPassword: {password}\nSite: lpmanager.cv";
                            _ = _smsSvc.SendSmsAsync(phone, msg);
                        }
                        else
                        {
                            Console.WriteLine($"[ImportJobService] No phone for Solo '{username}' — SMS skipped");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[ImportJobService] Solo user '{username}' already exists for PC {pcId} — skipped");
                    }

                    Console.WriteLine($"[ImportJobService] Processing Solo PC: {pc.PcName} (matched id={pcId})");

                    // Front_Cover and Back_Cover → solo folder, always overwrite on re-import
                    foreach (var file in pc.Files.Where(f => f.Section is "Front_Cover" or "Back_Cover"))
                    {
                        UpdateStatus(jobId, $"{pc.PcName} [Solo]: {file.FileName}");
                        var tempFile = string.IsNullOrEmpty(file.SubPath)
                            ? Path.Combine(tempJobPath, pc.FolderName, file.Section, file.FileName)
                            : Path.Combine(tempJobPath, pc.FolderName, file.Section, file.SubPath, file.FileName);
                        var (bytes, finalName, skipReason) = ReadAndConvert(tempFile, file.FileName);
                        if (bytes == null) { IncrementSkipped(jobId, pc.PcName, file.FileName, file.Section, skipReason); continue; }
                        if (_folderSvc.SoloSectionFileExists(pcId, file.Section, finalName, file.SubPath))
                            _folderSvc.OverwriteSoloSectionFile(pcId, file.Section, finalName, bytes, file.SubPath);
                        else
                            _folderSvc.SaveSoloSectionFile(pcId, file.Section, finalName, bytes, file.SubPath);
                        IncrementProcessed(jobId);
                    }

                    // WorkSheets → solo folder, session with AuditorId=NULL
                    foreach (var file in pc.Files.Where(f => f.Section == "WorkSheets"))
                    {
                        UpdateStatus(jobId, $"{pc.PcName} [Solo]: {file.FileName}");
                        var tempFile = string.IsNullOrEmpty(file.SubPath)
                            ? Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", file.FileName)
                            : Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", file.SubPath, file.FileName);
                        var (bytes, finalName, skipReason) = ReadAndConvert(tempFile, file.FileName);
                        if (bytes == null) { IncrementSkipped(jobId, pc.PcName, file.FileName, "WorkSheets", skipReason); continue; }
                        if (_folderSvc.SoloSectionFileExists(pcId, "WorkSheets", finalName, file.SubPath))
                        { IncrementSkipped(jobId, pc.PcName, finalName, "WorkSheets", "Already exists"); continue; }
                        _folderSvc.SaveSoloSectionFile(pcId, "WorkSheets", finalName, bytes, file.SubPath);
                        var sessionName = Path.GetFileNameWithoutExtension(finalName);
                        var sessionDate = ParseSessionDate(file);
                        var createdAt = ParseCreatedAt(file);
                        _dashSvc.CreateImportedSessionWithDate(pcId, userId, sessionName, sessionDate, createdAt, userId, isSolo: true);
                        if (CurrentJob != null) CurrentJob.SessionsCreated++;
                        Console.WriteLine($"[ImportJobService] Created solo session for PC {pcId}: '{sessionName}'");
                        IncrementProcessed(jobId);
                    }

                    // Attachments → solo folder
                    foreach (var file in pc.Files.Where(f => f.Section == "Attachment"))
                    {
                        UpdateStatus(jobId, $"{pc.PcName} [Solo]: {file.FileName} (attachment)");
                        var parentNoExt = file.ParentSessionFile != null
                            ? Path.GetFileNameWithoutExtension(file.ParentSessionFile) : null;
                        var tempFile = parentNoExt != null
                            ? Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", $"{parentNoExt}_att", file.FileName)
                            : Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", file.FileName);
                        var (bytes, finalName, skipReason) = ReadAndConvert(tempFile, file.FileName);
                        if (bytes == null) { IncrementSkipped(jobId, pc.PcName, file.FileName, "Attachment", skipReason); continue; }
                        if (file.ParentSessionFile != null)
                        {
                            if (_folderSvc.SoloAttachmentFileExists(pcId, file.ParentSessionFile, finalName))
                            { IncrementSkipped(jobId, pc.PcName, finalName, "Attachment", "Already exists"); continue; }
                            _folderSvc.SaveSoloImportedAttachment(pcId, file.ParentSessionFile, finalName, bytes);
                        }
                        else
                        {
                            if (_folderSvc.SoloSectionFileExists(pcId, "WorkSheets", finalName))
                            { IncrementSkipped(jobId, pc.PcName, finalName, "Attachment", "Already exists"); continue; }
                            _folderSvc.SaveSoloSectionFile(pcId, "WorkSheets", finalName, bytes);
                        }
                        IncrementProcessed(jobId);
                    }
                }
                else
                {
                    // Regular (non-solo) PC
                    (pcId, wasCreated) = _pcSvc.FindOrCreatePcByName(pc.PcName);
                    if (wasCreated && CurrentJob != null) CurrentJob.NewPcsCreated++;
                    Console.WriteLine($"[ImportJobService] Processing PC: {pc.PcName} (id={pcId}, new={wasCreated})");

                    // Front_Cover and Back_Cover — always overwrite on re-import
                    foreach (var file in pc.Files.Where(f => f.Section is "Front_Cover" or "Back_Cover"))
                    {
                        UpdateStatus(jobId, $"{pc.PcName}: {file.FileName}");
                        var tempFile = string.IsNullOrEmpty(file.SubPath)
                            ? Path.Combine(tempJobPath, pc.FolderName, file.Section, file.FileName)
                            : Path.Combine(tempJobPath, pc.FolderName, file.Section, file.SubPath, file.FileName);
                        var (bytes, finalName, skipReason) = ReadAndConvert(tempFile, file.FileName);
                        if (bytes == null) { IncrementSkipped(jobId, pc.PcName, file.FileName, file.Section, skipReason); continue; }
                        if (_folderSvc.SectionFileExists(pcId, file.Section, finalName, file.SubPath))
                            _folderSvc.OverwriteSectionFile(pcId, file.Section, finalName, bytes, file.SubPath);
                        else
                            _folderSvc.SaveSectionFile(pcId, file.Section, finalName, bytes, file.SubPath);
                        IncrementProcessed(jobId);
                    }

                    // WorkSheets — create session record for each
                    foreach (var file in pc.Files.Where(f => f.Section == "WorkSheets"))
                    {
                        UpdateStatus(jobId, $"{pc.PcName}: {file.FileName}");
                        var tempFile = string.IsNullOrEmpty(file.SubPath)
                            ? Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", file.FileName)
                            : Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", file.SubPath, file.FileName);
                        var (bytes, finalName, skipReason) = ReadAndConvert(tempFile, file.FileName);
                        if (bytes == null) { IncrementSkipped(jobId, pc.PcName, file.FileName, "WorkSheets", skipReason); continue; }
                        if (_folderSvc.SectionFileExists(pcId, "WorkSheets", finalName, file.SubPath))
                        { IncrementSkipped(jobId, pc.PcName, finalName, "WorkSheets", "Already exists"); continue; }
                        _folderSvc.SaveSectionFile(pcId, "WorkSheets", finalName, bytes, file.SubPath);
                        var sessionName = Path.GetFileNameWithoutExtension(finalName);
                        var sessionDate = ParseSessionDate(file);
                        var createdAt = ParseCreatedAt(file);
                        _dashSvc.CreateImportedSessionWithDate(pcId, userId, sessionName, sessionDate, createdAt, userId, isSolo: false);
                        if (CurrentJob != null) CurrentJob.SessionsCreated++;
                        Console.WriteLine($"[ImportJobService] Created session for PC {pcId}: '{sessionName}'");
                        IncrementProcessed(jobId);
                    }

                    // Attachments
                    foreach (var file in pc.Files.Where(f => f.Section == "Attachment"))
                    {
                        UpdateStatus(jobId, $"{pc.PcName}: {file.FileName} (attachment)");
                        var parentNoExt = file.ParentSessionFile != null
                            ? Path.GetFileNameWithoutExtension(file.ParentSessionFile) : null;
                        var tempFile = parentNoExt != null
                            ? Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", $"{parentNoExt}_att", file.FileName)
                            : Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", file.FileName);
                        var (bytes, finalName, skipReason) = ReadAndConvert(tempFile, file.FileName);
                        if (bytes == null) { IncrementSkipped(jobId, pc.PcName, file.FileName, "Attachment", skipReason); continue; }
                        if (file.ParentSessionFile != null)
                        {
                            if (_folderSvc.AttachmentFileExists(pcId, file.ParentSessionFile, finalName))
                            { IncrementSkipped(jobId, pc.PcName, finalName, "Attachment", "Already exists"); continue; }
                            _folderSvc.SaveImportedAttachment(pcId, file.ParentSessionFile, finalName, bytes);
                        }
                        else
                        {
                            if (_folderSvc.SectionFileExists(pcId, "WorkSheets", finalName))
                            { IncrementSkipped(jobId, pc.PcName, finalName, "Attachment", "Already exists"); continue; }
                            _folderSvc.SaveSectionFile(pcId, "WorkSheets", finalName, bytes);
                        }
                        IncrementProcessed(jobId);
                    }
                } // end else (non-solo)
            } // end foreach pc

            if (CurrentJob != null && CurrentJob.JobId == jobId)
            {
                CurrentJob.Status = ImportStatus.Complete;
                CurrentJob.CurrentFileName = "";
                OnProgressChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            if (CurrentJob != null && CurrentJob.JobId == jobId)
            {
                CurrentJob.Status = ImportStatus.Failed;
                CurrentJob.ErrorMessage = ex.Message;
                OnProgressChanged?.Invoke();
            }
            Console.WriteLine($"[Import] Error: {ex}");
        }
        finally
        {
            CleanupTempFolder(jobId);
        }
    }

    private void UpdateStatus(string jobId, string status)
    {
        if (CurrentJob != null && CurrentJob.JobId == jobId)
        {
            CurrentJob.CurrentFileName = status;
            CurrentJob.LastActivity = DateTime.UtcNow;
            OnProgressChanged?.Invoke();
        }
    }

    private void IncrementProcessed(string jobId)
    {
        if (CurrentJob != null && CurrentJob.JobId == jobId)
        {
            CurrentJob.ProcessedFiles++;
            OnProgressChanged?.Invoke();
        }
    }

    private void IncrementSkipped(string jobId, string pcName, string fileName, string section, string reason)
    {
        if (CurrentJob != null && CurrentJob.JobId == jobId)
        {
            CurrentJob.SkippedList.Add(new SkippedFileRecord(pcName, fileName, section, reason));
            CurrentJob.SkippedCount++;
            CurrentJob.ProcessedFiles++;
            Console.WriteLine($"[Import] SKIPPED ({reason}): PC='{pcName}' File='{fileName}' Section='{section}'");
            OnProgressChanged?.Invoke();
        }
    }

    public void ClearJob()
    {
        lock (_lock)
        {
            if (CurrentJob != null)
                CleanupTempFolder(CurrentJob.JobId);
            CurrentJob = null;
        }
    }

    private void CleanupTempFolder(string jobId)
    {
        try
        {
            var path = Path.Combine(_tempBasePath, jobId);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    // ── Adjust Auditors/CS ──

    private void BackupDbBeforeAdjust()
    {
        try
        {
            Directory.CreateDirectory(_dbBackupFolder);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var destPath = Path.Combine(_dbBackupFolder, $"lifepower_{timestamp}_Before adjusting auditors.db");
            using var src = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
            using var dst = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={destPath}");
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
            Console.WriteLine($"[ImportJobService] DB backed up to '{destPath}' before adjust");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImportJobService] DB backup before adjust FAILED: {ex.Message}");
            throw; // fatal — do not proceed without backup
        }
    }

    public AdjustResult AdjustAuditorsAndCs()
    {
        BackupDbBeforeAdjust();

        var rows = new List<AdjustResultRow>();
        int p1Found = 0, p1Set = 0, p1NoMatch = 0;
        int p2Found = 0, p2ReviewsFound = 0, p2Set = 0, p2NoMatch = 0;
        int p3Found = 0, p3ReviewsFound = 0, p3Set = 0, p3NoMatch = 0;

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        // ── Load sessions ──
        var nonSoloSessions = new List<(int SessionId, int PcId)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT SessionId, PcId FROM sess_sessions WHERE AuditorId = -1 AND IsImported = 1";
            using var r = cmd.ExecuteReader();
            while (r.Read()) nonSoloSessions.Add((r.GetInt32(0), r.GetInt32(1)));
        }
        p1Found = nonSoloSessions.Count;
        p2Found = nonSoloSessions.Count;

        var soloSessions = new List<(int SessionId, int PcId)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT SessionId, PcId FROM sess_sessions WHERE AuditorId IS NULL AND IsImported = 1";
            using var r = cmd.ExecuteReader();
            while (r.Read()) soloSessions.Add((r.GetInt32(0), r.GetInt32(1)));
        }
        p3Found = soloSessions.Count;

        // ── Batch-load PC names ──
        var allPcIds = nonSoloSessions.Select(s => s.PcId)
            .Concat(soloSessions.Select(s => s.PcId))
            .Distinct().ToList();

        var pcNames = new Dictionary<int, string>();
        if (allPcIds.Count > 0)
        {
            var idList = string.Join(",", allPcIds);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT p.PersonId, p.FirstName || ' ' || COALESCE(p.LastName, '')
                FROM core_persons p
                WHERE p.PersonId IN ({idList})";
            using var r = cmd.ExecuteReader();
            while (r.Read()) pcNames[r.GetInt32(0)] = r.GetString(1).Trim();
        }

        // ── Part 1: Fix Auditors ──
        foreach (var (sessionId, pcId) in nonSoloSessions)
        {
            var pcName = pcNames.GetValueOrDefault(pcId, $"PC#{pcId}");
            int? auditorUserId = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT UserId FROM sys_staff_pc_list WHERE PcId = @p AND WorkCapacity = 'Auditor' ORDER BY Id LIMIT 1";
                cmd.Parameters.AddWithValue("@p", pcId);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value) auditorUserId = Convert.ToInt32(res);
            }
            // Fallback: if no Auditor entry, try CS (CS may act as sole auditor for this PC)
            if (!auditorUserId.HasValue)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT UserId FROM sys_staff_pc_list WHERE PcId = @p AND WorkCapacity = 'CS' ORDER BY Id LIMIT 1";
                cmd.Parameters.AddWithValue("@p", pcId);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value) auditorUserId = Convert.ToInt32(res);
            }
            if (auditorUserId.HasValue)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE sess_sessions SET AuditorId = @a WHERE SessionId = @s";
                cmd.Parameters.AddWithValue("@a", auditorUserId.Value);
                cmd.Parameters.AddWithValue("@s", sessionId);
                cmd.ExecuteNonQuery();
                p1Set++;
                rows.Add(new AdjustResultRow(sessionId, pcId, pcName, "Auditor", $"Set → User {auditorUserId}"));
            }
            else
            {
                p1NoMatch++;
                rows.Add(new AdjustResultRow(sessionId, pcId, pcName, "Auditor", "No staff match"));
            }
        }

        // ── Part 2: Fix CS for non-solo sessions ──
        foreach (var (sessionId, pcId) in nonSoloSessions)
        {
            var pcName = pcNames.GetValueOrDefault(pcId, $"PC#{pcId}");
            int? csReviewId = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT CsReviewId FROM cs_reviews WHERE SessionId = @s";
                cmd.Parameters.AddWithValue("@s", sessionId);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value) csReviewId = Convert.ToInt32(res);
            }
            if (!csReviewId.HasValue)
            {
                rows.Add(new AdjustResultRow(sessionId, pcId, pcName, "CS (non-solo)", "No review row"));
                continue;
            }
            p2ReviewsFound++;

            int? csUserId = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT UserId FROM sys_staff_pc_list WHERE PcId = @p AND WorkCapacity = 'CS' ORDER BY Id LIMIT 1";
                cmd.Parameters.AddWithValue("@p", pcId);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value) csUserId = Convert.ToInt32(res);
            }
            if (csUserId.HasValue)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE cs_reviews SET CsId = @c WHERE CsReviewId = @r";
                cmd.Parameters.AddWithValue("@c", csUserId.Value);
                cmd.Parameters.AddWithValue("@r", csReviewId.Value);
                cmd.ExecuteNonQuery();
                p2Set++;
                rows.Add(new AdjustResultRow(sessionId, pcId, pcName, "CS (non-solo)", $"Set → User {csUserId}"));
            }
            else
            {
                p2NoMatch++;
                rows.Add(new AdjustResultRow(sessionId, pcId, pcName, "CS (non-solo)", "No staff match"));
            }
        }

        // ── Part 3: Fix CS for solo sessions ──
        foreach (var (sessionId, pcId) in soloSessions)
        {
            var pcName = pcNames.GetValueOrDefault(pcId, $"PC#{pcId}");
            int? csReviewId = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT CsReviewId FROM cs_reviews WHERE SessionId = @s";
                cmd.Parameters.AddWithValue("@s", sessionId);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value) csReviewId = Convert.ToInt32(res);
            }
            if (!csReviewId.HasValue)
            {
                rows.Add(new AdjustResultRow(sessionId, pcId, pcName, "CS (solo)", "No review row"));
                continue;
            }
            p3ReviewsFound++;

            int? csUserId = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT UserId FROM sys_staff_pc_list WHERE PcId = @p AND WorkCapacity = 'CS' ORDER BY Id LIMIT 1";
                cmd.Parameters.AddWithValue("@p", pcId);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value) csUserId = Convert.ToInt32(res);
            }
            if (csUserId.HasValue)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE cs_reviews SET CsId = @c WHERE CsReviewId = @r";
                cmd.Parameters.AddWithValue("@c", csUserId.Value);
                cmd.Parameters.AddWithValue("@r", csReviewId.Value);
                cmd.ExecuteNonQuery();
                p3Set++;
                rows.Add(new AdjustResultRow(sessionId, pcId, pcName, "CS (solo)", $"Set → User {csUserId}"));
            }
            else
            {
                p3NoMatch++;
                rows.Add(new AdjustResultRow(sessionId, pcId, pcName, "CS (solo)", "No staff match"));
            }
        }

        Console.WriteLine($"[ImportJobService] AdjustAuditorsAndCs done — P1: {p1Found} found, {p1Set} set | P2: {p2Found} found, {p2Set} set | P3: {p3Found} found, {p3Set} set");
        return new AdjustResult(p1Found, p1Set, p1NoMatch, p2Found, p2ReviewsFound, p2Set, p2NoMatch, p3Found, p3ReviewsFound, p3Set, p3NoMatch, rows);
    }

    // ── Name helpers ──

    internal static string NormalizeToAscii(string input)
    {
        var normalized = input.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
    }

    // ── Date parsing helpers ──

    static string ParseSessionDate(ImportFileManifest file)
    {
        var name = file.FileName;
        if (name.Length >= 9 && name[2] == '-' && name[5] == '-' && name[8] == '_')
        {
            var dateStr = "20" + name[..8];
            if (DateOnly.TryParseExact(dateStr, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
                return d.ToString("yyyy-MM-dd");
        }
        if (!string.IsNullOrEmpty(file.LastModified) && DateTime.TryParse(file.LastModified, out var dt))
            return DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd");
        return DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    }

    static string ParseCreatedAt(ImportFileManifest file)
    {
        if (!string.IsNullOrEmpty(file.LastModified) && DateTime.TryParse(file.LastModified, out var dt))
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
