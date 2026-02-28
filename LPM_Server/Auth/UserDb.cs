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
            FROM Users
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
            FROM UserRoles ur
            JOIN Roles r ON r.RoleId = ur.RoleId
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
