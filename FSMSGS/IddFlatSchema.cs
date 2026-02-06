using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MSGS
{
    public class IddFlatSchema
    {
        // --------------------------
        //  Internal data structures
        // --------------------------

        private class FlatRoot
        {
            [JsonPropertyName("structs")]
            public Dictionary<string, List<string>> Structs { get; set; } = new();
        }

        private class CacheEntry
        {
            public Dictionary<string, List<string>> Structs { get; init; } = new(StringComparer.Ordinal);
            public Dictionary<string, string> NameMapExact { get; init; } = new(StringComparer.Ordinal);
            public Dictionary<string, string> NameMapIgnoreCase { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }

        // --------------------------
        //  Instance fields
        // --------------------------

        private readonly CacheEntry _cache;

        // --------------------------
        //  Construction
        // --------------------------

        /// <summary>
        /// Initializes the schema from a JSON string (not a file path).
        /// </summary>
        public IddFlatSchema(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
                throw new ArgumentException("JSON content must not be null or empty.", nameof(jsonContent));

            _cache = ParseFromJson(jsonContent);
        }

        // --------------------------
        //  Public API
        // --------------------------

        /// <summary>
        /// Gets all struct names defined in this schema.
        /// </summary>
        public IReadOnlyCollection<string> StructNames
            => new List<string>(_cache.Structs.Keys);

        /// <summary>
        /// Checks whether <paramref name="obj"/> is one of the structs in this schema,
        /// and if so returns all flattened variable paths for it.
        /// </summary>
        public bool TryGetVariablePaths(object? obj, out IReadOnlyList<string> paths)
        {
            paths = Array.Empty<string>();
            if (obj is null) return false;

            var type = obj.GetType();
            if (type.IsByRef)
                type = type.GetElementType() ?? type;

            var name = type.Name;

            if (!_cache.NameMapExact.TryGetValue(name, out var key) &&
                !_cache.NameMapIgnoreCase.TryGetValue(name, out key))
            {
                return false;
            }

            if (_cache.Structs.TryGetValue(key, out var list))
            {
                paths = list;
                return true;
            }

            return false;
        }

        // --------------------------
        //  Internal helpers
        // --------------------------

        private static CacheEntry ParseFromJson(string json)
        {
            var doc = JsonSerializer.Deserialize<FlatRoot>(json) ?? new FlatRoot();

            var exact = new Dictionary<string, string>(StringComparer.Ordinal);
            var ignore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in doc.Structs.Keys)
            {
                exact[key] = key;
                ignore[key] = key;
            }

            return new CacheEntry
            {
                Structs = doc.Structs,
                NameMapExact = exact,
                NameMapIgnoreCase = ignore
            };
        }
    }

}