using CardModel;
using System.Text;

namespace LPM.Data
{
    public class FileEntry
    {
        public enum FileTargetType
        {
            Unknown,
            MICCB,
            MOCB,
            RC,
            MC
        };

        private static readonly string[] FileTargetTypeName = {
            "Unknown",
            "MICCB",
            "MOCB",
            "RC",
            "MC"
        };
        public string FileName { get; set; } = string.Empty;
        public string UploadedTime { get; set; } = string.Empty;

        // Raw file size in bytes
        public long SizeInBytes { get; set; }

        // Formatted size as string with commas
        public string FileSize => SizeInBytes.ToString("N0");  // e.g., "2,332,876"

        // Human-readable size (e.g., 1.2 MB)
        public string FileSizeReadable => FormatSize(SizeInBytes);

        // Add this property for the full file path
        public string FullPath { get; set; } = string.Empty;

        public FileTargetType FileTarget { get; set; } = FileTargetType.Unknown;

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.#} {sizes[order]}";
        }

        public string FileTargetName
        {
            get
            {
                int index = (int)FileTarget;
                // Ensure index is within bounds
                if (index >= 0 && index < FileTargetTypeName.Length)
                    return FileTargetTypeName[index];
                return FileTargetTypeName[0];
            }
        }

        public void AnalyzeIni()
        {
            // 1️⃣  Must have a path
            if (string.IsNullOrWhiteSpace(FullPath) || !File.Exists(FullPath))
            {
                FileTarget = FileTargetType.Unknown;
                return;
            }

            // 2️⃣  QUICK binary sniff (unchanged) ...............................
            try
            {
                const int sampleLen = 512;
                Span<byte> sample = stackalloc byte[sampleLen];
                int read;
                using (var fs = File.OpenRead(FullPath))
                    read = fs.Read(sample);

                if (read == 0) { FileTarget = FileTargetType.Unknown; return; }

                int nonPrintable = 0;
                for (int i = 0; i < read; i++)
                {
                    byte b = sample[i];
                    if (b == 0) { FileTarget = FileTargetType.Unknown; return; }
                    if (b < 0x09 || (b > 0x0D && b < 0x20)) nonPrintable++;
                }
                if (nonPrintable > read / 10)
                {
                    FileTarget = FileTargetType.Unknown;
                    return;
                }
            }
            catch
            {
                FileTarget = FileTargetType.Unknown;
                return;
            }

            // 3️⃣  Open as text and pull the first *meaningful* line
            string? firstLine;
            try
            {
                using var sr = new StreamReader(
                    FullPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                do
                {
                    firstLine = sr.ReadLine();
                    if (firstLine == null) break;          // EOF
                    firstLine = firstLine.Trim();          // ignore leading/trailing ws
                }
                // skip blank lines *and* comment lines that start with ';'
                while (string.IsNullOrEmpty(firstLine) || firstLine.StartsWith(';'));
            }
            catch
            {
                FileTarget = FileTargetType.Unknown;
                return;
            }

            if (string.IsNullOrEmpty(firstLine) ||
                firstLine.Length < 3 ||
                firstLine[0] != '[' || firstLine[^1] != ']')
            {
                FileTarget = FileTargetType.Unknown;
                return;
            }

            // 4️⃣  Map header → enum
            switch (firstLine)
            {
                case "[MicroscopeWsMicbConfigInfo]":
                    FileTarget = FileTargetType.MICCB; break;
                case "[MicroscopeWsMocbConfigInfo]":
                    FileTarget = FileTargetType.MOCB; break;
                case "[RobotWsConfigInfo]":
                    FileTarget = FileTargetType.RC; break;
                case "[SurgeonWsConfigInfo]":
                    FileTarget = FileTargetType.MC; break;
                default:
                    FileTarget = FileTargetType.Unknown;
                    break;
            }
        }
    }

    public class FileService
    {
        private readonly string _directoryPath;
        public bool IsIniFile { get; } = false;

        public string DirectoryPath => _directoryPath;

        public List<TableText> GetFileHeadersData()
        {
            var headers = new List<TableText>
            {
                new() { Title = "Filename",    HeaderClass = "w-100"       },
                new() { Title = "Create Time", HeaderClass = "text-nowrap" },
                new() { Title = "Size",        HeaderClass = "text-nowrap" },
                new() { Title = "Actions",     HeaderClass = "text-nowrap" }
            };

            if (IsIniFile)
            {
                headers.Insert(1,new TableText { Title = "File Type", HeaderClass = "text-nowrap" });
            }

            return headers;
        }

        public FileService(string directoryPath, bool isIniFile=false)
        {
            _directoryPath = directoryPath;
            if (!Directory.Exists(_directoryPath))
                Directory.CreateDirectory(_directoryPath);
            IsIniFile = isIniFile;
        }

        // Get file metadata list
        public List<FileEntry> GetData()
        {
            var files = new List<FileEntry>();

            if (!Directory.Exists(_directoryPath))
                return files;

            foreach (var filePath in Directory.GetFiles(_directoryPath))
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    FileEntry fileEntry = new FileEntry
                    {
                        FileName = fileInfo.Name,
                        UploadedTime = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        SizeInBytes = fileInfo.Length,
                        FullPath = fileInfo.FullName
                    };
                    if (IsIniFile)
                    {
                        fileEntry.AnalyzeIni();
                    }

                    files.Add(fileEntry);
                }
                catch (Exception ex)
                {
                    //This can happen if script results file is being zipped and original file is deleted
                    Console.WriteLine($"Error processing file {filePath}: {ex.Message}");
                }
            }

            return files.OrderByDescending(f => f.UploadedTime).ToList();
        }
    }

    public class AllScriptResServices
    {
        public ScriptResServices? FullScripts;
        public ScriptResServices? RwsScripts;
        public FileService? IniFileServices; 
    }
    public class ScriptResServices
    {
        public FileService Scripts;
        public FileService Results;
        public ScriptResServices(string ScriptDir,string ResultDir)
        {
            Scripts = new FileService(ScriptDir);
            Results = new FileService(ResultDir);
        }
    }
}
