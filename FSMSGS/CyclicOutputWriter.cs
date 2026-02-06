using System.Text;

namespace MSGS
{
    public class CyclicOutputWriter : TextWriter
    {
        private const int MaxLogFiles = 50;
        private const string LogDirName = "Log";
        private const string LogFilePrefix = "app_errors_";
        private const string LogFileSuffix = ".log";

        private readonly TextWriter _consoleWriter;
        private readonly TextWriter _fileWriter;
        private readonly string _logFilePath;

        private StringBuilder _lineBuffer = new();
        private bool _isNewLine = true;


        public override Encoding Encoding => Encoding.UTF8;

        public CyclicOutputWriter(TextWriter consoleWriter)
        {
            _consoleWriter = consoleWriter;

            // Create Log directory if it doesn't exist
            var logDir = Path.Combine(Directory.GetCurrentDirectory(), LogDirName);
            Directory.CreateDirectory(logDir);

            // Get next available log file number
            int fileNumber = GetNextFileNumber(logDir);
            _logFilePath = Path.Combine(logDir, $"{LogFilePrefix}{fileNumber}{LogFileSuffix}");

            // Create or overwrite the log file
            _fileWriter = new StreamWriter(_logFilePath, false, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        private int GetNextFileNumber(string logDir)
        {
            var existingFiles = Directory.GetFiles(logDir, $"{LogFilePrefix}*{LogFileSuffix}")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Select(f =>
                {
                    if (int.TryParse(f.Replace(LogFilePrefix, ""), out int num))
                        return num;
                    return -1;
                })
                .Where(n => n > 0)
                .ToList();

            if (!existingFiles.Any())
                return 1;

            // Find the most recently modified file
            var lastFile = Directory.GetFiles(logDir, $"{LogFilePrefix}*{LogFileSuffix}")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .First();

            var lastNumber = int.Parse(Path.GetFileNameWithoutExtension(lastFile)
                .Replace(LogFilePrefix, ""));

            // Increment and cycle back to 1 if exceeding MaxLogFiles
            return (lastNumber % MaxLogFiles) + 1;
        }

        private void WriteTimestampIfNeeded()
        {
            if (_isNewLine)
            {
                _fileWriter.Write($"{DateTime.Now:dd-MM-yy}\t{DateTime.Now:HH:mm:ss}\t");
                _isNewLine = false;
            }
        }

        public override void Write(char value)
        {
            _consoleWriter.Write(value);

            if (value == '\n')
            {
                _fileWriter.Write(value);
                _isNewLine = true;
            }
            else if (value == '\r')
            {
                _fileWriter.Write(value);
            }
            else
            {
                WriteTimestampIfNeeded();
                _fileWriter.Write(value);
            }
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                _consoleWriter.Write(value);
                return;
            }

            _consoleWriter.Write(value);

            var lines = value.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    _fileWriter.WriteLine();
                    _isNewLine = true;
                }

                if (!string.IsNullOrEmpty(lines[i]))
                {
                    WriteTimestampIfNeeded();
                    _fileWriter.Write(lines[i]);
                }
            }
        }

        public override void WriteLine(string? value)
        {
            _consoleWriter.WriteLine(value);

            if (value != null)
            {
                WriteTimestampIfNeeded();
                _fileWriter.WriteLine(value);
            }
            else
            {
                WriteTimestampIfNeeded();
                _fileWriter.WriteLine();
            }
            _isNewLine = true;
        }

        public override void WriteLine()
        {
            _consoleWriter.WriteLine();
            _fileWriter.WriteLine();
            _isNewLine = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fileWriter.Dispose();
                // Don't dispose console writer as it's managed externally
            }
            base.Dispose(disposing);
        }
    }
}