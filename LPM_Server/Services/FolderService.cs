using Microsoft.Data.Sqlite;

namespace LPM.Services;

public record FolderFileItem(string FileName, string Section, string RelativePath, DateTime? DateParsed);
public record WorkSheetItem(FolderFileItem File, List<FolderFileItem> Attachments);
public record PcFolderInfo(int PcId, string PcName, string FolderPath, List<FolderFileItem> FrontCover, List<FolderFileItem> BackCover, List<WorkSheetItem> WorkSheets);

public class FolderService
{
    private readonly string _basePath;
    private readonly string _connectionString;

    public FolderService(IConfiguration config)
    {
        _basePath = Path.Combine(Directory.GetCurrentDirectory(), "PC-Folders");
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
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

        return new PcFolderInfo(pcId, pcName, folder, frontCover, backCover, workSheets);
    }

    private List<WorkSheetItem> GetWorkSheets(string pcFolder)
    {
        var wsPath = Path.Combine(pcFolder, "WorkSheets");
        if (!Directory.Exists(wsPath)) return [];

        var files = Directory.GetFiles(wsPath, "*.pdf")
            .Select(f =>
            {
                var name = Path.GetFileName(f);
                var dateParsed = TryParseDatePrefix(name);
                var relativePath = $"WorkSheets/{name}";
                return new FolderFileItem(name, "WorkSheets", relativePath, dateParsed);
            })
            .OrderByDescending(f => f.DateParsed ?? DateTime.MinValue)
            .ToList();

        var items = new List<WorkSheetItem>();
        foreach (var file in files)
        {
            // Check for _att subfolder: WorkSheets/{filenameWithoutExt}_att/
            var nameNoExt = Path.GetFileNameWithoutExtension(file.FileName);
            var attDir = Path.Combine(wsPath, $"{nameNoExt}_att");
            var attachments = new List<FolderFileItem>();
            if (Directory.Exists(attDir))
            {
                attachments = Directory.GetFiles(attDir, "*.pdf")
                    .Select(af =>
                    {
                        var afName = Path.GetFileName(af);
                        var relPath = $"WorkSheets/{nameNoExt}_att/{afName}";
                        return new FolderFileItem(afName, "WorkSheets", relPath, null);
                    })
                    .OrderBy(af => af.FileName)
                    .ToList();
            }
            items.Add(new WorkSheetItem(file, attachments));
        }
        return items;
    }

    private List<FolderFileItem> GetFilesForSection(string pcFolder, string section)
    {
        var sectionPath = Path.Combine(pcFolder, section);
        if (!Directory.Exists(sectionPath)) return [];

        return Directory.GetFiles(sectionPath, "*.pdf")
            .Select(f =>
            {
                var name = Path.GetFileName(f);
                var dateParsed = TryParseDatePrefix(name);
                var relativePath = $"{section}/{name}";
                return new FolderFileItem(name, section, relativePath, dateParsed);
            })
            .ToList();
    }

    /// <summary>Parse yy-mm-dd_ prefix from filename</summary>
    private static DateTime? TryParseDatePrefix(string fileName)
    {
        // Format: yy-mm-dd_Name.pdf
        if (fileName.Length < 9 || fileName[2] != '-' || fileName[5] != '-' || fileName[8] != '_')
            return null;
        var dateStr = fileName[..8]; // "yy-mm-dd"
        if (DateTime.TryParseExact("20" + dateStr, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        return null;
    }

    /// <summary>Get the absolute path for a file given pcId and relative path</summary>
    public string? GetFilePath(int pcId, string relativePath)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return null;

        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null) return null;

        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>Save annotated PDF bytes back to disk</summary>
    public bool SaveFile(int pcId, string relativePath, byte[] pdfBytes)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return false;

        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null) return false;

        File.WriteAllBytes(fullPath, pdfBytes);
        return true;
    }

    /// <summary>Save an uploaded session file to WorkSheets with date prefix. Returns the saved filename.</summary>
    public string? SaveUploadedFile(int pcId, string fileName, byte[] fileBytes)
    {
        var folder = GetPcFolder(pcId);
        if (folder == null) return null;

        var wsPath = Path.Combine(folder.FolderPath, "WorkSheets");
        Directory.CreateDirectory(wsPath);

        var datePrefix = DateTime.Today.ToString("yy-MM-dd");
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var finalName = $"{datePrefix}_{safeName}";
        var fullPath = Path.Combine(wsPath, finalName);

        var counter = 2;
        var nameNoExt = Path.GetFileNameWithoutExtension(finalName);
        var ext = Path.GetExtension(finalName);
        while (File.Exists(fullPath))
        {
            fullPath = Path.Combine(wsPath, $"{nameNoExt}({counter}){ext}");
            counter++;
        }

        File.WriteAllBytes(fullPath, fileBytes);
        return Path.GetFileName(fullPath);
    }

    /// <summary>Save an attachment file into the _att subfolder for a session file.</summary>
    public void SaveAttachment(int pcId, string sessionFileName, string attFileName, byte[] fileBytes)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return;

        var wsPath = Path.Combine(folder, "WorkSheets");
        var sessionNoExt = Path.GetFileNameWithoutExtension(sessionFileName);
        var attDir = Path.Combine(wsPath, $"{sessionNoExt}_att");
        Directory.CreateDirectory(attDir);

        var safeName = string.Join("_", attFileName.Split(Path.GetInvalidFileNameChars()));
        var fullPath = Path.Combine(attDir, safeName);

        var counter = 2;
        var nameNoExt = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        while (File.Exists(fullPath))
        {
            fullPath = Path.Combine(attDir, $"{nameNoExt}({counter}){ext}");
            counter++;
        }

        File.WriteAllBytes(fullPath, fileBytes);
    }

    /// <summary>Resolve relative path safely — prevents traversal and absolute path injection</summary>
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
