using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MSGS
{
    public class EMBVersionStorage
    {
        private const string FILE_NAME = "emb_versions.json";
        private readonly string _filePath;

        // machineName -> (device -> version)
        private Dictionary<string, Dictionary<DevicesScreen, cidd_version>> _versions =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly object _lock = new();

        public EMBVersionStorage()
        {
            _filePath = Path.Combine(Directory.GetCurrentDirectory(), FILE_NAME);
            EnsureFileExists();
            Load();
        }

        // -----------------------------
        // PUBLIC API (thread safe)
        // -----------------------------
        public cidd_version GetVersion(string machineName, DevicesScreen device, cidd_version defaultVersion)
        {
            if (machineName == null)
            {
                Console.WriteLine("EMBVersionStorage: Cannot get version for null machine name");
                return defaultVersion;
            }
            lock (_lock)
            {
                if (!_versions.TryGetValue(machineName, out var deviceMap))
                {
                    deviceMap = new();
                    deviceMap[device] = defaultVersion;
                    _versions[machineName] = deviceMap;
                    SaveLocked(device);
                    return defaultVersion;
                }

                if (!deviceMap.TryGetValue(device, out var version))
                {
                    deviceMap[device] = defaultVersion;
                    SaveLocked(device);
                    return defaultVersion;
                }

                return version;
            }
        }

        public void SetVersion(string machineName, DevicesScreen device, cidd_version version)
        {
            if (machineName == null)
            {
                Console.WriteLine("EMBVersionStorage: Cannot set version for null machine name");
                return;
            }
            Console.WriteLine($"EmbVersionFinder: Success finding version");
            Console.WriteLine($"EMBVersionStorage: Setting version for machine '{machineName}', device '{device}' to {version}");

            lock (_lock)
            {
                if (!_versions.TryGetValue(machineName, out var deviceMap))
                {
                    deviceMap = new();
                    _versions[machineName] = deviceMap;
                }

                deviceMap[device] = version;
                SaveLocked(device, true);
            }
        }

        // -----------------------------
        // INTERNAL HELPERS
        // -----------------------------
        private void EnsureFileExists()
        {
            // Called only from ctor, but safe even if called multiple times
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, "{}"); // empty JSON object
            }
        }

        private void Load()
        {
            // Called only from ctor, so no need to lock
            try
            {
                string json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<
                    Dictionary<string, Dictionary<DevicesScreen, cidd_version>>
                >(json, _options);

                _versions = data != null
                    ? new Dictionary<string, Dictionary<DevicesScreen, cidd_version>>(data, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, Dictionary<DevicesScreen, cidd_version>>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                Console.WriteLine("EMBVersionStorage: Failed to load version data, starting fresh.");
                _versions = new(StringComparer.OrdinalIgnoreCase);
            }
        }

        // Must be called only under _lock
        private void SaveLocked(DevicesScreen device, bool forcePrint = false)
        {
            var json = JsonSerializer.Serialize(_versions, _options);
            File.WriteAllText(_filePath, json);

            if (forcePrint || device == DevicesScreen.MC_FAST)
            {
                Console.WriteLine($"===== JSON Version File Saved — device {device} =====");
                Console.WriteLine(json);
                Console.WriteLine("================================");
            }
        }
    }
}
