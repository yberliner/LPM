namespace LPM.Services;

public enum ImportStatus { Uploading, Processing, Complete, Failed }

public record ImportFileManifest(
    string RelativePath, string FileName, string Section,
    string? ParentSessionFile, string? LastModified);

public record ImportPcManifest(
    string FolderName, string PcName, int? ExistingPcId,
    List<ImportFileManifest> Files);

public record ImportCoverMapping(string FileName, string Section, string PcFolderName, int AssignedItemId);

public class ImportJobState
{
    public string JobId { get; init; } = "";
    public int UserId { get; init; }
    public ImportStatus Status { get; set; }
    public int TotalFiles { get; set; }
    public int UploadedFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public int NewPcsCreated { get; set; }
    public int SessionsCreated { get; set; }
    public string CurrentFileName { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; init; }
    public DateTime LastActivity { get; set; }
}

public class ImportJobService
{
    private readonly FolderService _folderSvc;
    private readonly PcService _pcSvc;
    private readonly DashboardService _dashSvc;
    private readonly string _tempBasePath;
    private readonly object _lock = new();

    public ImportJobState? CurrentJob { get; private set; }
    public event Action? OnProgressChanged;

    public ImportJobService(FolderService folderSvc, PcService pcSvc, DashboardService dashSvc)
    {
        _folderSvc = folderSvc;
        _pcSvc = pcSvc;
        _dashSvc = dashSvc;
        _tempBasePath = Path.Combine(Directory.GetCurrentDirectory(), "PC-Folders", "_import_temp");
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

            return jobId;
        }
    }

    /// <summary>Save a single uploaded file to the temp folder.</summary>
    public void RecordFileUploaded(string jobId, string pcFolderName, string section,
        string fileName, byte[] bytes, string? parentSessionFile)
    {
        if (CurrentJob == null || CurrentJob.JobId != jobId) return;

        string targetDir;
        if (parentSessionFile != null)
        {
            var sessionNoExt = Path.GetFileNameWithoutExtension(parentSessionFile);
            targetDir = Path.Combine(_tempBasePath, jobId, pcFolderName, section, $"{sessionNoExt}_att");
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
        List<ImportCoverMapping> coverMappings)
    {
        if (CurrentJob == null || CurrentJob.JobId != jobId) return;

        CurrentJob.Status = ImportStatus.Processing;
        CurrentJob.ProcessedFiles = 0;
        OnProgressChanged?.Invoke();

        var userId = CurrentJob.UserId;
        var tempJobPath = Path.Combine(_tempBasePath, jobId);

        _ = Task.Run(() => ProcessInBackground(jobId, tempJobPath, manifest, coverMappings, userId));
    }

    private void ProcessInBackground(string jobId, string tempJobPath,
        List<ImportPcManifest> manifest, List<ImportCoverMapping> coverMappings, int userId)
    {
        try
        {
            foreach (var pc in manifest)
            {
                UpdateStatus(jobId, $"Processing PC: {pc.PcName}...");

                var (pcId, wasCreated) = _pcSvc.FindOrCreatePcByName(pc.FolderName);
                if (wasCreated && CurrentJob != null) CurrentJob.NewPcsCreated++;

                // Front_Cover and Back_Cover
                foreach (var file in pc.Files.Where(f => f.Section is "Front_Cover" or "Back_Cover"))
                {
                    UpdateStatus(jobId, $"{pc.PcName}: {file.FileName}");

                    if (_folderSvc.SectionFileExists(pcId, file.Section, file.FileName))
                    {
                        IncrementSkipped(jobId);
                        continue;
                    }

                    var tempFile = Path.Combine(tempJobPath, pc.FolderName, file.Section, file.FileName);
                    if (!File.Exists(tempFile)) { IncrementSkipped(jobId); continue; }

                    var bytes = File.ReadAllBytes(tempFile);
                    _folderSvc.SaveSectionFile(pcId, file.Section, file.FileName, bytes);
                    File.Delete(tempFile);

                    IncrementProcessed(jobId);
                }

                // WorkSheets — create session record for each
                foreach (var file in pc.Files.Where(f => f.Section == "WorkSheets"))
                {
                    UpdateStatus(jobId, $"{pc.PcName}: {file.FileName}");

                    if (_folderSvc.SectionFileExists(pcId, "WorkSheets", file.FileName))
                    {
                        IncrementSkipped(jobId);
                        continue;
                    }

                    var tempFile = Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", file.FileName);
                    if (!File.Exists(tempFile)) { IncrementSkipped(jobId); continue; }

                    var bytes = File.ReadAllBytes(tempFile);
                    _folderSvc.SaveSectionFile(pcId, "WorkSheets", file.FileName, bytes);
                    File.Delete(tempFile);

                    // Create session record
                    var sessionName = Path.GetFileNameWithoutExtension(file.FileName);
                    var sessionDate = ParseSessionDate(file);
                    var createdAt = ParseCreatedAt(file);
                    _dashSvc.CreateImportedSessionWithDate(pcId, userId, sessionName,
                        sessionDate, createdAt, userId);
                    if (CurrentJob != null) CurrentJob.SessionsCreated++;

                    IncrementProcessed(jobId);
                }

                // Attachments
                foreach (var file in pc.Files.Where(f => f.Section == "Attachment"))
                {
                    UpdateStatus(jobId, $"{pc.PcName}: {file.FileName} (attachment)");

                    if (file.ParentSessionFile != null &&
                        _folderSvc.AttachmentFileExists(pcId, file.ParentSessionFile, file.FileName))
                    {
                        IncrementSkipped(jobId);
                        continue;
                    }

                    var parentNoExt = file.ParentSessionFile != null
                        ? Path.GetFileNameWithoutExtension(file.ParentSessionFile) : null;
                    var tempFile = parentNoExt != null
                        ? Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", $"{parentNoExt}_att", file.FileName)
                        : Path.Combine(tempJobPath, pc.FolderName, "WorkSheets", file.FileName);

                    if (!File.Exists(tempFile)) { IncrementSkipped(jobId); continue; }

                    var bytes = File.ReadAllBytes(tempFile);
                    if (file.ParentSessionFile != null)
                        _folderSvc.SaveImportedAttachment(pcId, file.ParentSessionFile, file.FileName, bytes);
                    else
                        _folderSvc.SaveSectionFile(pcId, "WorkSheets", file.FileName, bytes);
                    File.Delete(tempFile);

                    IncrementProcessed(jobId);
                }
            }

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

    private void IncrementSkipped(string jobId)
    {
        if (CurrentJob != null && CurrentJob.JobId == jobId)
        {
            CurrentJob.SkippedFiles++;
            CurrentJob.ProcessedFiles++;
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
