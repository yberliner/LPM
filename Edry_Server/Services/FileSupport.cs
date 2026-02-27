using LPM.Data;
using FSMSGS;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace LPM
{
    public class FileSupport
    {
        public string? _selectedFileName;
        public string? _selectedFileFullPath;

        public string? _pendingDeleteFile;
        private readonly IJSRuntime _JS;
        private readonly FileService _FileService;
        private readonly IFileStore _fileStore;

        public FileSupport(IJSRuntime jsRuntime, FileService FileService, IFileStore FileStore)
        {
            _JS = jsRuntime;
            _FileService = FileService;
            _fileStore = FileStore;
        }   

        public async Task DownloadFile((string DirectoryPath, string FileName) fileInfo)
        {
            var lastSegment = Path.GetFileName(fileInfo.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // 2️⃣  Combine into a URL and escape the filename for safety
            var url = $"/download/{lastSegment}/{Uri.EscapeDataString(fileInfo.FileName)}";

            await _JS.InvokeVoidAsync("open", url, "_blank");
        }
        public void RegisterDeleteFile((string DirectoryPath, string FileName) fileInfo)
        {
            _pendingDeleteFile = Path.Combine(fileInfo.DirectoryPath, fileInfo.FileName);
        }
        public async Task OnUploadFile(InputFileChangeEventArgs e)
        {
            try
            {
                Console.WriteLine($"OnUploadFile: File count: {e.FileCount}. Directory:{_FileService.DirectoryPath}");

                if (e.FileCount == 0 || string.IsNullOrWhiteSpace(_FileService.DirectoryPath))
                {
                    Console.WriteLine("OnUploadFile: No files to upload or directory path is empty.");
                    return;
                }
                foreach (var file in e.GetMultipleFiles())                       // handles 1-or-many
                {
                    Console.WriteLine($"OnUploadFile: Processing file: {file.Name}, Size: {file.Size} bytes");
                    var targetPath = Path.Combine(_FileService.DirectoryPath, file.Name);

                    await using var inStream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024); // 10 MB limit
                    await using var outStream = File.Create(targetPath);
                    await inStream.CopyToAsync(outStream);
                }
                Console.WriteLine($"OnUploadFile: Files uploaded successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnUploadFile: Error exception uploading file: {ex.Message}");
            }
        }

        public void OnFileNameClicked((string FileName, string FullPath) fileInfo)
        {
            _selectedFileName = fileInfo.FileName;
            _selectedFileFullPath = fileInfo.FullPath;
        }

        public string? FreeSpaceGBFormatted
        {
            get
            {
                try
                {
                    var dir = _FileService.DirectoryPath;
                    if (string.IsNullOrEmpty(dir)) return null;
                    var root = Path.GetPathRoot(dir);
                    if (string.IsNullOrEmpty(root)) return null;
                    var drive = new DriveInfo(root);
                    var gb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    if (gb < 1)
                        return gb.ToString("N2") + " GB";
                    else
                        return gb.ToString("N0") + " GB";
                }
                catch
                {
                    return "N/A";
                }
            }
        }

        public bool ConfirmDelete()
        {
            if (string.IsNullOrEmpty(_pendingDeleteFile))
                return false;

            try
            {
                if (File.Exists(_pendingDeleteFile))
                {
                    File.Delete(_pendingDeleteFile);
                }
            }
            catch
            {
                return false;
            }

            _pendingDeleteFile = null;
            return true;
        }

    }
}
