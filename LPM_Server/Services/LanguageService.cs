using System.Text.Json;

namespace LPM.Services;

public class LanguageService
{
    private Dictionary<string, string> _translations = new();
    private string _currentLanguage = "en";
    private readonly string _wwwRootPath;

    public event Action? OnLanguageChanged;
    public string CurrentLanguage => _currentLanguage;
    public bool IsRtl => _currentLanguage == "he";

    public LanguageService(IWebHostEnvironment env)
    {
        _wwwRootPath = env.WebRootPath ?? "";
        LoadLanguage("en");
    }

    public void SetLanguage(string lang)
    {
        if (lang == _currentLanguage) return;
        if (LoadLanguage(lang))
            OnLanguageChanged?.Invoke();
    }

    public string T(string key) =>
        _translations.TryGetValue(key, out var val) ? val : key;

    private bool LoadLanguage(string lang)
    {
        var path = Path.Combine(_wwwRootPath, "i18n", $"{lang}.json");
        if (!File.Exists(path)) return false;
        try
        {
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict == null) return false;
            _translations = dict;
            _currentLanguage = lang;
            return true;
        }
        catch { return false; }
    }
}
