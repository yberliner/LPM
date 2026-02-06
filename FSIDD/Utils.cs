using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;

namespace MSGS
{
    public class Utils
    {
        public static void ToggleLedCaption(ref string caption)
        {
            caption = caption == "Start" ? "Stop" : "Start";
        }
        public static sLedPattern GetLedPattern(string color, string interval, string caption)
        {
            int colorIndex = GetLastDigit(color);
            int intervalIndex = GetLastDigit(interval);

            return new sLedPattern
            {
                LedColorPattern = caption == "Start"? (eLedColorPattern)colorIndex : 0,
                LedIntervalPattern = caption == "Start" ? (eLedIntervalPattern)intervalIndex : 0
            };
        }

        private static int GetLastDigit(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("Invalid char at GetLastDigit for light change");
                return 0; // default to 0 if invalid
            }
            char lastChar = input[^1]; // C# index from end
            return char.IsDigit(lastChar) ? lastChar - '0' : 1;
        }

        public static double ConvertU16ToDouble(ushort sample)
        {
            byte lsb = (byte)(sample & 0xFF);       // low byte
            byte msb = (byte)((sample >> 8) & 0xFF); // high byte

            return (double)((double)lsb + ((double)msb / 100f));
        }

        private static readonly Random rng = new Random();
        public static byte GetRandomByte()
        {
            // Random.Next(minValue, maxValue) is inclusive of minValue, exclusive of maxValue
            return (byte)rng.Next(0, 256);
        }

        public static string[] IncrementPatterns(string[] patterns)
        {
            for (int i = 0; i < patterns.Length; i++)
            {
                string value = patterns[i];

                // Case 1: "Pattern X" -> increment number
                //if (value.StartsWith("Pattern", StringComparison.OrdinalIgnoreCase))
                //{
                //    string[] parts = value.Split(' ');
                //    if (parts.Length == 2 && int.TryParse(parts[1], out int number))
                //    {
                //        number++;
                //        if (number > 9)
                //            number = 0;

                //        patterns[i] = $"{parts[0]} {number}";
                //    }
                //}
                //else
                //{
                    // Case 2: Look inside LedColorPatternNames
                    int idx = Array.IndexOf(LedIniLoader.LedColorPatternNames, value);
                    if (idx >= 0)
                    {
                        int nextIdx = (idx + 1) % LedIniLoader.LedColorPatternNames.Length;
                        patterns[i] = LedIniLoader.LedColorPatternNames[nextIdx];
                        continue;
                    }

                    // Case 3: Look inside LedIntervalPatternNames
                    idx = Array.IndexOf(LedIniLoader.LedIntervalPatternNames, value);
                    if (idx >= 0)
                    {
                        int nextIdx = (idx + 1) % LedIniLoader.LedIntervalPatternNames.Length;
                        patterns[i] = LedIniLoader.LedIntervalPatternNames[nextIdx];
                        continue;
                    }

                    // Case 4: Not found -> leave unchanged
                //}
            }

            return patterns;
        }


        public static class LedIniLoader
        {
            public static string[] LedColorPatternNames { get; private set; } = Array.Empty<string>();
            public static string[] LedIntervalPatternNames { get; private set; } = Array.Empty<string>();

            // Reads: 10 sRgbColor from [LedColorPatterns] and 10 sLedInterval from [LedIntervalPatterns]
            public static void Load(string iniPath, out sRgbColor[] ledColors, out sLedInterval[] ledIntervals)
            {
                if (!File.Exists(iniPath))
                {
                    Console.WriteLine($"LedIniLoader.Load:: Error Exception in Load led patterns. Dir: {iniPath}");
                    ledColors = new sRgbColor[10];
                    ledIntervals = new sLedInterval[10];
                    LedColorPatternNames = Enumerable.Range(1, 10).Select(i => $"unknown {i}").ToArray();
                    LedIntervalPatternNames = Enumerable.Range(1, 10).Select(i => $"unknown {i}").ToArray();
                    return;
                }

                Console.WriteLine($"LedIniLoader.Load:: Loading led patterns from INI: {iniPath}");

                // Parse INI into a flat dictionary: "<Section>:<key>" -> value
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? section = null;

                foreach (var rawLine in File.ReadAllLines(iniPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    var eq = line.IndexOf('=');
                    if (eq <= 0 || section == null) continue;

                    var key = line.Substring(0, eq).Trim();
                    var val = line.Substring(eq + 1).Trim();
                    map[$"{section}:{key}"] = val;
                }

                // Allocate outputs
                ledColors = new sRgbColor[10];
                ledIntervals = new sLedInterval[10];
                LedColorPatternNames = new string[10];
                LedIntervalPatternNames = new string[10];

                // Helpers
                static byte GetByteOrDefault(Dictionary<string, string> m, string k, byte def = 0)
                    => m.TryGetValue(k, out var s) && byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : def;

                static string GetStringOrDefault(Dictionary<string, string> m, string k, string def = "")
                    => m.TryGetValue(k, out var s) ? s : def;

                // Colors
                for (int i = 0; i < 10; i++)
                {
                    ledColors[i] = new sRgbColor
                    {
                        Red = GetByteOrDefault(map, $"LedColorPatterns:color_pattern_{i}/r"),
                        Green = GetByteOrDefault(map, $"LedColorPatterns:color_pattern_{i}/g"),
                        Blue = GetByteOrDefault(map, $"LedColorPatterns:color_pattern_{i}/b"),
                    };

                    string name = GetStringOrDefault(map, $"LedColorPatterns:color_pattern_{i}/name", "unknown");
                    LedColorPatternNames[i] = $"{name} {i}";
                }

                // Intervals
                for (int i = 0; i < 10; i++)
                {
                    ledIntervals[i] = new sLedInterval
                    {
                        Rising = GetByteOrDefault(map, $"LedIntervalPatterns:interval_pattern_{i}/rising"),
                        High = GetByteOrDefault(map, $"LedIntervalPatterns:interval_pattern_{i}/high"),
                        Falling = GetByteOrDefault(map, $"LedIntervalPatterns:interval_pattern_{i}/falling"),
                        Low = GetByteOrDefault(map, $"LedIntervalPatterns:interval_pattern_{i}/low"),
                        ScaleMs = GetByteOrDefault(map, $"LedIntervalPatterns:interval_pattern_{i}/scale_ms"),
                    };

                    string name = GetStringOrDefault(map, $"LedIntervalPatterns:interval_pattern_{i}/name", "unknown");
                    LedIntervalPatternNames[i] = $"{name} {i}";
                }
            }
        }


    }
}
