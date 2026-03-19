using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace LPM.Auth;

public record UserListItem(int Id, int PersonId, string Username, string FullName,
    string StaffRole, string UserType, bool IsActive, string? GradeCode, bool AllowAll);

public record UserDetail(int Id, int PersonId, string Username, string StaffRole,
    string UserType, bool IsActive, int? GradeId, bool AllowAll);

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

    /// <summary>
    /// Admin reset: sets a new password without requiring the current one.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public string? AdminResetPassword(int userId, string newPwd)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM core_users WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        if ((long)cmd.ExecuteScalar()! == 0) return "User not found.";

        cmd.CommandText = "UPDATE core_users SET PasswordHash = @h WHERE Id = @id";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@h", HashPassword(newPwd));
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Password reset by admin for userId={userId}");
        return null;
    }

    /// <summary>
    /// Validates credentials. On success populates roles ("Admin" if UserType='Admin')
    /// and staffRole (the user's StaffRole value). Returns false if invalid or inactive.
    /// </summary>
    public bool ValidateUser(string username, string password, out List<string> roles, out string staffRole)
    {
        roles = new List<string>();
        staffRole = "None";
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var userCmd = conn.CreateCommand();
        userCmd.CommandText = @"
            SELECT Id, PasswordHash, IsActive, UserType, StaffRole
            FROM core_users
            WHERE Username = @u COLLATE NOCASE";
        userCmd.Parameters.AddWithValue("@u", username);

        string storedHash;
        bool isActive;
        string userType;

        using (var r = userCmd.ExecuteReader())
        {
            if (!r.Read()) return false;
            storedHash = r.GetString(1);
            isActive   = r.GetInt32(2) == 1;
            userType   = r.GetString(3);
            staffRole  = r.GetString(4);
        }

        if (!isActive) return false;
        if (!VerifyPbkdf2(storedHash, password)) return false;

        if (userType == "Admin")
            roles.Add("Admin");

        return true;
    }

    public int? GetPersonId(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PersonId FROM core_users WHERE Username = @u COLLATE NOCASE AND IsActive = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username);
        var result = cmd.ExecuteScalar();
        return result is long v ? (int)v : null;
    }

    // ── User management (Admin UI) ──────────────────────────────────────────

    public List<UserListItem> GetAllUsers()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.Id, u.PersonId, u.Username,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   u.StaffRole, u.UserType, u.IsActive, g.Code, u.AllowAll
            FROM core_users u
            JOIN core_persons p ON p.PersonId = u.PersonId
            LEFT JOIN lkp_grades g ON g.GradeId = u.GradeId
            ORDER BY p.FirstName, u.Username";
        var list = new List<UserListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UserListItem(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetInt32(6) != 0,
                r.IsDBNull(7) ? null : r.GetString(7), r.GetInt32(8) != 0));
        return list;
    }

    public UserDetail? GetUserDetail(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, PersonId, Username, StaffRole, UserType, IsActive, GradeId, AllowAll
            FROM core_users WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new UserDetail(
            r.GetInt32(0), r.GetInt32(1), r.GetString(2),
            r.GetString(3), r.GetString(4), r.GetInt32(5) != 0,
            r.IsDBNull(6) ? null : r.GetInt32(6), r.GetInt32(7) != 0);
    }

    /// <summary>Creates a new user. Returns the new user Id, or throws on duplicate username.</summary>
    public int CreateUser(int personId, string username, string password,
        string staffRole, string userType, int? gradeId, bool allowAll)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO core_users (PersonId, Username, PasswordHash, StaffRole, UserType, GradeId, AllowAll, IsActive)
            VALUES (@pid, @u, @h, @sr, @ut, @gid, @aa, 1)";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@u", username.Trim());
        cmd.Parameters.AddWithValue("@h", HashPassword(password));
        cmd.Parameters.AddWithValue("@sr", staffRole);
        cmd.Parameters.AddWithValue("@ut", userType);
        cmd.Parameters.AddWithValue("@gid", gradeId.HasValue ? (object)gradeId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@aa", allowAll ? 1 : 0);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid()";
        cmd.Parameters.Clear();
        var newId = (int)(long)cmd.ExecuteScalar()!;
        Console.WriteLine($"[UserDb] Created user '{username.Trim()}' (Id={newId}, StaffRole={staffRole})");
        return newId;
    }

    public void UpdateUser(int userId, string staffRole, string userType, int? gradeId, bool allowAll, bool isActive)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE core_users
            SET StaffRole=@sr, UserType=@ut, GradeId=@gid, AllowAll=@aa, IsActive=@active
            WHERE Id=@id";
        cmd.Parameters.AddWithValue("@sr", staffRole);
        cmd.Parameters.AddWithValue("@ut", userType);
        cmd.Parameters.AddWithValue("@gid", gradeId.HasValue ? (object)gradeId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@aa", allowAll ? 1 : 0);
        cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Updated user Id={userId} StaffRole={staffRole} UserType={userType} IsActive={isActive}");
    }

    public void UpdateUsername(int userId, string newUsername)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET Username=@u WHERE Id=@id";
        cmd.Parameters.AddWithValue("@u", newUsername.Trim());
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Username updated for Id={userId} → '{newUsername.Trim()}'");
    }

    public void DeactivateUser(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET IsActive=0 WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Deactivated user Id={userId}");
    }

    public bool UsernameExists(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM core_users WHERE Username = @u COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@u", username.Trim());
        return (long)cmd.ExecuteScalar()! > 0;
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static string HashPassword(string password)
    {
        const int iterations = 260000;
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        using var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        byte[] hash = kdf.GetBytes(32);
        return $"pbkdf2_sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
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
