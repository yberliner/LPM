using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using OtpNet;

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

        // Add ContactConfirmed column if missing (DEFAULT 1 = already confirmed; 0 = must fill in on first login)
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('core_users') WHERE name='ContactConfirmed'";
        if ((long)cmd.ExecuteScalar()! == 0)
        {
            cmd.CommandText = "ALTER TABLE core_users ADD COLUMN ContactConfirmed INTEGER NOT NULL DEFAULT 1";
            cmd.ExecuteNonQuery();
        }

        // Mark TOTP columns as deprecated (replaced by passkeys + email verification)
        cmd.CommandText = "UPDATE core_users SET TotpEnabled = -999, TotpSecret = 'DEPRECATED', Require2FA = -999, ContactConfirmed = 1 WHERE TotpEnabled != -999";
        cmd.ExecuteNonQuery();

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
        bool currentOk = VerifyPbkdf2(storedHash, currentPwd);
        if (!currentOk && currentPwd.Length > 0)
        {
            var toggled = char.IsUpper(currentPwd[0])
                ? char.ToLower(currentPwd[0]) + currentPwd[1..]
                : char.ToUpper(currentPwd[0]) + currentPwd[1..];
            currentOk = VerifyPbkdf2(storedHash, toggled);
        }
        if (!currentOk) return "Current password is incorrect.";

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
        staffRole = StaffRoles.None;
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

        bool verified = VerifyPbkdf2(storedHash, password);
        if (!verified && password.Length > 0)
        {
            var toggled = char.IsUpper(password[0])
                ? char.ToLower(password[0]) + password[1..]
                : char.ToUpper(password[0]) + password[1..];
            verified = VerifyPbkdf2(storedHash, toggled);
        }
        if (!verified) return false;

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

    /// <summary>
    /// Returns IsActive flag and first 12 chars of PasswordHash for auto-login cookie validation.
    /// If the user is not found, returns null.
    /// </summary>
    public (bool IsActive, string PwdHashPrefix)? GetAutoLoginInfo(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IsActive, PasswordHash FROM core_users WHERE Id=@id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var isActive = r.GetInt32(0) == 1;
        var pwdHash  = r.GetString(1);
        return (isActive, pwdHash[..Math.Min(12, pwdHash.Length)]);
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
            INSERT INTO core_users (PersonId, Username, PasswordHash, StaffRole, UserType, GradeId, AllowAll, IsActive, MustChangePassword, ContactConfirmed, TotpEnabled, TotpSecret, Require2FA)
            VALUES (@pid, @u, @h, @sr, @ut, @gid, @aa, 1, 1, 0, -999, 'DEPRECATED', -999)";
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

    // ── Login Flags ─────────────────────────────────────────────────────────

    public record LoginFlags(int UserId, string Username, bool MustChangePassword, List<string> Roles, string StaffRole, int PersonId);

    /// <summary>Returns login flags for a user by username. Call only after ValidateUser succeeds.</summary>
    public LoginFlags? GetLoginFlags(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.Id, u.Username, u.MustChangePassword, u.UserType, u.StaffRole, u.PersonId
            FROM core_users u
            WHERE u.Username = @u COLLATE NOCASE AND u.IsActive = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var userType  = r.GetString(3);
        var staffRole = r.GetString(4);
        var roles = new List<string>();
        if (userType == "Admin")       roles.Add("Admin");
        else if (staffRole != StaffRoles.Solo)  roles.Add("Customer");
        return new LoginFlags(r.GetInt32(0), r.GetString(1), r.GetInt32(2) == 1, roles, staffRole, r.GetInt32(5));
    }

    /// <summary>Same as GetLoginFlags but looks up by UserId (core_users.Id).</summary>
    public LoginFlags? GetLoginFlagsById(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.Id, u.Username, u.MustChangePassword, u.UserType, u.StaffRole, u.PersonId
            FROM core_users u
            WHERE u.Id = @id AND u.IsActive = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var userType  = r.GetString(3);
        var staffRole = r.GetString(4);
        var roles = new List<string>();
        if (userType == "Admin") roles.Add("Admin");
        else if (staffRole != StaffRoles.Solo) roles.Add("Customer");
        return new LoginFlags(r.GetInt32(0), r.GetString(1), r.GetInt32(2) == 1, roles, staffRole, r.GetInt32(5));
    }

    /// <summary>Sets or clears the per-user Require2FA flag (admin use).</summary>
    public void SetRequire2FA(int userId, bool value)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET Require2FA=@v WHERE Id=@id";
        cmd.Parameters.AddWithValue("@v", value ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Require2FA={value} for userId={userId}");
    }

    /// <summary>Forces a password change (no current password required) and clears MustChangePassword.</summary>
    public void ForceSetPassword(int userId, string newPassword)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET PasswordHash=@h, MustChangePassword=0 WHERE Id=@id";
        cmd.Parameters.AddWithValue("@h", HashPassword(newPassword));
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Forced password change for userId={userId}");
    }

    /// <summary>Generates a new TOTP secret, encrypts it, saves it, sets TotpEnabled=1.</summary>
    public string SetupTotp(int userId, string encKey)
    {
        var rawSecret = KeyGeneration.GenerateRandomKey(20); // 160-bit secret
        var encrypted = EncryptTotp(rawSecret, encKey);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET TotpSecret=@s, TotpEnabled=1 WHERE Id=@id";
        cmd.Parameters.AddWithValue("@s", encrypted);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] TOTP enabled for userId={userId}");
        return Base32Encoding.ToString(rawSecret); // return raw base32 for QR
    }

    /// <summary>Generates a new secret without saving — for the setup preview step.</summary>
    public static (byte[] RawSecret, string Base32Secret) GenerateTotpSecret()
    {
        var raw = KeyGeneration.GenerateRandomKey(20);
        return (raw, Base32Encoding.ToString(raw));
    }

    /// <summary>Verifies a 6-digit code against the encrypted secret. Allows ±1 time window.</summary>
    public bool VerifyTotpCode(string encryptedSecret, string code, string encKey)
    {
        try
        {
            var rawSecret = DecryptTotp(encryptedSecret, encKey);
            var totp = new Totp(rawSecret);
            return totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
        }
        catch { return false; }
    }

    /// <summary>Verifies a code against a raw (not yet saved) base32 secret. Used during setup.</summary>
    public static bool VerifyTotpCodeRaw(byte[] rawSecret, string code)
    {
        try
        {
            var totp = new Totp(rawSecret);
            return totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
        }
        catch { return false; }
    }

    /// <summary>Saves an already-encrypted TOTP secret and enables TOTP for the user.</summary>
    public void SaveEncryptedTotpSecret(int userId, string encryptedSecret)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET TotpSecret=@s, TotpEnabled=1 WHERE Id=@id";
        cmd.Parameters.AddWithValue("@s", encryptedSecret);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] TOTP secret saved for userId={userId}");
    }

    /// <summary>Encrypts a raw TOTP secret byte array using AES-256-GCM. Public for use in setup flow.</summary>
    public static string EncryptTotpRaw(byte[] rawSecret, string encKey)
        => EncryptTotp(rawSecret, encKey); // returns base64 string

    public void DisableTotp(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET TotpSecret=NULL, TotpEnabled=0 WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] TOTP disabled for userId={userId}");
    }

    // ── Trusted devices ──────────────────────────────────────────────────────

    /// <summary>Returns the UserId (core_users.Id) that owns this trust token, or null if invalid.</summary>
    public int? GetTrustedDeviceUserId(string token)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT UserId FROM sys_trusted_devices WHERE DeviceToken=@t LIMIT 1";
        cmd.Parameters.AddWithValue("@t", token);
        var result = cmd.ExecuteScalar();
        return result is long v ? (int)v : null;
    }

    public void AddTrustedDevice(int userId, string token)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO sys_trusted_devices (DeviceToken, UserId, CreatedAt) VALUES (@t,@u,@c)";
        cmd.Parameters.AddWithValue("@t", token);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Trusted device added for userId={userId}");
    }

    public void RevokeAllTrustedDevices(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sys_trusted_devices WHERE UserId=@u";
        cmd.Parameters.AddWithValue("@u", userId);
        var n = cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Revoked {n} trusted devices for userId={userId}");
    }

    public int GetTrustedDeviceCount(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys_trusted_devices WHERE UserId=@u";
        cmd.Parameters.AddWithValue("@u", userId);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    // ── Solo auditor management ──────────────────────────────────────────────

    /// <summary>
    /// Activates or creates a Solo auditor account for the given PC.
    /// If a Solo row already exists for pcId, reactivates it.
    /// Otherwise creates a new user: username=first.last, password=First1992.
    /// Returns the username that was used.
    /// </summary>
    public (string Username, string? NewPassword) EnableSoloAuditor(int pcId, string firstName, string lastName)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Check if a Solo row already exists for this PC
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT Id FROM core_users WHERE PersonId = @pid AND StaffRole = 'Solo' LIMIT 1";
        checkCmd.Parameters.AddWithValue("@pid", pcId);
        var existingId = checkCmd.ExecuteScalar();

        // Build initial password (same formula used for new users)
        var fn = firstName.Trim().ToLower();
        var ln = lastName.Trim().ToLower();
        var resetPassword = fn.Length > 0
            ? char.ToUpper(fn[0]) + fn[1..] + "1992"
            : "User1992";

        if (existingId is not null)
        {
            using var reactivateCmd = conn.CreateCommand();
            reactivateCmd.CommandText = "UPDATE core_users SET IsActive = 1, MustChangePassword = 1, PasswordHash = @h WHERE Id = @id";
            reactivateCmd.Parameters.AddWithValue("@h", HashPassword(resetPassword));
            reactivateCmd.Parameters.AddWithValue("@id", existingId);
            reactivateCmd.ExecuteNonQuery();

            using var nameCmd = conn.CreateCommand();
            nameCmd.CommandText = "SELECT Username FROM core_users WHERE Id = @id";
            nameCmd.Parameters.AddWithValue("@id", existingId);
            var username = (string)nameCmd.ExecuteScalar()!;
            Console.WriteLine($"[UserDb] Reactivated Solo user '{username}' for PC {pcId} with reset password");
            return (username, resetPassword); // return password so caller can SMS it
        }
        var baseUsername = string.IsNullOrEmpty(ln) ? fn : $"{fn}.{ln}";
        var password = fn.Length > 0
            ? char.ToUpper(fn[0]) + fn[1..] + "1992"
            : "User1992";

        // Ensure unique username
        var finalUsername = baseUsername;
        int suffix = 2;
        using var existsCmd = conn.CreateCommand();
        existsCmd.CommandText = "SELECT COUNT(*) FROM core_users WHERE Username = @u COLLATE NOCASE";
        while (true)
        {
            existsCmd.Parameters.Clear();
            existsCmd.Parameters.AddWithValue("@u", finalUsername);
            if ((long)existsCmd.ExecuteScalar()! == 0) break;
            finalUsername = $"{baseUsername}{suffix++}";
        }

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO core_users (PersonId, Username, PasswordHash, StaffRole, UserType, IsActive, MustChangePassword, ContactConfirmed, AllowAll, SendSms, TotpEnabled, TotpSecret, Require2FA)
            VALUES (@pid, @u, @h, 'Solo', 'Standard', 1, 1, 0, 0, 1, -999, 'DEPRECATED', -999)";
        insertCmd.Parameters.AddWithValue("@pid", pcId);
        insertCmd.Parameters.AddWithValue("@u", finalUsername);
        insertCmd.Parameters.AddWithValue("@h", HashPassword(password));
        insertCmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Created Solo user '{finalUsername}' for PC {pcId}");
        return (finalUsername, password); // password returned so caller can SMS it
    }

    // ── Contact confirmation ─────────────────────────────────────────────────

    /// <summary>Returns true if the user must fill in contact info on this login.</summary>
    public bool NeedsContactConfirm(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(ContactConfirmed,1) FROM core_users WHERE Id=@id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        var result = cmd.ExecuteScalar();
        return result is long v && v == 0;
    }

    /// <summary>Marks the user's contact info as confirmed.</summary>
    public void SetContactConfirmed(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET ContactConfirmed=1 WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Marks the user as needing to confirm contact info on next login.</summary>
    public void SetContactConfirmNeeded(int userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET ContactConfirmed=0 WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Deactivates the Solo auditor account for the given PC (sets IsActive=0).</summary>
    public void DisableSoloAuditor(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_users SET IsActive = 0 WHERE PersonId = @pid AND StaffRole = 'Solo'";
        cmd.Parameters.AddWithValue("@pid", pcId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[UserDb] Disabled Solo user for PC {pcId}");
    }

    // ── AES-256-GCM helpers ──────────────────────────────────────────────────

    private static string EncryptTotp(byte[] plaintext, string base64Key)
    {
        var key = Convert.FromBase64String(base64Key);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var cipher = new byte[plaintext.Length];
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        // format: nonce(12) + tag(16) + cipher
        var result = new byte[nonce.Length + tag.Length + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, nonce.Length);
        cipher.CopyTo(result, nonce.Length + tag.Length);
        return Convert.ToBase64String(result);
    }

    private static byte[] DecryptTotp(string base64Cipher, string base64Key)
    {
        var data = Convert.FromBase64String(base64Cipher);
        var key = Convert.FromBase64String(base64Key);
        const int nonceSize = 12, tagSize = 16;
        var nonce = data[..nonceSize];
        var tag   = data[nonceSize..(nonceSize + tagSize)];
        var cipher = data[(nonceSize + tagSize)..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, tagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
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

    /// <summary>Public wrapper — verifies a stored PBKDF2 hash against a plain-text password.</summary>
    public static bool VerifyPassword(string storedHash, string password) => VerifyPbkdf2(storedHash, password);

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

    // ── Magic Links ──────────────────────────────────────────────────────────

    public string CreateMagicLink(int personId)
    {
        var token = Random.Shared.Next(100000, 999999).ToString(); // 6-digit code
        var expiresAt = DateTime.UtcNow.AddMinutes(10).ToString("O");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Invalidate all previous unused tokens for this person
        using var inv = conn.CreateCommand();
        inv.CommandText = "UPDATE sys_magic_links SET UsedAt = datetime('now') WHERE PersonId = @pid AND UsedAt IS NULL";
        inv.Parameters.AddWithValue("@pid", personId);
        inv.ExecuteNonQuery();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO sys_magic_links (PersonId, Token, ExpiresAt) VALUES (@pid, @tok, @exp)";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@tok", token);
        cmd.Parameters.AddWithValue("@exp", expiresAt);
        cmd.ExecuteNonQuery();

        Console.WriteLine($"[Auth] Magic link created for PersonId={personId}, expires={expiresAt}");
        return token;
    }

    /// <summary>Validates and consumes a magic link token. Returns PersonId or null.</summary>
    public int? ValidateMagicLink(string token)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, PersonId, ExpiresAt, UsedAt FROM sys_magic_links WHERE Token = @tok";
        cmd.Parameters.AddWithValue("@tok", token);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var id = r.GetInt32(0);
        var personId = r.GetInt32(1);
        var expiresAt = DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind);
        var usedAt = r.IsDBNull(3) ? (string?)null : r.GetString(3);

        if (usedAt != null) { Console.WriteLine($"[Auth] Magic link already used (id={id})"); return null; }
        if (DateTime.UtcNow > expiresAt) { Console.WriteLine($"[Auth] Magic link expired (id={id})"); return null; }

        // Mark as used
        using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE sys_magic_links SET UsedAt = datetime('now') WHERE Id = @id";
        upd.Parameters.AddWithValue("@id", id);
        upd.ExecuteNonQuery();

        Console.WriteLine($"[Auth] Magic link validated for PersonId={personId}");
        return personId;
    }

    /// <summary>Gets the email for a person from core_persons.</summary>
    public string? GetPersonEmail(int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Email FROM core_persons WHERE PersonId = @pid";
        cmd.Parameters.AddWithValue("@pid", personId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Gets the phone for a person from core_persons.</summary>
    public string? GetPersonPhone(int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Phone FROM core_persons WHERE PersonId = @pid";
        cmd.Parameters.AddWithValue("@pid", personId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Gets the display name for a person.</summary>
    public string? GetPersonDisplayName(int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''), '')) FROM core_persons WHERE PersonId = @pid";
        cmd.Parameters.AddWithValue("@pid", personId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Finds the UserId (core_users.Id) for a PersonId, preferring non-Solo active rows.</summary>
    public LoginFlags? GetLoginFlagsByPersonId(int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.Id, u.Username, u.MustChangePassword, u.UserType, u.StaffRole, u.PersonId
            FROM core_users u
            WHERE u.PersonId = @pid AND u.IsActive = 1
            ORDER BY CASE WHEN u.StaffRole = 'Solo' THEN 1 ELSE 0 END
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pid", personId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var userType = r.GetString(3);
        var staffRole = r.GetString(4);
        var roles = new List<string>();
        if (userType == "Admin") roles.Add("Admin");
        else if (staffRole != StaffRoles.Solo) roles.Add("Customer");
        return new LoginFlags(r.GetInt32(0), r.GetString(1), r.GetInt32(2) == 1, roles, staffRole, r.GetInt32(5));
    }

    // ── Passkeys ─────────────────────────────────────────────────────────────

    public record PasskeyInfo(int Id, int PersonId, byte[] CredentialId, byte[] PublicKey, int SignCount, string DeviceName, string CreatedAt);

    public void AddPasskey(int personId, byte[] credentialId, byte[] publicKey, int signCount, string deviceName)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO sys_passkeys (PersonId, CredentialId, PublicKey, SignCount, DeviceName)
                            VALUES (@pid, @cid, @pk, @sc, @dn)";
        cmd.Parameters.AddWithValue("@pid", personId);
        cmd.Parameters.AddWithValue("@cid", credentialId);
        cmd.Parameters.AddWithValue("@pk", publicKey);
        cmd.Parameters.AddWithValue("@sc", signCount);
        cmd.Parameters.AddWithValue("@dn", deviceName);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[Auth] Passkey registered for PersonId={personId}, device='{deviceName}'");
    }

    public List<PasskeyInfo> GetPasskeysByPerson(int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, PersonId, CredentialId, PublicKey, SignCount, DeviceName, CreatedAt FROM sys_passkeys WHERE PersonId = @pid ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@pid", personId);
        var list = new List<PasskeyInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var cid = new byte[r.GetBytes(2, 0, null, 0, 0)];
            r.GetBytes(2, 0, cid, 0, cid.Length);
            var pk = new byte[r.GetBytes(3, 0, null, 0, 0)];
            r.GetBytes(3, 0, pk, 0, pk.Length);
            list.Add(new PasskeyInfo(r.GetInt32(0), r.GetInt32(1), cid, pk, r.GetInt32(4), r.GetString(5), r.GetString(6)));
        }
        return list;
    }

    public PasskeyInfo? GetPasskeyByCredentialId(byte[] credentialId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, PersonId, CredentialId, PublicKey, SignCount, DeviceName, CreatedAt FROM sys_passkeys WHERE CredentialId = @cid";
        cmd.Parameters.AddWithValue("@cid", credentialId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var cid = new byte[r.GetBytes(2, 0, null, 0, 0)];
        r.GetBytes(2, 0, cid, 0, cid.Length);
        var pk = new byte[r.GetBytes(3, 0, null, 0, 0)];
        r.GetBytes(3, 0, pk, 0, pk.Length);
        return new PasskeyInfo(r.GetInt32(0), r.GetInt32(1), cid, pk, r.GetInt32(4), r.GetString(5), r.GetString(6));
    }

    public void UpdatePasskeySignCount(int id, int signCount)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sys_passkeys SET SignCount = @sc WHERE Id = @id";
        cmd.Parameters.AddWithValue("@sc", signCount);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public bool RemovePasskey(int id, int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sys_passkeys WHERE Id = @id AND PersonId = @pid";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@pid", personId);
        return cmd.ExecuteNonQuery() > 0;
    }
}
