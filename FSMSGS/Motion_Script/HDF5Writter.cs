using HDF.PInvoke;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class HDF5Writter : IDisposable
    {
        private string _outputFileName = string.Empty;
        

        private Hdf5FloatGrowingDataset? _hdf;
        private bool _disposed = false; // Dispose flag
        private readonly object _syncRoot = new(); // Lock object

        public string OutputFileName { get => _outputFileName; }

        public HDF5Writter(string load_file, string sub_folder, string agentName)
        {
            GenerateNewFileName(load_file, sub_folder, agentName);           
        }

        public HDF5Writter()
        {
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_hdf != null)
                {
                    _hdf?.Dispose();
                    _hdf = null;

                    _outputFileName = string.Empty;
                }
                ZipOutputFile();
                _disposed = true;
                GC.SuppressFinalize(this);
                Console.WriteLine("[HDF5Writter] Disposed and flushed data to file.");
            }
        }

        public void GenerateGeneralSaverName()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "UserFiles", "GeneralSave");
            Directory.CreateDirectory(dir);
            string nameWithoutExt = "GeneralSave";
            CreateOutputFile(dir, nameWithoutExt);

        }
        private void GenerateNewFileName(string inputFileName, string sub_folder, string agentName)
        {
            if (_outputFileName.Length > 0)
            {
                return;
            }
            inputFileName = Path.ChangeExtension(inputFileName, ".h5");
            string? dir = Path.GetDirectoryName(inputFileName);
            dir = ReplaceLastSegment(dir, sub_folder);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            Directory.CreateDirectory(dir);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(inputFileName);
            
            // Replace all spaces in agentName with underscores
            //agentName = agentName.Replace(' ', '_'); 
            
            nameWithoutExt += $"_{agentName}";
            CreateOutputFile(dir, nameWithoutExt);
        }

        private void CreateOutputFile(string dir, string nameWithoutExt)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH_mm_ss");

            string baseName = $"{nameWithoutExt}_output_{timestamp}";
            string fileName = $"{baseName}.h5";
            string fullPath = Path.Combine(dir, fileName);

            int counter = 1;
            while (File.Exists(fullPath))
            {
                fileName = $"{baseName}_{counter}.h5";
                fullPath = Path.Combine(dir, fileName);
                counter++;
            }

            _outputFileName = fullPath;
            Console.WriteLine($"HDF5 output file name is: {_outputFileName}.");
        }

        public void FlushToFile(
            List<object> _linuxTimesStatusMsg,
            List<object> _linuxTimesControlMsg,
            List<object> _linuxTimesMetryMsg,
            Dictionary<(string FatherName, string DatasetName), List<object>> _values,
            List<int> _tmp_msgs_counter)
        {
            lock (_syncRoot)
            {

                if (_outputFileName == string.Empty || _disposed)
                {
                    return;
                }

                bool fileSizeAndDiskQuota = CheckFileSizeAndDiskQuota();
                if (!fileSizeAndDiskQuota)
                {
                    return;
                }

                if (_hdf == null)
                {
                    _hdf = new Hdf5FloatGrowingDataset(_outputFileName);
                }

                Console.WriteLine($"Flushing HDF5 file. Size: {_linuxTimesStatusMsg.Count}. tmp_counter: {_tmp_msgs_counter.Count}");

                //for (int i = 1; i < _tmp_msgs_counter.Count; i++)
                //{
                //    if (_tmp_msgs_counter[i] != _tmp_msgs_counter[i - 1] + 1)
                //    {
                //        Console.WriteLine($"Gap between {_tmp_msgs_counter[i - 1]} and {_tmp_msgs_counter[i]}");
                //    }
                //}
                if (_linuxTimesStatusMsg.Count > 0)
                {
                    _hdf?.AddValues(("Status", "TimeStamp"), _linuxTimesStatusMsg, H5T.STD_I64LE);
                }

                if (_linuxTimesControlMsg.Count > 0)
                {
                    _hdf?.AddValues(("Control", "TimeStamp"), _linuxTimesControlMsg, H5T.STD_I64LE);
                }
                if (_linuxTimesMetryMsg.Count > 0)
                {
                    _hdf?.AddValues(("Metry", "TimeStamp"), _linuxTimesMetryMsg, H5T.STD_I64LE);
                }

                foreach (var kvp in _values)
                {
                    if (kvp.Value.Count > 0)
                    {
                        _hdf?.AddValues(kvp.Key, kvp.Value, H5T.IEEE_F32LE);
                    }
                }
                _linuxTimesStatusMsg.Clear();
                _linuxTimesControlMsg.Clear();
                _linuxTimesMetryMsg.Clear();
                _values.Clear();
                _tmp_msgs_counter.Clear();

                _hdf?.Dispose();
                _hdf = null;
            }
        }

        private bool CheckFileSizeAndDiskQuota()
        {
            // 5 GB in bytes
            const long minFreeSpace = 5L * 1024 * 1024 * 1024;
            string driveRoot = Path.GetPathRoot(_outputFileName)!;
            var driveInfo = new DriveInfo(driveRoot);
            if (driveInfo.AvailableFreeSpace < minFreeSpace)
            {
                Console.WriteLine($"Not enough disk space on {driveRoot}. At least 5 GB required.");
                return false;
            }

            // 500 GB in bytes
            const long maxSize = 500L * 1024 * 1024 * 1024;
            if (File.Exists(_outputFileName))
            {
                var fileInfo = new FileInfo(_outputFileName);
                if (fileInfo.Length > maxSize)
                {
                    Console.WriteLine($"File {_outputFileName} is larger than 500 GB. Not flushing to file.");
                    return false;
                }
            }

            return true;
        }


        private void ZipOutputFile()
    {
            lock (_syncRoot)
            {
                if (string.IsNullOrEmpty(_outputFileName) || !File.Exists(_outputFileName))
                    return;

                string zipPath = Path.ChangeExtension(_outputFileName, ".zip");
                Console.WriteLine($"Zipping file to {zipPath}");

                // Delete zip if it already exists to avoid exceptions
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                using (FileStream zipToOpen = new FileStream(zipPath, FileMode.Create))
                using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(_outputFileName, Path.GetFileName(_outputFileName), CompressionLevel.Optimal);
                }

                File.Delete(_outputFileName);
                _outputFileName = string.Empty; // Clear the output file name after zipping
            }
    }

    static string ReplaceLastSegment(string? path, string newLastSegment)
        {
            // 1️⃣  Trim any trailing slash so GetDirectoryName works
            path = path!.TrimEnd(Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar);

            // 2️⃣  Parent folder = everything except the last segment
            string? parent = Path.GetDirectoryName(path);
            if (parent is null)
                throw new ArgumentException("Path must contain at least one directory.", nameof(path));

            // 3️⃣  Re-combine with the replacement directory name
            return Path.Combine(parent, newLastSegment);
        }
    }
}
