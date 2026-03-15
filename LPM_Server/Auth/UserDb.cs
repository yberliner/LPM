using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace LPM.Auth;

public class UserDb
{
    private readonly string _connectionString;

    public UserDb(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Add AvatarPath column if missing
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('core_users') WHERE name='AvatarPath'";
        if ((long)cmd.ExecuteScalar()! == 0)
        {
            cmd.CommandText = "ALTER TABLE core_users ADD COLUMN AvatarPath TEXT";
            cmd.ExecuteNonQuery();
        }

        // One-time fix: rename "camela" → "carmela" with corrected password
        cmd.CommandText = "SELECT COUNT(*) FROM core_users WHERE LOWER(Username)='camela'";
        if ((long)cmd.ExecuteScalar()! > 0)
        {
            cmd.CommandText = "UPDATE core_users SET Username='carmela', PasswordHash=@h WHERE LOWER(Username)='camela'";
            cmd.Parameters.AddWithValue("@h", HashPassword("Carmela1992"));
            cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();

            // Fix display name in Persons table as well
            cmd.CommandText = "UPDATE core_persons SET FirstName='Carmela' WHERE LOWER(FirstName)='camela'";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Returns the relative avatar URL (e.g. "/avatars/tami.png"), or null.</summary>
    public string? GetAvatarPath(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT AvatarPath FROM core_users WHERE Username = @u COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@u", username);
        var result = cmd.ExecuteScalar();
        return result is DBNull || result is null ? null : (string)result;
    }

    /// <summary>Saves the relative avatar URL to the database.</summary>
    public void SetAvatarPath(string username, string? path)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET AvatarPath = @p WHERE Username = @u COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@p", (object?)path ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Avatar updated for user '{username}'");
    }

    /// <summary>
    /// Changes the password for the given user.
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    public string? ChangePassword(string username, string currentPwd, string newPwd)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PasswordHash FROM core_users WHERE Username = @u COLLATE NOCASE AND IsActive = 1";
        cmd.Parameters.AddWithValue("@u", username);
        var storedHash = cmd.ExecuteScalar() as string;
        if (storedHash is null) return "User not found.";
        if (!VerifyPbkdf2(storedHash, currentPwd)) return "Current password is incorrect.";

        var newHash = HashPassword(newPwd);
        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE core_users SET PasswordHash = @h WHERE Username = @u COLLATE NOCASE";
        updateCmd.Parameters.AddWithValue("@h", newHash);
        updateCmd.Parameters.AddWithValue("@u", username);
        updateCmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Password changed for user '{username}'");
        return null;
    }

    private static string HashPassword(string password)
    {
        const int iterations = 260000;
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        using var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        byte[] hash = kdf.GetBytes(32);
        return $"pbkdf2_sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Returns true if credentials are valid and the user is active.
    /// roles is populated with all role codes assigned to the user.
    /// </summary>
    public bool ValidateUser(string username, string password, out List<string> roles)
    {
        roles = new List<string>();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Fetch user record
        using var userCmd = conn.CreateCommand();
        userCmd.CommandText = @"
            SELECT Id, PasswordHash, IsActive
            FROM core_users
            WHERE Username = @u COLLATE NOCASE";
        userCmd.Parameters.AddWithValue("@u", username);

        int userId;
        string storedHash;
        bool isActive;

        using (var r = userCmd.ExecuteReader())
        {
            if (!r.Read()) return false;
            userId     = r.GetInt32(0);
            storedHash = r.GetString(1);
            isActive   = r.GetInt32(2) == 1;
        }

        if (!isActive) return false;
        if (!VerifyPbkdf2(storedHash, password)) return false;

        // Fetch assigned roles
        using var roleCmd = conn.CreateCommand();
        roleCmd.CommandText = @"
            SELECT r.Code
            FROM core_user_roles ur
            JOIN lkp_roles r ON r.RoleId = ur.RoleId
            WHERE ur.UserId = @id";
        roleCmd.Parameters.AddWithValue("@id", userId);

        using var rr = roleCmd.ExecuteReader();
        while (rr.Read())
            roles.Add(rr.GetString(0));

        return roles.Count > 0;
    }

    // Verifies a PBKDF2-SHA256 hash in the format:
    //   pbkdf2_sha256$<iterations>$<base64-salt>$<base64-hash>
    private static bool VerifyPbkdf2(string storedHash, string password)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2_sha256") return false;
        if (!int.TryParse(parts[1], out int iterations)) return false;

        byte[] salt     = Convert.FromBase64String(parts[2]);
        byte[] expected = Convert.FromBase64String(parts[3]);

        using var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        byte[] actual = kdf.GetBytes(expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
