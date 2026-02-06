using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public class AbsoluteDefaultsProvider
{
    public Dictionary<string, string?[]> AbsoluteDefaults_0 { get; private set; } = new();

    public string LoadFromJson(string fileName = "absolute_defaults.json")
    {
        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
        Path.Combine(baseDir, fileName),                    // bin/.../absolute_defaults.json
        Path.Combine(baseDir, "Pages", fileName),           // bin/.../Pages/absolute_defaults.json  ← your case
        Path.Combine(baseDir, "wwwroot", fileName)          // if you later move it to wwwroot
    };

        var fullPath = Array.Find(candidates, File.Exists);
        if (fullPath is null)
        {
            Console.WriteLine("[AbsoluteDefaults] File not found. Tried:\n" + string.Join("\n", candidates));
            return string.Empty;
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            AbsoluteDefaults_0 = JsonSerializer.Deserialize<Dictionary<string, string?[]>>(json, options)
                                 ?? new Dictionary<string, string?[]>();
            Console.WriteLine($"[AbsoluteDefaults] Loaded {AbsoluteDefaults_0.Count} entries from {fullPath}.");
            return json;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AbsoluteDefaults] Error reading JSON at {fullPath}: {ex.Message}");
        }
        return string.Empty;
    }

}
