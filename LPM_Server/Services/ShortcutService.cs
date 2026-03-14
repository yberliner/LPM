using Microsoft.Data.Sqlite;

namespace LPM.Services;

public class ShortcutService
{
    private readonly string _connectionString;

    public ShortcutService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>Returns all shortcuts keyed by KeyChar.</summary>
    public Dictionary<string, string> GetShortcuts()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT KeyChar, Text FROM lkp_shortcuts";
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = r.GetString(0);
            var text = r.IsDBNull(1) ? "" : r.GetString(1);
            dict[key] = text;
        }
        return dict;
    }

    /// <summary>Upsert a shortcut.</summary>
    public void SaveShortcut(string keyChar, string text)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        if (string.IsNullOrEmpty(text))
        {
            using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM lkp_shortcuts WHERE KeyChar = @key";
            del.Parameters.AddWithValue("@key", keyChar);
            del.ExecuteNonQuery();
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO lkp_shortcuts (KeyChar, Text)
                VALUES (@key, @text)
                ON CONFLICT(KeyChar) DO UPDATE SET Text = @text";
            cmd.Parameters.AddWithValue("@key", keyChar);
            cmd.Parameters.AddWithValue("@text", text);
            cmd.ExecuteNonQuery();
        }
    }
}
