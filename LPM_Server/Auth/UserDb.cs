using Microsoft.Data.Sqlite;
using BC = BCrypt.Net.BCrypt;

namespace LPM.Auth;

public class UserDb
{
    private readonly string _connectionString;

    public UserDb(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "users.db";
        _connectionString = $"Data Source={dbPath}";
        EnsureCreated();
    }

    private void EnsureCreated()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Users (
                Id       INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                PassHash TEXT    NOT NULL,
                Role     TEXT    NOT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    public bool ValidateUser(string username, string password, out string role)
    {
        role = "";
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PassHash, Role FROM Users WHERE Username = @u COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@u", username);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return false;

        var hash = reader.GetString(0);
        role = reader.GetString(1);
        return BC.Verify(password, hash);
    }

    public void EnsureSeedUser(string username, string password, string role)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = @u COLLATE NOCASE";
        check.Parameters.AddWithValue("@u", username);
        if ((long)check.ExecuteScalar()! > 0) return;

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO Users (Username, PassHash, Role) VALUES (@u, @h, @r)";
        insert.Parameters.AddWithValue("@u", username);
        insert.Parameters.AddWithValue("@h", BC.HashPassword(password));
        insert.Parameters.AddWithValue("@r", role);
        insert.ExecuteNonQuery();

        Console.WriteLine($"[UserDb] Seeded user '{username}' with role '{role}'");
    }
}
