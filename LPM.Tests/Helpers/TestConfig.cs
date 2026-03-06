using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace LPM.Tests.Helpers;

/// <summary>
/// Provides a minimal IConfiguration that points services to a specific SQLite file path.
/// Implemented as an inline class so no extra NuGet packages are required beyond what
/// the main LPM project already brings in transitively.
/// </summary>
public static class TestConfig
{
    public static IConfiguration For(string dbPath) => new FlatConfig(dbPath);

    // -------------------------------------------------------------------------
    // Minimal IConfiguration implementation
    // -------------------------------------------------------------------------

    private sealed class FlatConfig : IConfiguration
    {
        private readonly Dictionary<string, string?> _data;

        public FlatConfig(string dbPath)
        {
            _data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Database:Path"] = dbPath
            };
        }

        public string? this[string key]
        {
            get => _data.TryGetValue(key, out var v) ? v : null;
            set => _data[key] = value;
        }

        public IConfigurationSection GetSection(string key) => new FlatSection(_data, key);

        public IEnumerable<IConfigurationSection> GetChildren() =>
            _data.Keys.Select(k => GetSection(k));

        public IChangeToken GetReloadToken() => new EmptyChangeToken();
    }

    private sealed class FlatSection : IConfigurationSection
    {
        private readonly Dictionary<string, string?> _data;
        private readonly string _prefix;

        public FlatSection(Dictionary<string, string?> data, string prefix)
        {
            _data   = data;
            _prefix = prefix;
        }

        public string Key   => _prefix.Split(':').Last();
        public string Path  => _prefix;
        public string? Value
        {
            get => _data.TryGetValue(_prefix, out var v) ? v : null;
            set => _data[_prefix] = value;
        }

        public string? this[string key]
        {
            get => _data.TryGetValue($"{_prefix}:{key}", out var v) ? v : null;
            set => _data[$"{_prefix}:{key}"] = value;
        }

        public IConfigurationSection GetSection(string key) =>
            new FlatSection(_data, $"{_prefix}:{key}");

        public IEnumerable<IConfigurationSection> GetChildren() =>
            _data.Keys
                 .Where(k => k.StartsWith(_prefix + ":", StringComparison.OrdinalIgnoreCase))
                 .Select(k => new FlatSection(_data, k));

        public IChangeToken GetReloadToken() => new EmptyChangeToken();
    }

    private sealed class EmptyChangeToken : IChangeToken
    {
        public bool HasChanged        => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) =>
            new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
