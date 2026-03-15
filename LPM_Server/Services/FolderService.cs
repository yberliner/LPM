using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace LPM.Services;

public record FolderFileItem(string FileName, string Section, string RelativePath, DateTime? DateParsed);
public record WorkSheetItem(FolderFileItem File, List<FolderFileItem> Attachments);
public record PcFolderInfo(int PcId, string PcName, string FolderPath, List<FolderFileItem> FrontCover, List<FolderFileItem> BackCover, List<WorkSheetItem> WorkSheets);

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

        return new PcFolderInfo(pcId, pcName, folder, frontCover, backCover, workSheets);
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
    public byte[]? ReadFileBytes(int pcId, string relativePath)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return null;

        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null || !File.Exists(fullPath)) return null;

        var raw = File.ReadAllBytes(fullPath);
        return DecryptBytes(raw);
    }

    /// <summary>Save annotated PDF bytes back to disk (encrypted)</summary>
    public bool SaveFile(int pcId, string relativePath, byte[] pdfBytes)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return false;

        var fullPath = SafeResolvePath(folder, relativePath);
        if (fullPath == null) return false;

        File.WriteAllBytes(fullPath, EncryptBytes(pdfBytes));
        return true;
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
    public bool SessionFileExistsByName(int pcId, string sessionName)
    {
        var folder = FindPcFolder(pcId);
        if (folder == null) return false;
        var wsPath = Path.Combine(folder, "WorkSheets");
        if (!Directory.Exists(wsPath)) return false;

        foreach (var f in Directory.GetFiles(wsPath, "*.pdf"))
        {
            var name = Path.GetFileNameWithoutExtension(Path.GetFileName(f));
            if (name.Contains("_att_", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals(sessionName, StringComparison.OrdinalIgnoreCase))
                return true;
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

        // Write plaintext first so Ghostscript can shrink it
        File.WriteAllBytes(fullPath, fileBytes);
        TryShrinkPdf(fullPath);
        // Now encrypt the (possibly shrunk) file in-place
        EncryptFileInPlace(fullPath);
        return Path.GetFileName(fullPath);
    }

    /// <summary>Save an attachment file as flat file in WorkSheets: {session}_att_{name}.</summary>
    public void SaveAttachment(int pcId, string sessionFileName, string attFileName, byte[] fileBytes)
    {
        var folder = FindPcFolder(pcId);
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
        // Detect unencrypted legacy files — PDF starts with %PDF
        if (raw.Length >= 4 && raw[0] == '%' && raw[1] == 'P' && raw[2] == 'D' && raw[3] == 'F')
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
