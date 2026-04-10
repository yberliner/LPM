using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record PcListItem(int PcId, string FullName, string Nick, long RemainSec);
public record PcDetailInfo(int PcId, string FirstName, string LastName, string Nick,
    string Phone, string Email, string Notes, string DateOfBirth, string Gender,
    string Org, string Source, int OrgId = 0)
{
    public string FullName => string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";
}
public record PcSessionInfo(int SessionId, string Date, string AuditorName,
    int LengthSec, int AdminSec, bool IsFree, string VerifiedStatus);
public record PcStats(int TotalSessions, int FreeSessions, long UsedSec,
    double TotalHoursPurchased, int TotalAmountPaid, string? LastSessionDate);
public record PcListItemEx(int PcId, string FullName, string Nick, long RemainSec,
    long TotalSessionSec, int TotalSessions, int AuditorSessions, int AcademyVisits, double HoursPurchased, string Auditor = "");
public record PurchaseListItem(int PurchaseId, int PcId, string PcName, string PurchaseDate,
    string? Notes, string ApprovedStatus, string? ApprovedByName, string? ApprovedAt,
    string? CreatedByName, string CreatedAt, int TotalAmount, double TotalHours, bool IsDeleted = false, string Currency = "ILS", int? TransferPurchaseId = null);
public record PurchaseItemInfo(int PurchaseItemId, string ItemType, int? CourseId,
    string? CourseName, int? BookId, string? BookName, double HoursBought, int AmountPaid);
public record PurchaseDetail(int PurchaseId, int PcId, string PcName, string PurchaseDate,
    string? Notes, string? SignatureData, string ApprovedStatus, string? ApprovedByName,
    string? CreatedByName, List<PurchaseItemInfo> Items, List<PurchasePaymentMethodInfo> PaymentMethods,
    int? RegistrarId = null, string? RegistrarName = null, int? ReferralId = null, string? ReferralName = null, string Currency = "ILS");
public record PurchasePaymentMethodInfo(int PaymentMethodId, string MethodType,
    int Amount, string? PaymentDate, bool IsMoneyInBank, string? MoneyInBankDate, int Installments = 1);

// ── Balance calculation records ──
public record PcBalanceData(
    int PurchasedNis, decimal UsedNis, decimal BalanceNis,
    double HoursLeft, int EffectiveRateCents, int PurchaseRateCents, bool RateMismatch,
    string Currency = "ILS");

public record PcPurchaseRow(int PurchaseId, string Date, double Hours, int AmountNis, string Currency = "ILS");

public record PcSessionCostRow(
    int SessionId, string Date, string AuditorName,
    int LengthSec, int AdminSec, int RateCentsUsed, string RateSource, decimal CostNis, bool IsImported = false);

public record SoloCsReviewCostRow(int SessionId, string Date, int ReviewLengthSec, int RateCents, decimal CostNis, bool IsOtfsFree = false, bool IsImported = false);

public record PcBalanceExplanation(
    List<PcPurchaseRow> Purchases, int TotalPurchasedNis,
    decimal TotalUsedNis, decimal BalanceNis, int BillableSessionCount, int SoloReviewCount,
    int EffectiveRateCents, int PurchaseRateCents, double HoursLeft, string RateSource);

public class PcService
{
    private readonly string _connectionString;

    public PcService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        // Schema managed directly in DB — no CREATE TABLE statements here.
    }

public List<PcListItem> GetAllPcs()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   COALESCE(p.Nick, '') AS Nick,
                   (COALESCE(pay.TotalHours, 0) * 3600 - COALESCE(sess.UsedSec, 0)) AS RemainSec
            FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            LEFT JOIN (
                SELECT pu.PcId, SUM(pi.HoursBought) AS TotalHours
                FROM fin_purchase_items pi
                JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
                WHERE pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
                GROUP BY pu.PcId
            ) pay ON pay.PcId = pc.PcId
            LEFT JOIN (
                SELECT PcId, SUM(LengthSeconds) AS UsedSec
                FROM sess_sessions WHERE IsFreeSession = 0 GROUP BY PcId
            ) sess ON sess.PcId = pc.PcId
            WHERE COALESCE(p.IsActive, 1) = 1
            ORDER BY RemainSec ASC, p.FirstName, p.LastName";
        var list = new List<PcListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcListItem(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt64(3)));
        return list;
    }

    /// <summary>
    /// Checks for duplicate PC by FirstName+LastName+Nick.
    /// Returns null if no conflict, or a message string if blocked.
    /// If the duplicate is inactive, auto-renames its nick and allows adding.
    /// </summary>
    public string? CheckDuplicatePc(string firstName, string lastName, string nick)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PersonId, COALESCE(p.IsActive, 1)
            FROM core_persons p
            JOIN core_pcs pc ON pc.PcId = p.PersonId
            WHERE LOWER(TRIM(p.FirstName)) = LOWER(@fn)
              AND LOWER(COALESCE(TRIM(p.LastName),'')) = LOWER(@ln)
              AND LOWER(COALESCE(TRIM(p.Nick),'')) = LOWER(@nick)";
        cmd.Parameters.AddWithValue("@fn", firstName.Trim());
        cmd.Parameters.AddWithValue("@ln", lastName.Trim());
        cmd.Parameters.AddWithValue("@nick", nick?.Trim() ?? "");
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null; // no conflict

        var existingId = r.GetInt32(0);
        var isActive = r.GetInt32(1) == 1;

        if (!isActive)
        {
            // Auto-rename the inactive one's nick
            r.Close();
            using var upd = conn.CreateCommand();
            upd.CommandText = "UPDATE core_persons SET Nick = @newNick WHERE PersonId = @id";
            upd.Parameters.AddWithValue("@newNick", $"_{existingId}");
            upd.Parameters.AddWithValue("@id", existingId);
            upd.ExecuteNonQuery();
            return null; // conflict resolved
        }

        return "ACTIVE_DUPLICATE";
    }

    /// <summary>Find an existing PC by name or create a new one. Returns (PcId, wasCreated).</summary>
    private static readonly string[] _noiseWords = { "solo", "review", "folder", "confidential" };

    /// <summary>Strip diacritics (ģ→g, é→e, etc.) while preserving original casing.</summary>
    public static string StripDiacritics(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    public static string StripNoiseWords(string name)
    {
        foreach (var word in _noiseWords)
            name = System.Text.RegularExpressions.Regex.Replace(
                name, $@"(?<!\w){System.Text.RegularExpressions.Regex.Escape(word)}(?!\w)",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s+", " ").Trim();
    }

    public (int PcId, bool WasCreated) FindOrCreatePcByName(string folderName)
    {
        // Strip numeric prefix like "25-" from folder name, then noise words
        var stripped = System.Text.RegularExpressions.Regex.Replace(folderName.Trim(), @"^\d+-\s*", "");
        var name = StripNoiseWords(stripped);
        // Fallback: if noise stripping removed everything, use the pre-strip name
        if (string.IsNullOrWhiteSpace(name)) name = stripped.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = folderName.Trim();

        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = parts.Length > 0 ? parts[0] : name;
        var lastName  = parts.Length > 1 ? parts[1] : "Unknown";

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Try to find existing PC by name match
        using var findCmd = conn.CreateCommand();
        findCmd.CommandText = @"
            SELECT pc.PcId FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            WHERE LOWER(TRIM(p.FirstName)) = LOWER(@fn)
              AND LOWER(COALESCE(TRIM(p.LastName),'')) = LOWER(@ln)
            LIMIT 1";
        findCmd.Parameters.AddWithValue("@fn", firstName);
        findCmd.Parameters.AddWithValue("@ln", lastName);
        var existing = findCmd.ExecuteScalar();
        if (existing is long existingId)
            return ((int)existingId, false);

        // Normalized fallback: compare with Unicode-stripped names in C#
        var normalizedId = FindPcByNormalizedName(conn, firstName, lastName);
        if (normalizedId.HasValue)
            return (normalizedId.Value, false);

        // Create new
        var newId = AddPcWithPerson(firstName, lastName, "", "", "", "");
        Console.WriteLine($"[PcService] Created new PC {newId} from folder name '{folderName}'");
        return (newId, true);
    }

    /// <summary>Find an existing PC by folder/pc name. Returns PcId or null. Does not create.</summary>
    public int? FindPcByName(string folderName)
    {
        var stripped = System.Text.RegularExpressions.Regex.Replace(folderName.Trim(), @"^\d+-\s*", "");
        var name = StripNoiseWords(stripped);
        if (string.IsNullOrWhiteSpace(name)) name = stripped.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = folderName.Trim();
        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = parts.Length > 0 ? parts[0] : name;
        var lastName  = parts.Length > 1 ? parts[1] : "Unknown";

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            WHERE LOWER(TRIM(p.FirstName)) = LOWER(@fn)
              AND LOWER(COALESCE(TRIM(p.LastName),'')) = LOWER(@ln)
            LIMIT 1";
        cmd.Parameters.AddWithValue("@fn", firstName);
        cmd.Parameters.AddWithValue("@ln", lastName);
        var result = cmd.ExecuteScalar();
        if (result is long id) return (int)id;

        // Normalized fallback: compare with Unicode-stripped names in C#
        return FindPcByNormalizedName(conn, firstName, lastName);
    }

    private static int? FindPcByNormalizedName(SqliteConnection conn, string firstName, string lastName)
    {
        var fnNorm = ImportJobService.NormalizeToAscii(firstName);
        var lnNorm = ImportJobService.NormalizeToAscii(lastName);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId, p.FirstName, COALESCE(p.LastName,'') AS LastName
            FROM core_pcs pc JOIN core_persons p ON p.PersonId = pc.PcId";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (ImportJobService.NormalizeToAscii(reader.GetString(1)) == fnNorm &&
                ImportJobService.NormalizeToAscii(reader.GetString(2)) == lnNorm)
                return (int)reader.GetInt64(0);
        }
        return null;
    }


    public int AddPcWithPerson(string firstName, string lastName,
        string phone, string email, string dateOfBirth, string gender,
        int? orgId = null, int? sourceId = null, string notes = "", string nick = "")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            INSERT INTO core_persons (FirstName, LastName, Phone, Email, DateOfBirth, Gender, Org, Source, Notes, Nick)
            VALUES (@fn, @ln, @ph, @em, @dob, @gender, @org, @srcId, @notes, @nick)";
        pCmd.Parameters.AddWithValue("@fn",  firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln",  lastName.Trim());
        pCmd.Parameters.AddWithValue("@ph",  Nv(phone));
        pCmd.Parameters.AddWithValue("@em",  Nv(email));
        pCmd.Parameters.AddWithValue("@dob", Nv(dateOfBirth));
        pCmd.Parameters.AddWithValue("@gender", Nv(gender));
        pCmd.Parameters.AddWithValue("@org", orgId.HasValue ? (object)orgId.Value : DBNull.Value);
        pCmd.Parameters.AddWithValue("@srcId", sourceId.HasValue ? (object)sourceId.Value : DBNull.Value);
        pCmd.Parameters.AddWithValue("@notes", Nv(notes));
        pCmd.Parameters.AddWithValue("@nick", Nv(nick));
        pCmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var personId = (int)(long)idCmd.ExecuteScalar()!;

        using var pcCmd = conn.CreateCommand();
        pcCmd.CommandText = "INSERT INTO core_pcs (PcId) VALUES (@id)";
        pcCmd.Parameters.AddWithValue("@id", personId);
        pcCmd.ExecuteNonQuery();

        Console.WriteLine($"[PcService] Added PC with person {personId}: '{firstName.Trim()} {lastName.Trim()}'");
        return personId;
    }

    public PcDetailInfo? GetPcDetail(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.FirstName,             COALESCE(p.LastName,''),
                   COALESCE(p.Nick,''), COALESCE(p.Phone,''),
                   COALESCE(p.Email,''),        COALESCE(p.Notes,''),
                   COALESCE(p.DateOfBirth,''), COALESCE(p.Gender,''),
                   COALESCE(og.Name,''),        COALESCE(rs.Name,''),
                   COALESCE(p.Org, 0)
            FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            LEFT JOIN lkp_referral_sources rs ON rs.ReferralId = p.Source
            LEFT JOIN lkp_organizations og ON og.OrgId = p.Org
            WHERE pc.PcId = @id";
        cmd.Parameters.AddWithValue("@id", pcId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PcDetailInfo(pcId,
            r.GetString(0), r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetString(4), r.GetString(5),
            r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9),
            r.GetInt32(10));
    }

    public void UpdatePcDetail(int pcId, string firstName, string lastName,
        string nick, string phone, string email, string notes,
        string dateOfBirth, string gender, int? orgId = null, int? sourceId = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            UPDATE core_persons SET FirstName=@fn, LastName=@ln,
                               Phone=@ph, Email=@em, DateOfBirth=@dob, Gender=@gender,
                               Nick=@nick, Notes=@nt, Org=@org, Source=@srcId
            WHERE PersonId=@id";
        pCmd.Parameters.AddWithValue("@fn",   firstName.Trim());
        pCmd.Parameters.AddWithValue("@ln",   lastName.Trim());
        pCmd.Parameters.AddWithValue("@ph",   Nv(phone));
        pCmd.Parameters.AddWithValue("@em",   Nv(email));
        pCmd.Parameters.AddWithValue("@dob",  Nv(dateOfBirth));
        pCmd.Parameters.AddWithValue("@gender", Nv(gender));
        pCmd.Parameters.AddWithValue("@nick", Nv(nick));
        pCmd.Parameters.AddWithValue("@nt",   Nv(notes));
        pCmd.Parameters.AddWithValue("@org",  orgId.HasValue ? (object)orgId.Value : DBNull.Value);
        pCmd.Parameters.AddWithValue("@srcId", sourceId.HasValue ? (object)sourceId.Value : DBNull.Value);
        pCmd.Parameters.AddWithValue("@id",   pcId);
        pCmd.ExecuteNonQuery();
        Console.WriteLine($"[PcService] Updated person data for {pcId}");
    }

    /// <summary>Saves only the email and phone for a person (used in WelcomeContact).</summary>
    public (string Email, string Phone) GetPersonContact(int personId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(Email,''), COALESCE(Phone,'') FROM core_persons WHERE PersonId=@id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", personId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return ("", "");
        return (r.GetString(0), r.GetString(1));
    }

    public void SaveContactInfo(int personId, string email, string phone)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE core_persons SET Email=@em, Phone=@ph WHERE PersonId=@id";
        cmd.Parameters.AddWithValue("@em", string.IsNullOrWhiteSpace(email) ? DBNull.Value : (object)email.Trim());
        cmd.Parameters.AddWithValue("@ph", string.IsNullOrWhiteSpace(phone) ? DBNull.Value : (object)phone.Trim());
        cmd.Parameters.AddWithValue("@id", personId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[PcService] Saved contact info for PersonId={personId}");
    }

    public PcStats GetPcStats(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var sCmd = conn.CreateCommand();
        sCmd.CommandText = @"
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN IsFreeSession=1 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN IsFreeSession=0 THEN LengthSeconds ELSE 0 END), 0),
                   MAX(SessionDate)
            FROM sess_sessions WHERE PcId=@id";
        sCmd.Parameters.AddWithValue("@id", pcId);
        using var sr = sCmd.ExecuteReader();
        sr.Read();
        int    total     = sr.GetInt32(0);
        int    free      = sr.GetInt32(1);
        long   usedSec   = sr.GetInt64(2);
        string? lastDate = sr.IsDBNull(3) ? null : sr.GetString(3);

        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            SELECT COALESCE(SUM(CASE WHEN pi.ItemType='Auditing' THEN pi.HoursBought ELSE 0 END),0),
                   COALESCE(SUM(pi.AmountPaid),0)
            FROM fin_purchase_items pi
            JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
            WHERE pu.PcId=@id AND pu.IsDeleted = 0";
        pCmd.Parameters.AddWithValue("@id", pcId);
        using var pr = pCmd.ExecuteReader();
        pr.Read();
        double hours = pr.GetDouble(0);
        int amount = pr.GetInt32(1);

        return new PcStats(total, free, usedSec, hours, amount, lastDate);
    }

    public List<PcSessionInfo> GetPcSessions(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.SessionId, s.SessionDate, p.FirstName,
                   s.LengthSeconds, s.AdminSeconds, s.IsFreeSession,
                   COALESCE(s.VerifiedStatus,'Pending')
            FROM sess_sessions s
            JOIN core_persons p ON p.PersonId = s.AuditorId
            WHERE s.PcId=@id
            ORDER BY s.SessionDate DESC, s.SequenceInDay DESC";
        cmd.Parameters.AddWithValue("@id", pcId);
        var list = new List<PcSessionInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcSessionInfo(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.GetInt32(3), r.GetInt32(4),
                r.GetInt32(5) == 1, r.GetString(6)));
        return list;
    }


    /// <summary>All active staff (Auditors + CaseSupervisors), ordered by first name.</summary>
    public List<(int PersonId, string FullName)> GetStaff()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PersonId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''),'')) AS FullName
            FROM core_persons p
            WHERE p.PersonId IN (
                SELECT PersonId FROM core_users WHERE StaffRole != 'None' AND IsActive = 1
            )
            ORDER BY p.FirstName";
        var list = new List<(int, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1).Trim()));
        return list;
    }

    /// <summary>All persons for referral lookup.</summary>
    public List<(int PersonId, string FullName)> GetAllPersons()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT PersonId, TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''),''))
            FROM core_persons WHERE COALESCE(IsActive,1) = 1 ORDER BY FirstName, LastName";
        var list = new List<(int, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1).Trim()));
        return list;
    }

    /// <summary>Look up PersonId by username (matched on FirstName, case-insensitive).</summary>
    public int? GetPersonIdByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PersonId FROM core_persons WHERE LOWER(FirstName) = LOWER(@u) LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username.Trim());
        var result = cmd.ExecuteScalar();
        return result == null ? null : (int)(long)result;
    }

    // ── Mixed-currency detection ───────────────────────────────────

    /// Returns PcIds that have auditing purchases in more than one currency.
    public HashSet<int> GetPcsWithMixedCurrencies()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pu.PcId
            FROM fin_purchase_items pi
            JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
            WHERE pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
            GROUP BY pu.PcId
            HAVING COUNT(DISTINCT COALESCE(pu.Currency, 'ILS')) > 1";
        var result = new HashSet<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetInt32(0));
        return result;
    }

    /// Returns the set of distinct currencies used in auditing purchases for a given PC.
    public List<string> GetPcAuditingCurrencies(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT COALESCE(pu.Currency, 'ILS')
            FROM fin_purchase_items pi
            JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
            WHERE pu.PcId = @id AND pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'";
        cmd.Parameters.AddWithValue("@id", pcId);
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    /// Returns the currency of the last auditing purchase for a PC, or "ILS" if none.
    public string GetLastPurchaseCurrencyForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(pu.Currency, 'ILS')
            FROM fin_purchases pu
            JOIN fin_purchase_items pi ON pi.PurchaseId = pu.PurchaseId
            WHERE pu.PcId = @id AND pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
            GROUP BY pu.PurchaseId
            ORDER BY pu.PurchaseId DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@id", pcId);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : "ILS";
    }

    // ── Rate-change detection (last 365 days) ─────────────────────

    public record PurchaseRateEntry(string Date, double Hours, int RatePerHour);

    /// Returns PCs that have 2+ auditing items with different per-hour rates in the last 365 days.
    public Dictionary<int, List<PurchaseRateEntry>> GetPcsWithMixedRates()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var cutoff = DateTime.Today.AddDays(-365).ToString("yyyy-MM-dd");
        cmd.CommandText = @"
            SELECT pu.PcId, pu.PurchaseDate, pi.HoursBought, pi.AmountPaid
            FROM fin_purchase_items pi
            JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
            WHERE pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
              AND pu.PurchaseDate >= @cutoff
              AND pi.AmountPaid <> 0 AND ABS(pi.HoursBought) > 0
            ORDER BY pu.PcId, pu.PurchaseId, pi.PurchaseItemId";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        var all = new Dictionary<int, List<PurchaseRateEntry>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int pcId = r.GetInt32(0);
            string date = r.GetString(1);
            double hrs = r.GetDouble(2);
            int amt = r.GetInt32(3);
            int rate = (int)Math.Round(Math.Abs((double)amt / hrs));
            if (!all.ContainsKey(pcId)) all[pcId] = new();
            all[pcId].Add(new PurchaseRateEntry(date, hrs, rate));
        }

        // Keep only PCs with 2+ distinct rates
        var result = new Dictionary<int, List<PurchaseRateEntry>>();
        foreach (var (pcId, entries) in all)
        {
            if (entries.Select(e => e.RatePerHour).Distinct().Count() >= 2)
                result[pcId] = entries;
        }
        return result;
    }

    // ── Extended PC list with stats for table view ────────────────

    public List<PcListItemEx> GetAllPcsExtended()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pc.PcId,
                   TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')) AS FullName,
                   COALESCE(p.Nick, '') AS Nick,
                   (COALESCE(pay.TotalHours, 0) * 3600 - COALESCE(sess.UsedSec, 0)) AS RemainSec,
                   COALESCE(sess.UsedSec, 0) AS TotalSessionSec,
                   COALESCE(sess.SessionCount, 0) AS TotalSessions,
                   COALESCE(sess.AuditorSessionCount, 0) AS AuditorSessions,
                   COALESCE(acad.VisitCount, 0) AS AcademyVisits,
                   COALESCE(pay.TotalHours, 0) AS HoursPurchased,
                   COALESCE(TRIM(audp.FirstName || ' ' || COALESCE(NULLIF(audp.LastName,''), '')), '') AS Auditor
            FROM core_pcs pc
            JOIN core_persons p ON p.PersonId = pc.PcId
            LEFT JOIN (
                SELECT pu.PcId, SUM(pi.HoursBought) AS TotalHours
                FROM fin_purchase_items pi
                JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
                WHERE pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
                GROUP BY pu.PcId
            ) pay ON pay.PcId = pc.PcId
            LEFT JOIN (
                SELECT PcId,
                       SUM(CASE WHEN AuditorId IS NOT NULL THEN LengthSeconds + COALESCE(AdminSeconds, 0) ELSE 0 END) AS UsedSec,
                       COUNT(*) AS SessionCount,
                       SUM(CASE WHEN AuditorId IS NOT NULL THEN 1 ELSE 0 END) AS AuditorSessionCount
                FROM sess_sessions WHERE IsFreeSession = 0 GROUP BY PcId
            ) sess ON sess.PcId = pc.PcId
            LEFT JOIN (
                SELECT PersonId, COUNT(*) AS VisitCount
                FROM acad_attendance GROUP BY PersonId
            ) acad ON acad.PersonId = pc.PcId
            LEFT JOIN (
                SELECT lm.PcId, lm.AuditorId
                FROM sess_sessions lm
                WHERE lm.AuditorId IS NOT NULL
                  AND lm.SessionId IN (SELECT MAX(s2.SessionId) FROM sess_sessions s2 WHERE s2.AuditorId IS NOT NULL GROUP BY s2.PcId)
            ) aud ON aud.PcId = pc.PcId
            LEFT JOIN core_persons audp ON audp.PersonId = aud.AuditorId
            WHERE COALESCE(p.IsActive, 1) = 1
            ORDER BY RemainSec ASC, p.FirstName, p.LastName";
        var list = new List<PcListItemEx>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PcListItemEx(
                r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt64(3),
                r.GetInt64(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7), r.GetDouble(8), r.GetString(9)));
        return list;
    }

    // ── Balance calculation (bulk for PC list) ──────────────────────

    public Dictionary<int, PcBalanceData> GetAllPcBalances()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // A: Total purchased ₪ per PC (auditing items, non-deleted purchases)
        var purchased = new Dictionary<int, int>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT pu.PcId, COALESCE(SUM(pi.AmountPaid), 0)
                FROM fin_purchase_items pi
                JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
                WHERE pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
                GROUP BY pu.PcId";
            using var r = cmd.ExecuteReader();
            while (r.Read()) purchased[r.GetInt32(0)] = r.GetInt32(1);
        }

        // B: Last purchase rate + currency per PC (highest PurchaseId with total AmountPaid > 0)
        var lastPurchaseRate = new Dictionary<int, int>();
        var pcCurrency = new Dictionary<int, string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT pu.PcId, pu.PurchaseId, SUM(pi.AmountPaid), SUM(pi.HoursBought),
                       COALESCE(pu.Currency, 'ILS')
                FROM fin_purchase_items pi
                JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
                WHERE pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
                GROUP BY pu.PcId, pu.PurchaseId, pu.Currency
                HAVING SUM(pi.AmountPaid) <> 0
                ORDER BY pu.PcId, pu.PurchaseId";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int pcId = r.GetInt32(0);
                int amt = r.GetInt32(2);
                double hrs = r.GetDouble(3);
                pcCurrency[pcId] = r.GetString(4); // last one wins (highest PurchaseId)
                if (Math.Abs(hrs) > 0)
                    lastPurchaseRate[pcId] = (int)Math.Round(Math.Abs((double)amt / hrs)) * 100;
            }
        }

        // C: All billable sessions (non-solo, non-free)
        var sessions = new Dictionary<int, List<(int sid, int rate, int admin, int length)>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT PcId, SessionId, ChargedRateCentsPerHour, AdminSeconds, LengthSeconds
                FROM sess_sessions
                WHERE AuditorId IS NOT NULL AND IsFreeSession = 0
                ORDER BY PcId, SessionId";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int pcId = r.GetInt32(0);
                if (!sessions.ContainsKey(pcId)) sessions[pcId] = new();
                sessions[pcId].Add((r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)));
            }
        }

        // D: Solo CS review costs (only where cs_reviews.Notes = 'Bill')
        var soloCosts = new Dictionary<int, decimal>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT s.PcId, cr.ChargedCentsRatePerHour, cr.ReviewLengthSeconds
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE s.AuditorId IS NULL AND cr.ChargedCentsRatePerHour > 0
                  AND cr.Notes = 'Bill'";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int pcId = r.GetInt32(0);
                decimal cost = (decimal)r.GetInt32(1) * r.GetInt32(2) / 3600m / 100m;
                soloCosts[pcId] = soloCosts.GetValueOrDefault(pcId) + cost;
            }
        }

        // Calculate per PC
        var result = new Dictionary<int, PcBalanceData>();
        foreach (int pcId in purchased.Keys.Union(sessions.Keys).Union(soloCosts.Keys))
        {
            int purchasedNis = purchased.GetValueOrDefault(pcId, 0);
            int purchaseRate = lastPurchaseRate.GetValueOrDefault(pcId, 0);
            decimal soloUsed = soloCosts.GetValueOrDefault(pcId);

            string currency = pcCurrency.GetValueOrDefault(pcId, "ILS");

            if (!sessions.TryGetValue(pcId, out var list) || list.Count == 0)
            {
                decimal totalUsed = soloUsed;
                decimal bal = purchasedNis - totalUsed;
                double hrs = purchaseRate > 0 ? (double)(bal / ((decimal)purchaseRate / 100m)) : 0;
                result[pcId] = new(purchasedNis, totalUsed, bal, hrs, purchaseRate, purchaseRate, false, currency);
                continue;
            }

            // Global last session rate (for hours-left divisor)
            int lastSessRate = 0;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].rate > 0) { lastSessRate = list[i].rate; break; }

            // Per-session cost: backward-looking fallback rate
            int lastSeenRate = 0;
            decimal usedNis = 0m;
            foreach (var s in list)
            {
                int rate;
                if (s.rate > 0) { rate = s.rate; lastSeenRate = s.rate; }
                else { rate = lastSeenRate > 0 ? lastSeenRate : purchaseRate; }
                usedNis += (decimal)rate * (s.admin + s.length) / 3600m / 100m;
            }

            usedNis += soloUsed;
            decimal balance = purchasedNis - usedNis;
            int effective = lastSessRate > 0 ? lastSessRate : purchaseRate;
            double hoursLeft = effective > 0 ? (double)(balance / ((decimal)effective / 100m)) : 0;
            bool mismatch = effective > 0 && purchaseRate > 0 && effective != purchaseRate;

            result[pcId] = new(purchasedNis, usedNis, balance, hoursLeft, effective, purchaseRate, mismatch, currency);
        }
        return result;
    }

    // ── Purchase methods ────────────────────────────────────────────

    public int CreatePurchase(int pcId, string date, string? notes, string? signatureData,
        int? createdByPersonId,
        List<(string itemType, int? courseId, int? bookId, double hoursBought, int amountPaid)> items,
        List<(string methodType, int amount, string? paymentDate, int installments)>? paymentMethods = null,
        int? registrarId = null, int? referralId = null, string currency = "ILS")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO fin_purchases (PcId, PurchaseDate, Notes, SignatureData, CreatedByPersonId, RegistrarId, ReferralId, Currency)
            VALUES (@pcId, @date, @notes, @sig, @createdBy, @regId, @refId, @currency)";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        cmd.Parameters.AddWithValue("@date", date);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sig", (object?)signatureData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy", createdByPersonId.HasValue ? (object)createdByPersonId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@regId", registrarId.HasValue ? (object)registrarId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@refId", referralId.HasValue ? (object)referralId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@currency", currency);
        cmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.Transaction = tx;
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var purchaseId = (int)(long)idCmd.ExecuteScalar()!;

        foreach (var item in items)
        {
            using var iCmd = conn.CreateCommand();
            iCmd.Transaction = tx;
            iCmd.CommandText = @"
                INSERT INTO fin_purchase_items (PurchaseId, ItemType, CourseId, BookId, HoursBought, AmountPaid)
                VALUES (@pid, @type, @cid, @bid, @hrs, @amt)";
            iCmd.Parameters.AddWithValue("@pid", purchaseId);
            iCmd.Parameters.AddWithValue("@type", item.itemType);
            iCmd.Parameters.AddWithValue("@cid", item.courseId.HasValue ? (object)item.courseId.Value : DBNull.Value);
            iCmd.Parameters.AddWithValue("@bid", item.bookId.HasValue ? (object)item.bookId.Value : DBNull.Value);
            iCmd.Parameters.AddWithValue("@hrs", item.hoursBought);
            iCmd.Parameters.AddWithValue("@amt", item.amountPaid);
            iCmd.ExecuteNonQuery();

            // Auto-enroll in Academy StudentCourses when a course is purchased
            if (item.itemType == "Course" && item.courseId.HasValue)
            {
                using var scCmd = conn.CreateCommand();
                scCmd.Transaction = tx;
                scCmd.CommandText = @"
                    INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted, InstructorId, CsId)
                    SELECT @personId, @courseId, @date,
                      CASE WHEN (SELECT CourseType FROM lkp_courses WHERE CourseId=@courseId) IN ('OT','OTFS')
                           THEN (SELECT PersonId FROM core_users WHERE Username='aviv' AND IsActive=1 LIMIT 1) END,
                      CASE WHEN (SELECT CourseType FROM lkp_courses WHERE CourseId=@courseId) IN ('OT','OTFS')
                           THEN (SELECT PersonId FROM core_users WHERE Username='tami' AND IsActive=1 LIMIT 1) END
                    WHERE NOT EXISTS (
                        SELECT 1 FROM acad_student_courses
                        WHERE PersonId=@personId AND CourseId=@courseId AND DateFinished IS NULL
                    )";
                scCmd.Parameters.AddWithValue("@personId", pcId);
                scCmd.Parameters.AddWithValue("@courseId", item.courseId.Value);
                scCmd.Parameters.AddWithValue("@date", date);
                scCmd.ExecuteNonQuery();
            }
        }

        // Insert payment methods
        if (paymentMethods != null)
        {
            foreach (var pm in paymentMethods)
            {
                var inst = Math.Max(1, pm.installments);
                if (pm.methodType == "CreditCard" && inst > 1 && pm.paymentDate != null)
                {
                    // Split into N rows with monthly dates
                    var perPayment = pm.amount / inst;
                    var remainder = pm.amount - perPayment * inst;
                    var startDate = DateOnly.Parse(pm.paymentDate);
                    for (int k = 0; k < inst; k++)
                    {
                        var pmDate = startDate.AddMonths(k).ToString("yyyy-MM-dd");
                        var amt = perPayment + (k == inst - 1 ? remainder : 0);
                        using var pmCmd = conn.CreateCommand();
                        pmCmd.Transaction = tx;
                        pmCmd.CommandText = @"
                            INSERT INTO fin_payment_methods (PurchaseId, MethodType, Amount, PaymentDate, Installments)
                            VALUES (@pid, @type, @amt, @date, @inst)";
                        pmCmd.Parameters.AddWithValue("@pid", purchaseId);
                        pmCmd.Parameters.AddWithValue("@type", pm.methodType);
                        pmCmd.Parameters.AddWithValue("@amt", amt);
                        pmCmd.Parameters.AddWithValue("@date", pmDate);
                        pmCmd.Parameters.AddWithValue("@inst", inst);
                        pmCmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    using var pmCmd = conn.CreateCommand();
                    pmCmd.Transaction = tx;
                    pmCmd.CommandText = @"
                        INSERT INTO fin_payment_methods (PurchaseId, MethodType, Amount, PaymentDate, Installments)
                        VALUES (@pid, @type, @amt, @date, @inst)";
                    pmCmd.Parameters.AddWithValue("@pid", purchaseId);
                    pmCmd.Parameters.AddWithValue("@type", pm.methodType);
                    pmCmd.Parameters.AddWithValue("@amt", pm.amount);
                    pmCmd.Parameters.AddWithValue("@date", (object?)pm.paymentDate ?? DBNull.Value);
                    pmCmd.Parameters.AddWithValue("@inst", 1);
                    pmCmd.ExecuteNonQuery();
                }
            }
        }

        tx.Commit();
        Console.WriteLine($"[PcService] Created purchase {purchaseId} for PC {pcId}, items: {items.Count}");
        return purchaseId;
    }

    public List<PurchaseListItem> GetPurchases(bool includeApproved, DateOnly? from = null, DateOnly? to = null, bool includeDeleted = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var where = new List<string>();
        if (!includeDeleted)
            where.Add("COALESCE(p.IsDeleted, 0) = 0");
        if (!includeApproved)
            where.Add("p.ApprovedStatus != 'Approved'");
        if (from.HasValue)
        {
            where.Add("p.PurchaseDate >= @from");
            cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd"));
        }
        if (to.HasValue)
        {
            where.Add("p.PurchaseDate <= @to");
            cmd.Parameters.AddWithValue("@to", to.Value.ToString("yyyy-MM-dd"));
        }
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        cmd.CommandText = $@"
            SELECT p.PurchaseId, p.PcId,
                   TRIM(per.FirstName || ' ' || COALESCE(NULLIF(per.LastName,''), '')) AS PcName,
                   p.PurchaseDate, p.Notes, p.ApprovedStatus,
                   TRIM(COALESCE(ap.FirstName,'') || ' ' || COALESCE(NULLIF(ap.LastName,''),'')) AS ApprovedByName,
                   p.ApprovedAt,
                   TRIM(COALESCE(cr.FirstName,'') || ' ' || COALESCE(NULLIF(cr.LastName,''),'')) AS CreatedByName,
                   p.CreatedAt,
                   COALESCE(items.TotalAmount, 0),
                   COALESCE(items.TotalHours, 0),
                   COALESCE(p.IsDeleted, 0),
                   COALESCE(p.Currency,'ILS'),
                   p.TransferPurchaseId
            FROM fin_purchases p
            JOIN core_persons per ON per.PersonId = p.PcId
            LEFT JOIN core_persons ap ON ap.PersonId = p.ApprovedByPersonId
            LEFT JOIN core_persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN (
                SELECT PurchaseId, SUM(AmountPaid) AS TotalAmount, SUM(HoursBought) AS TotalHours
                FROM fin_purchase_items GROUP BY PurchaseId
            ) items ON items.PurchaseId = p.PurchaseId
            {whereClause}
            ORDER BY p.PurchaseDate DESC, p.PurchaseId DESC";

        var list = new List<PurchaseListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PurchaseListItem(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2).Trim(), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6).Trim(),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8).Trim(),
                r.GetString(9),
                r.GetInt32(10), r.GetDouble(11),
                r.GetInt32(12) == 1, r.GetString(13),
                r.IsDBNull(14) ? null : (int?)r.GetInt32(14)));
        return list;
    }

    public PurchaseDetail? GetPurchaseDetail(int purchaseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PurchaseId, p.PcId,
                   TRIM(per.FirstName || ' ' || COALESCE(NULLIF(per.LastName,''), '')) AS PcName,
                   p.PurchaseDate, p.Notes, p.SignatureData, p.ApprovedStatus,
                   TRIM(COALESCE(ap.FirstName,'') || ' ' || COALESCE(NULLIF(ap.LastName,''),'')) AS ApprovedByName,
                   TRIM(COALESCE(cr.FirstName,'') || ' ' || COALESCE(NULLIF(cr.LastName,''),'')) AS CreatedByName,
                   p.RegistrarId,
                   TRIM(COALESCE(reg.FirstName,'') || ' ' || COALESCE(NULLIF(reg.LastName,''),'')) AS RegistrarName,
                   p.ReferralId,
                   CASE WHEN p.ReferralId = -1 THEN 'Other'
                        ELSE TRIM(COALESCE(rf.FirstName,'') || ' ' || COALESCE(NULLIF(rf.LastName,''),''))
                   END AS ReferralName,
                   COALESCE(p.Currency,'ILS') AS Currency
            FROM fin_purchases p
            JOIN core_persons per ON per.PersonId = p.PcId
            LEFT JOIN core_persons ap ON ap.PersonId = p.ApprovedByPersonId
            LEFT JOIN core_persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN core_persons reg ON reg.PersonId = p.RegistrarId
            LEFT JOIN core_persons rf ON rf.PersonId = p.ReferralId
            WHERE p.PurchaseId = @id";
        cmd.Parameters.AddWithValue("@id", purchaseId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var items = new List<PurchaseItemInfo>();
        var paymentMethods = new List<PurchasePaymentMethodInfo>();

        var detail = new PurchaseDetail(
            r.GetInt32(0), r.GetInt32(1), r.GetString(2).Trim(), r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7).Trim(),
            r.IsDBNull(8) ? null : r.GetString(8).Trim(),
            items, paymentMethods,
            r.IsDBNull(9) ? null : r.GetInt32(9),
            r.IsDBNull(10) ? null : r.GetString(10).Trim(),
            r.IsDBNull(11) ? null : r.GetInt32(11),
            r.IsDBNull(12) ? null : r.GetString(12).Trim(),
            Currency: r.GetString(13));
        r.Close();

        using var iCmd = conn.CreateCommand();
        iCmd.CommandText = @"
            SELECT pi.PurchaseItemId, pi.ItemType, pi.CourseId, c.Name,
                   pi.BookId, b.Name,
                   pi.HoursBought, pi.AmountPaid
            FROM fin_purchase_items pi
            LEFT JOIN lkp_courses c ON c.CourseId = pi.CourseId
            LEFT JOIN lkp_books b ON b.BookId = pi.BookId
            WHERE pi.PurchaseId = @id
            ORDER BY pi.PurchaseItemId";
        iCmd.Parameters.AddWithValue("@id", purchaseId);
        using var ir = iCmd.ExecuteReader();
        while (ir.Read())
            items.Add(new PurchaseItemInfo(
                ir.GetInt32(0), ir.GetString(1),
                ir.IsDBNull(2) ? null : ir.GetInt32(2),
                ir.IsDBNull(3) ? null : ir.GetString(3),
                ir.IsDBNull(4) ? null : ir.GetInt32(4),
                ir.IsDBNull(5) ? null : ir.GetString(5),
                ir.GetDouble(6), ir.GetInt32(7)));
        ir.Close();

        // Load payment methods
        using var pmCmd = conn.CreateCommand();
        pmCmd.CommandText = @"
            SELECT PaymentMethodId, MethodType, Amount, PaymentDate, IsMoneyInBank, MoneyInBankDate, COALESCE(Installments,1)
            FROM fin_payment_methods WHERE PurchaseId = @id ORDER BY PaymentMethodId";
        pmCmd.Parameters.AddWithValue("@id", purchaseId);
        using var pmr = pmCmd.ExecuteReader();
        while (pmr.Read())
            paymentMethods.Add(new PurchasePaymentMethodInfo(
                pmr.GetInt32(0), pmr.GetString(1), pmr.GetInt32(2),
                pmr.IsDBNull(3) ? null : pmr.GetString(3),
                pmr.GetInt32(4) == 1,
                pmr.IsDBNull(5) ? null : pmr.GetString(5),
                pmr.GetInt32(6)));

        return detail;
    }

    public List<PurchaseListItem> GetPendingPurchasesForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PurchaseId, p.PcId,
                   TRIM(per.FirstName || ' ' || COALESCE(NULLIF(per.LastName,''), '')) AS PcName,
                   p.PurchaseDate, p.Notes, p.ApprovedStatus,
                   NULL AS ApprovedByName, p.ApprovedAt,
                   TRIM(COALESCE(cr.FirstName,'') || ' ' || COALESCE(NULLIF(cr.LastName,''),'')) AS CreatedByName,
                   p.CreatedAt,
                   COALESCE(items.TotalAmount, 0),
                   COALESCE(items.TotalHours, 0),
                   COALESCE(p.Currency,'ILS')
            FROM fin_purchases p
            JOIN core_persons per ON per.PersonId = p.PcId
            LEFT JOIN core_persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN (
                SELECT PurchaseId, SUM(AmountPaid) AS TotalAmount, SUM(HoursBought) AS TotalHours
                FROM fin_purchase_items GROUP BY PurchaseId
            ) items ON items.PurchaseId = p.PurchaseId
            WHERE p.PcId = @pcId AND p.ApprovedStatus != 'Approved' AND COALESCE(p.IsDeleted, 0) = 0
            ORDER BY p.PurchaseDate DESC";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<PurchaseListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PurchaseListItem(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2).Trim(), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6).Trim(),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8).Trim(),
                r.GetString(9),
                r.GetInt32(10), r.GetDouble(11),
                Currency: r.GetString(12)));
        return list;
    }

    public List<PurchaseListItem> GetAllPurchasesForPc(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.PurchaseId, p.PcId,
                   TRIM(per.FirstName || ' ' || COALESCE(NULLIF(per.LastName,''), '')) AS PcName,
                   p.PurchaseDate, p.Notes, p.ApprovedStatus,
                   TRIM(COALESCE(ap.FirstName,'') || ' ' || COALESCE(NULLIF(ap.LastName,''),'')) AS ApprovedByName,
                   p.ApprovedAt,
                   TRIM(COALESCE(cr.FirstName,'') || ' ' || COALESCE(NULLIF(cr.LastName,''),'')) AS CreatedByName,
                   p.CreatedAt,
                   COALESCE(items.TotalAmount, 0),
                   COALESCE(items.TotalHours, 0),
                   COALESCE(p.Currency,'ILS')
            FROM fin_purchases p
            JOIN core_persons per ON per.PersonId = p.PcId
            LEFT JOIN core_persons ap ON ap.PersonId = p.ApprovedByPersonId
            LEFT JOIN core_persons cr ON cr.PersonId = p.CreatedByPersonId
            LEFT JOIN (
                SELECT PurchaseId, SUM(AmountPaid) AS TotalAmount, SUM(HoursBought) AS TotalHours
                FROM fin_purchase_items GROUP BY PurchaseId
            ) items ON items.PurchaseId = p.PurchaseId
            WHERE p.PcId = @pcId AND COALESCE(p.IsDeleted, 0) = 0
            ORDER BY p.PurchaseDate DESC, p.PurchaseId DESC";
        cmd.Parameters.AddWithValue("@pcId", pcId);
        var list = new List<PurchaseListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PurchaseListItem(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2).Trim(), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6).Trim(),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8).Trim(),
                r.GetString(9),
                r.GetInt32(10), r.GetDouble(11),
                Currency: r.GetString(12)));
        return list;
    }

    public void UpdatePurchase(int purchaseId, string date, string? notes,
        List<(string itemType, int? courseId, int? bookId, double hoursBought, int amountPaid)> items,
        List<(string methodType, int amount, string? paymentDate, int installments)> paymentMethods,
        int? registrarId = null, int? referralId = null, string currency = "ILS")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        // Get PcId
        int pcId;
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT PcId FROM fin_purchases WHERE PurchaseId = @id";
            q.Parameters.AddWithValue("@id", purchaseId);
            pcId = (int)(long)q.ExecuteScalar()!;
        }

        // Update header + reset status to Pending
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE fin_purchases SET PurchaseDate = @date, Notes = @notes,
                    RegistrarId = @regId, ReferralId = @refId, Currency = @currency,
                    ApprovedStatus = 'Pending', ApprovedByPersonId = NULL, ApprovedAt = NULL
                WHERE PurchaseId = @id";
            cmd.Parameters.AddWithValue("@id", purchaseId);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@regId", registrarId.HasValue ? (object)registrarId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@refId", referralId.HasValue ? (object)referralId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@currency", currency);
            cmd.ExecuteNonQuery();
        }

        // Collect old course IDs before deleting items
        var oldCourseIds = new HashSet<int>();
        using (var oc = conn.CreateCommand())
        {
            oc.Transaction = tx;
            oc.CommandText = "SELECT CourseId FROM fin_purchase_items WHERE PurchaseId = @id AND ItemType = 'Course' AND CourseId IS NOT NULL";
            oc.Parameters.AddWithValue("@id", purchaseId);
            using var rdr = oc.ExecuteReader();
            while (rdr.Read()) oldCourseIds.Add(rdr.GetInt32(0));
        }

        // Delete old items + recreate
        using (var d = conn.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM fin_purchase_items WHERE PurchaseId = @id"; d.Parameters.AddWithValue("@id", purchaseId); d.ExecuteNonQuery(); }

        var newCourseIds = new HashSet<int>();
        foreach (var item in items)
        {
            using var iCmd = conn.CreateCommand();
            iCmd.Transaction = tx;
            iCmd.CommandText = @"
                INSERT INTO fin_purchase_items (PurchaseId, ItemType, CourseId, BookId, HoursBought, AmountPaid)
                VALUES (@pid, @type, @cid, @bid, @hrs, @amt)";
            iCmd.Parameters.AddWithValue("@pid", purchaseId);
            iCmd.Parameters.AddWithValue("@type", item.itemType);
            iCmd.Parameters.AddWithValue("@cid", item.courseId.HasValue ? (object)item.courseId.Value : DBNull.Value);
            iCmd.Parameters.AddWithValue("@bid", item.bookId.HasValue ? (object)item.bookId.Value : DBNull.Value);
            iCmd.Parameters.AddWithValue("@hrs", item.hoursBought);
            iCmd.Parameters.AddWithValue("@amt", item.amountPaid);
            iCmd.ExecuteNonQuery();

            if (item.itemType == "Course" && item.courseId.HasValue)
                newCourseIds.Add(item.courseId.Value);
        }

        // Sync acad_student_courses: remove dropped courses, add new courses
        foreach (var removedCourseId in oldCourseIds.Except(newCourseIds))
        {
            using var delSc = conn.CreateCommand();
            delSc.Transaction = tx;
            delSc.CommandText = "DELETE FROM acad_student_courses WHERE PersonId = @pid AND CourseId = @cid AND DateFinished IS NULL";
            delSc.Parameters.AddWithValue("@pid", pcId);
            delSc.Parameters.AddWithValue("@cid", removedCourseId);
            delSc.ExecuteNonQuery();
        }
        foreach (var addedCourseId in newCourseIds.Except(oldCourseIds))
        {
            using var addSc = conn.CreateCommand();
            addSc.Transaction = tx;
            addSc.CommandText = @"
                INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted, InstructorId, CsId)
                SELECT @pid, @cid, @date,
                  CASE WHEN (SELECT CourseType FROM lkp_courses WHERE CourseId=@cid) IN ('OT','OTFS')
                       THEN (SELECT PersonId FROM core_users WHERE Username='aviv' AND IsActive=1 LIMIT 1) END,
                  CASE WHEN (SELECT CourseType FROM lkp_courses WHERE CourseId=@cid) IN ('OT','OTFS')
                       THEN (SELECT PersonId FROM core_users WHERE Username='tami' AND IsActive=1 LIMIT 1) END
                WHERE NOT EXISTS (
                    SELECT 1 FROM acad_student_courses WHERE PersonId=@pid AND CourseId=@cid AND DateFinished IS NULL
                )";
            addSc.Parameters.AddWithValue("@pid", pcId);
            addSc.Parameters.AddWithValue("@cid", addedCourseId);
            addSc.Parameters.AddWithValue("@date", date);
            addSc.ExecuteNonQuery();
        }

        // Delete old payment methods + recreate
        using (var d = conn.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM fin_payment_methods WHERE PurchaseId = @id"; d.Parameters.AddWithValue("@id", purchaseId); d.ExecuteNonQuery(); }

        foreach (var pm in paymentMethods)
        {
            var inst = Math.Max(1, pm.installments);
            if (pm.methodType == "CreditCard" && inst > 1 && pm.paymentDate != null)
            {
                var perPayment = pm.amount / inst;
                var remainder = pm.amount - perPayment * inst;
                var startDate = DateOnly.Parse(pm.paymentDate);
                for (int k = 0; k < inst; k++)
                {
                    var pmDate = startDate.AddMonths(k).ToString("yyyy-MM-dd");
                    var amt = perPayment + (k == inst - 1 ? remainder : 0);
                    using var pmCmd = conn.CreateCommand();
                    pmCmd.Transaction = tx;
                    pmCmd.CommandText = @"
                        INSERT INTO fin_payment_methods (PurchaseId, MethodType, Amount, PaymentDate, Installments)
                        VALUES (@pid, @type, @amt, @date, @inst)";
                    pmCmd.Parameters.AddWithValue("@pid", purchaseId);
                    pmCmd.Parameters.AddWithValue("@type", pm.methodType);
                    pmCmd.Parameters.AddWithValue("@amt", amt);
                    pmCmd.Parameters.AddWithValue("@date", pmDate);
                    pmCmd.Parameters.AddWithValue("@inst", inst);
                    pmCmd.ExecuteNonQuery();
                }
            }
            else
            {
                using var pmCmd = conn.CreateCommand();
                pmCmd.Transaction = tx;
                pmCmd.CommandText = @"
                    INSERT INTO fin_payment_methods (PurchaseId, MethodType, Amount, PaymentDate, Installments)
                    VALUES (@pid, @type, @amt, @date, @inst)";
                pmCmd.Parameters.AddWithValue("@pid", purchaseId);
                pmCmd.Parameters.AddWithValue("@type", pm.methodType);
                pmCmd.Parameters.AddWithValue("@amt", pm.amount);
                pmCmd.Parameters.AddWithValue("@date", (object?)pm.paymentDate ?? DBNull.Value);
                pmCmd.Parameters.AddWithValue("@inst", 1);
                pmCmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
        Console.WriteLine($"[PcService] Updated purchase {purchaseId}, items: {items.Count}");
    }

    public void SetMoneyInBank(int paymentMethodId, bool isInBank)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE fin_payment_methods
            SET IsMoneyInBank = @val, MoneyInBankDate = CASE WHEN @val = 1 THEN datetime('now', '+2 hours') ELSE NULL END
            WHERE PaymentMethodId = @id";
        cmd.Parameters.AddWithValue("@id", paymentMethodId);
        cmd.Parameters.AddWithValue("@val", isInBank ? 1 : 0);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[PcService] Set money in bank={isInBank} for payment method {paymentMethodId}");
    }

    public void ConfirmToBePaid(int paymentMethodId, string newMethodType)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE fin_payment_methods
            SET MethodType = @type, PaymentDate = date('now', '+2 hours'), IsMoneyInBank = 0, MoneyInBankDate = NULL
            WHERE PaymentMethodId = @id AND MethodType = 'ToBePaid'";
        cmd.Parameters.AddWithValue("@id", paymentMethodId);
        cmd.Parameters.AddWithValue("@type", newMethodType);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[PcService] Confirmed ToBePaid → {newMethodType} for payment method {paymentMethodId}");
    }

    public void DeletePurchase(int purchaseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        // Remove course enrollments linked to this purchase
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = @"
                DELETE FROM acad_student_courses
                WHERE DateFinished IS NULL
                  AND PersonId = (SELECT PcId FROM fin_purchases WHERE PurchaseId = @id)
                  AND CourseId IN (SELECT CourseId FROM fin_purchase_items WHERE PurchaseId = @id AND ItemType = 'Course' AND CourseId IS NOT NULL)";
            q.Parameters.AddWithValue("@id", purchaseId);
            q.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE fin_purchases SET IsDeleted = 1 WHERE PurchaseId = @id";
            cmd.Parameters.AddWithValue("@id", purchaseId);
            cmd.ExecuteNonQuery();
        }

        // Cascade: if this is a transfer, delete the paired purchase too
        using (var cmd2 = conn.CreateCommand())
        {
            cmd2.Transaction = tx;
            cmd2.CommandText = "UPDATE fin_purchases SET IsDeleted = 1 WHERE PurchaseId = (SELECT TransferPurchaseId FROM fin_purchases WHERE PurchaseId = @id) AND COALESCE(IsDeleted,0) = 0";
            cmd2.Parameters.AddWithValue("@id", purchaseId);
            cmd2.ExecuteNonQuery();
        }

        tx.Commit();
        Console.WriteLine($"[PcService] Deleted purchase {purchaseId}");
    }

    public void RestorePurchase(int purchaseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE fin_purchases SET IsDeleted = 0 WHERE PurchaseId = @id";
            cmd.Parameters.AddWithValue("@id", purchaseId);
            cmd.ExecuteNonQuery();
        }

        // Cascade: if this is a transfer, restore the paired purchase too
        using (var cmd2 = conn.CreateCommand())
        {
            cmd2.Transaction = tx;
            cmd2.CommandText = "UPDATE fin_purchases SET IsDeleted = 0 WHERE PurchaseId = (SELECT TransferPurchaseId FROM fin_purchases WHERE PurchaseId = @id) AND COALESCE(IsDeleted,0) = 1";
            cmd2.Parameters.AddWithValue("@id", purchaseId);
            cmd2.ExecuteNonQuery();
        }

        // Re-enroll courses from this purchase
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = @"
                INSERT INTO acad_student_courses (PersonId, CourseId, DateStarted, InstructorId, CsId)
                SELECT p.PcId, pi.CourseId, p.PurchaseDate,
                  CASE WHEN c.CourseType IN ('OT','OTFS')
                       THEN (SELECT PersonId FROM core_users WHERE Username='aviv' AND IsActive=1 LIMIT 1) END,
                  CASE WHEN c.CourseType IN ('OT','OTFS')
                       THEN (SELECT PersonId FROM core_users WHERE Username='tami' AND IsActive=1 LIMIT 1) END
                FROM fin_purchase_items pi
                JOIN fin_purchases p ON p.PurchaseId = pi.PurchaseId
                JOIN lkp_courses c ON c.CourseId = pi.CourseId
                WHERE pi.PurchaseId = @id AND pi.ItemType = 'Course' AND pi.CourseId IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM acad_student_courses sc
                    WHERE sc.PersonId = p.PcId AND sc.CourseId = pi.CourseId AND sc.DateFinished IS NULL
                  )";
            q.Parameters.AddWithValue("@id", purchaseId);
            q.ExecuteNonQuery();
        }

        tx.Commit();
        Console.WriteLine($"[PcService] Restored purchase {purchaseId}");
    }

    // ── Balance explanation (per PC, for popup) ─────────────────────

    public PcBalanceExplanation GetPcBalanceExplanation(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Purchases
        var purchases = new List<PcPurchaseRow>();
        int totalPurchased = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT pu.PurchaseId, pu.PurchaseDate,
                       COALESCE(SUM(pi.HoursBought), 0), COALESCE(SUM(pi.AmountPaid), 0),
                       COALESCE(pu.Currency, 'ILS')
                FROM fin_purchase_items pi
                JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
                WHERE pu.PcId = @id AND pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
                GROUP BY pu.PurchaseId, pu.PurchaseDate, pu.Currency
                ORDER BY pu.PurchaseId";
            cmd.Parameters.AddWithValue("@id", pcId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int amt = r.GetInt32(3);
                purchases.Add(new(r.GetInt32(0), r.GetString(1), r.GetDouble(2), amt, r.GetString(4)));
                totalPurchased += amt;
            }
        }

        // Last purchase rate
        int purchaseRate = 0;
        for (int i = purchases.Count - 1; i >= 0; i--)
            if (purchases[i].AmountNis != 0 && Math.Abs(purchases[i].Hours) > 0)
            { purchaseRate = (int)Math.Round(Math.Abs((double)purchases[i].AmountNis / purchases[i].Hours)) * 100; break; }

        // Billable sessions
        var sess = new List<(int sid, int rate, int admin, int length)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT SessionId, ChargedRateCentsPerHour, AdminSeconds, LengthSeconds
                FROM sess_sessions
                WHERE PcId = @id AND AuditorId IS NOT NULL AND IsFreeSession = 0
                ORDER BY SessionId";
            cmd.Parameters.AddWithValue("@id", pcId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) sess.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)));
        }

        // Global last session rate (for hours-left divisor)
        int lastSessRate = 0, lastSessId = 0;
        for (int i = sess.Count - 1; i >= 0; i--)
            if (sess[i].rate > 0) { lastSessRate = sess[i].rate; lastSessId = sess[i].sid; break; }

        // Per-session cost: backward-looking fallback rate
        int lastSeenRate = 0;
        decimal usedNis = 0m;
        foreach (var s in sess)
        {
            int rate;
            if (s.rate > 0) { rate = s.rate; lastSeenRate = s.rate; }
            else { rate = lastSeenRate > 0 ? lastSeenRate : purchaseRate; }
            usedNis += (decimal)rate * (s.admin + s.length) / 3600m / 100m;
        }

        // Solo CS review costs (only where cs_reviews.Notes = 'Bill')
        int soloCount = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT cr.ChargedCentsRatePerHour, cr.ReviewLengthSeconds
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE s.PcId = @id AND s.AuditorId IS NULL AND cr.ChargedCentsRatePerHour > 0
                  AND cr.Notes = 'Bill'";
            cmd.Parameters.AddWithValue("@id", pcId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                usedNis += (decimal)r.GetInt32(0) * r.GetInt32(1) / 3600m / 100m;
                soloCount++;
            }
        }

        decimal balance = totalPurchased - usedNis;
        int effective = lastSessRate > 0 ? lastSessRate : purchaseRate;
        double hoursLeft = effective > 0 ? (double)(balance / ((decimal)effective / 100m)) : 0;
        string source = lastSessRate > 0 ? $"Session #{lastSessId}" : "Purchase";

        return new(purchases, totalPurchased, usedNis, balance, sess.Count, soloCount,
            effective, purchaseRate, hoursLeft, source);
    }

    public List<PcSessionCostRow> GetPcSessionCostDetails(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Purchase rate fallback (for sessions with no prior rated session)
        int purchaseRate = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT SUM(pi.AmountPaid), SUM(pi.HoursBought)
                FROM fin_purchase_items pi
                JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
                WHERE pu.PcId = @id AND pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
                  AND pu.PurchaseId = (
                      SELECT pu2.PurchaseId FROM fin_purchases pu2
                      JOIN fin_purchase_items pi2 ON pi2.PurchaseId = pu2.PurchaseId
                      WHERE pu2.PcId = @id AND pu2.IsDeleted = 0
                        AND pi2.ItemType = 'Auditing'
                      GROUP BY pu2.PurchaseId
                      HAVING SUM(pi2.AmountPaid) <> 0
                      ORDER BY pu2.PurchaseId DESC LIMIT 1
                  )";
            cmd.Parameters.AddWithValue("@id", pcId);
            using var r = cmd.ExecuteReader();
            if (r.Read() && !r.IsDBNull(0) && !r.IsDBNull(1))
            {
                double hrs = r.GetDouble(1);
                if (Math.Abs(hrs) > 0) purchaseRate = (int)Math.Round(Math.Abs(r.GetInt32(0) / hrs)) * 100;
            }
        }

        // All billable sessions with auditor names — backward-looking fallback
        int prevRate = 0, prevRateId = 0;
        var result = new List<PcSessionCostRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT s.SessionId, s.SessionDate,
                       COALESCE(TRIM(p.FirstName || ' ' || COALESCE(NULLIF(p.LastName,''), '')), '?'),
                       s.LengthSeconds, s.AdminSeconds, s.ChargedRateCentsPerHour,
                       COALESCE(s.IsImported, 0)
                FROM sess_sessions s
                LEFT JOIN core_persons p ON p.PersonId = s.AuditorId
                WHERE s.PcId = @id AND s.AuditorId IS NOT NULL AND s.IsFreeSession = 0
                ORDER BY s.SessionId";
            cmd.Parameters.AddWithValue("@id", pcId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int sessionId = r.GetInt32(0);
                int rateCents = r.GetInt32(5);
                int rateUsed; string src;
                if (rateCents > 0) { rateUsed = rateCents; src = "Own"; prevRate = rateCents; prevRateId = sessionId; }
                else if (prevRate > 0) { rateUsed = prevRate; src = $"Session #{prevRateId}"; }
                else { rateUsed = purchaseRate; src = "Purchase"; }

                decimal cost = (decimal)rateUsed * (r.GetInt32(4) + r.GetInt32(3)) / 3600m / 100m;
                bool imported = r.GetInt32(6) == 1;
                result.Add(new(sessionId, r.GetString(1), r.GetString(2),
                    r.GetInt32(3), r.GetInt32(4), rateUsed, src, cost, imported));
            }
        }
        return result;
    }

    public List<SoloCsReviewCostRow> GetSoloCsReviewCostDetails(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var result = new List<SoloCsReviewCostRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.SessionId, s.SessionDate, cr.ReviewLengthSeconds,
                   COALESCE(cr.ChargedCentsRatePerHour, 0),
                   COALESCE(cr.Notes, ''),
                   COALESCE(s.IsImported, 0)
            FROM cs_reviews cr
            JOIN sess_sessions s ON s.SessionId = cr.SessionId
            WHERE s.PcId = @id AND s.AuditorId IS NULL
            ORDER BY s.SessionDate, s.SessionId";
        cmd.Parameters.AddWithValue("@id", pcId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int rateCents = r.GetInt32(3);
            int lengthSec = r.GetInt32(2);
            string notes = r.GetString(4);
            // Only 'Bill' is charged; 'Free' and NULL/empty (old data) are free
            bool isFree = notes != "Bill";
            bool imported = r.GetInt32(5) == 1;
            decimal cost = isFree ? 0m : (decimal)rateCents * lengthSec / 3600m / 100m;
            result.Add(new(r.GetInt32(0), r.GetString(1), lengthSec, rateCents, cost, isFree, imported));
        }
        return result;
    }

    // ── Transfer Balance ──────────────────────────────────────────

    public record PcAuditingBalance(double HoursPurchased, int AuditingAmountPaid, long UsedSec, double RemainingHours, int RemainingNis, int RatePerHour);

    public PcAuditingBalance GetPcAuditingBalance(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Auditing-only approved purchases
        using var pCmd = conn.CreateCommand();
        pCmd.CommandText = @"
            SELECT COALESCE(SUM(pi.HoursBought), 0), COALESCE(SUM(pi.AmountPaid), 0)
            FROM fin_purchase_items pi
            JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
            WHERE pu.PcId = @id AND pu.IsDeleted = 0 AND pi.ItemType = 'Auditing'
              AND pu.ApprovedStatus = 'Approved'";
        pCmd.Parameters.AddWithValue("@id", pcId);
        using var pr = pCmd.ExecuteReader();
        pr.Read();
        double hours = pr.GetDouble(0);
        int amount = pr.GetInt32(1);
        pr.Close();

        // Used seconds (paid sessions only)
        using var sCmd = conn.CreateCommand();
        sCmd.CommandText = "SELECT COALESCE(SUM(LengthSeconds), 0) FROM sess_sessions WHERE PcId = @id AND IsFreeSession = 0";
        sCmd.Parameters.AddWithValue("@id", pcId);
        long usedSec = (long)sCmd.ExecuteScalar()!;

        double remainHrs = hours - (double)usedSec / 3600.0;
        int rate = Math.Abs(hours) > 0 ? (int)Math.Round(Math.Abs(amount / hours)) : 0;
        int remainNis = (int)Math.Round(remainHrs * rate);

        return new PcAuditingBalance(hours, amount, usedSec, remainHrs, remainNis, rate);
    }

    public (int fromPurchaseId, int toPurchaseId) CreateTransfer(
        int fromPcId, int toPcId, double deductHours, double addHours,
        int createdByPersonId, string currency = "ILS")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            // Re-check remaining balance inside transaction
            using (var chk = conn.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = @"
                    SELECT (COALESCE(SUM(pi.HoursBought), 0) * 3600 - COALESCE(
                        (SELECT SUM(LengthSeconds) FROM sess_sessions WHERE PcId = @id AND IsFreeSession = 0), 0))
                    FROM fin_purchase_items pi
                    JOIN fin_purchases pu ON pu.PurchaseId = pi.PurchaseId
                    WHERE pu.PcId = @id AND pu.IsDeleted = 0 AND pi.ItemType = 'Auditing' AND pu.ApprovedStatus = 'Approved'";
                chk.Parameters.AddWithValue("@id", fromPcId);
                var remainSec = Convert.ToDouble(chk.ExecuteScalar()!);
                if (remainSec / 3600.0 < deductHours)
                    throw new InvalidOperationException($"Insufficient balance: {remainSec / 3600} hours remaining, {deductHours} requested");
            }

            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var now = $"datetime('now', '+2 hours')";

            // Get names for notes
            string GetName(int pcId)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT TRIM(FirstName || ' ' || COALESCE(NULLIF(LastName,''), '')) FROM core_persons WHERE PersonId = @id";
                cmd.Parameters.AddWithValue("@id", pcId);
                return cmd.ExecuteScalar()?.ToString() ?? $"PC {pcId}";
            }
            var fromName = GetName(fromPcId);
            var toName = GetName(toPcId);

            // 1. Create FROM purchase (negative hours)
            int fromPurchaseId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $@"
                    INSERT INTO fin_purchases (PcId, PurchaseDate, Notes, ApprovedStatus, ApprovedByPersonId, ApprovedAt, CreatedByPersonId, CreatedAt, Currency)
                    VALUES (@pcId, @date, @notes, 'Approved', @by, {now}, @by, {now}, @cur);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@pcId", fromPcId);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@notes", $"Transfer to {toName}");
                cmd.Parameters.AddWithValue("@by", createdByPersonId);
                cmd.Parameters.AddWithValue("@cur", currency);
                fromPurchaseId = Convert.ToInt32(cmd.ExecuteScalar()!);
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO fin_purchase_items (PurchaseId, ItemType, HoursBought, AmountPaid) VALUES (@pid, 'Auditing', @hrs, 0)";
                cmd.Parameters.AddWithValue("@pid", fromPurchaseId);
                cmd.Parameters.AddWithValue("@hrs", -deductHours);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO fin_payment_methods (PurchaseId, MethodType, Amount, PaymentDate, IsMoneyInBank) VALUES (@pid, 'Transfer', 0, @date, 0)";
                cmd.Parameters.AddWithValue("@pid", fromPurchaseId);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.ExecuteNonQuery();
            }

            // 2. Create TO purchase (positive hours)
            int toPurchaseId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $@"
                    INSERT INTO fin_purchases (PcId, PurchaseDate, Notes, ApprovedStatus, ApprovedByPersonId, ApprovedAt, CreatedByPersonId, CreatedAt, Currency)
                    VALUES (@pcId, @date, @notes, 'Approved', @by, {now}, @by, {now}, @cur);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@pcId", toPcId);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@notes", $"Transfer from {fromName}");
                cmd.Parameters.AddWithValue("@by", createdByPersonId);
                cmd.Parameters.AddWithValue("@cur", currency);
                toPurchaseId = Convert.ToInt32(cmd.ExecuteScalar()!);
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO fin_purchase_items (PurchaseId, ItemType, HoursBought, AmountPaid) VALUES (@pid, 'Auditing', @hrs, 0)";
                cmd.Parameters.AddWithValue("@pid", toPurchaseId);
                cmd.Parameters.AddWithValue("@hrs", addHours);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO fin_payment_methods (PurchaseId, MethodType, Amount, PaymentDate, IsMoneyInBank) VALUES (@pid, 'Transfer', 0, @date, 0)";
                cmd.Parameters.AddWithValue("@pid", toPurchaseId);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.ExecuteNonQuery();
            }

            // 3. Link them together
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE fin_purchases SET TransferPurchaseId = @pair WHERE PurchaseId = @id";
                cmd.Parameters.AddWithValue("@id", fromPurchaseId);
                cmd.Parameters.AddWithValue("@pair", toPurchaseId);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE fin_purchases SET TransferPurchaseId = @pair WHERE PurchaseId = @id";
                cmd.Parameters.AddWithValue("@id", toPurchaseId);
                cmd.Parameters.AddWithValue("@pair", fromPurchaseId);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            Console.WriteLine($"[PcService] Transfer: {fromName} (PC {fromPcId}) -{deductHours:0.##}hrs → {toName} (PC {toPcId}) +{addHours:0.##}hrs | purchases {fromPurchaseId}↔{toPurchaseId}");
            return (fromPurchaseId, toPurchaseId);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void ApprovePurchase(int purchaseId, int approvedByPersonId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE fin_purchases SET ApprovedStatus = 'Approved',
                                 ApprovedByPersonId = @by,
                                 ApprovedAt = datetime('now', '+2 hours')
            WHERE PurchaseId = @id";
        cmd.Parameters.AddWithValue("@id", purchaseId);
        cmd.Parameters.AddWithValue("@by", approvedByPersonId);
        cmd.ExecuteNonQuery();
        Console.WriteLine($"[PcService] Approved purchase {purchaseId}");
    }

    private static object Nv(string? s) =>
        string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
}
