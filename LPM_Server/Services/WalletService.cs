using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public record Wallet(
    int WalletId,
    int PcId,
    string Currency,
    string Name,
    string? Notes,
    bool IsActive,
    int CreatedByPersonId,
    string CreatedAt);

/// <summary>Per-wallet balance snapshot in the wallet's own currency.</summary>
public record WalletBalance(
    int WalletId,
    int PcId,
    string Currency,
    string Name,
    bool IsActive,
    long PurchasedCents,       // total amount paid across all purchases on this wallet
    double PurchasedHours,     // total hours bought across all purchases on this wallet
    long UsedSessionCents,     // session charges attributable to this wallet
    double UsedSessionHours,
    long UsedCsReviewCents,    // CS review charges attributable to this wallet
    double UsedCsReviewHours,
    int EffectiveRateCentsPerHour = 0) // last approved session rate, else last purchase rate
{
    public long RemainingCents => PurchasedCents - UsedSessionCents - UsedCsReviewCents;

    /// <summary>
    /// Hours the remaining balance can still buy at the effective rate.
    /// Derived from RemainingCents ÷ rate so money-only transfers are reflected.
    /// Returns 0 when no rate has been observed yet.
    /// </summary>
    public double RemainingHours =>
        EffectiveRateCentsPerHour > 0
            ? (double)RemainingCents / EffectiveRateCentsPerHour
            : 0;
}

public class WalletService
{
    private readonly string _connectionString;

    public WalletService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "lifepower.db";
        _connectionString = $"Data Source={dbPath}";
    }

    // ── Read ─────────────────────────────────────────────────────

    private const string SelectCols = "WalletId, PcId, Currency, Name, Notes, IsActive, CreatedByPersonId, CreatedAt";

    public List<Wallet> GetWalletsForPc(int pcId, bool includeInactive = true)
    {
        var list = new List<Wallet>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT {SelectCols}
            FROM fin_wallets
            WHERE PcId = @pc {(includeInactive ? "" : "AND IsActive = 1")}
            ORDER BY WalletId";
        cmd.Parameters.AddWithValue("@pc", pcId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadWallet(r));
        return list;
    }

    public Wallet? GetWallet(int walletId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM fin_wallets WHERE WalletId = @id";
        cmd.Parameters.AddWithValue("@id", walletId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadWallet(r) : null;
    }

    /// <summary>First active wallet for a PC (by WalletId order). Falls back to first inactive if none active.</summary>
    public Wallet? GetDefaultWallet(int pcId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT {SelectCols}
            FROM fin_wallets WHERE PcId = @pc
            ORDER BY IsActive DESC, WalletId
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pc", pcId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadWallet(r) : null;
    }

    /// <summary>Batch lookup of wallets for multiple PCs, keyed by PcId → list of wallets.</summary>
    public Dictionary<int, List<Wallet>> GetWalletsForPcs(IEnumerable<int> pcIds, bool includeInactive = true)
    {
        var set = pcIds.Distinct().ToList();
        var map = set.ToDictionary(id => id, _ => new List<Wallet>());
        if (set.Count == 0) return map;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT {SelectCols}
            FROM fin_wallets
            WHERE PcId IN ({string.Join(",", set)})
              {(includeInactive ? "" : "AND IsActive = 1")}
            ORDER BY PcId, WalletId";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var w = ReadWallet(r);
            if (map.TryGetValue(w.PcId, out var bucket)) bucket.Add(w);
        }
        return map;
    }

    // ── Write ────────────────────────────────────────────────────

    public int AddWallet(int pcId, string currency, string name, string? notes, int createdByPersonId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO fin_wallets (PcId, Currency, Name, Notes, CreatedByPersonId)
            VALUES (@pc, @cur, @name, @notes, @by);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pc",    pcId);
        cmd.Parameters.AddWithValue("@cur",   currency);
        cmd.Parameters.AddWithValue("@name",  name);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by",    createdByPersonId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateWallet(int walletId, string name, string? notes, bool isActive)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE fin_wallets
               SET Name = @name, Notes = @notes, IsActive = @active
             WHERE WalletId = @id";
        cmd.Parameters.AddWithValue("@id",    walletId);
        cmd.Parameters.AddWithValue("@name",  name);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Delete a wallet only if no purchases/sessions/reviews reference it.</summary>
    public bool TryDeleteWallet(int walletId, out string? errorMessage)
    {
        errorMessage = null;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var check = conn.CreateCommand();
        check.CommandText = @"
            SELECT
              (SELECT COUNT(*) FROM fin_purchases WHERE WalletId = @id),
              (SELECT COUNT(*) FROM sess_sessions WHERE WalletId = @id),
              (SELECT COUNT(*) FROM cs_reviews    WHERE WalletId = @id)";
        check.Parameters.AddWithValue("@id", walletId);
        using (var r = check.ExecuteReader())
        {
            r.Read();
            int p = r.GetInt32(0), s = r.GetInt32(1), cr = r.GetInt32(2);
            if (p + s + cr > 0)
            {
                errorMessage = $"Wallet has history: {p} purchases, {s} sessions, {cr} reviews. Deactivate instead.";
                return false;
            }
        }
        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM fin_wallets WHERE WalletId = @id";
        del.Parameters.AddWithValue("@id", walletId);
        del.ExecuteNonQuery();
        return true;
    }

    // ── Balance ──────────────────────────────────────────────────

    public WalletBalance GetBalance(int walletId)
    {
        var w = GetWallet(walletId) ?? throw new InvalidOperationException($"Wallet {walletId} not found");
        return ComputeBalance(w);
    }

    /// <summary>Per-wallet balances for all wallets belonging to a PC.</summary>
    public List<WalletBalance> GetBalancesForPc(int pcId)
    {
        var wallets = GetWalletsForPc(pcId);
        if (wallets.Count == 0) return new();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var ids = string.Join(",", wallets.Select(w => w.WalletId));

        // Purchases totals per wallet.
        // NOTE: fin_purchase_items.AmountPaid is stored in whole currency units (NIS),
        // not cents. We multiply by 100 so RemainingCents arithmetic is consistent with
        // session charges which ARE in cents (ChargedRateCentsPerHour × seconds / 3600).
        var purchCents = new Dictionary<int, long>();
        var purchHours = new Dictionary<int, double>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT p.WalletId,
                       COALESCE(SUM(i.AmountPaid) * 100, 0) AS cents,
                       COALESCE(SUM(i.HoursBought), 0)      AS hrs
                FROM fin_purchases p
                JOIN fin_purchase_items i ON i.PurchaseId = p.PurchaseId
                WHERE p.WalletId IN ({ids})
                  AND (p.IsDeleted IS NULL OR p.IsDeleted = 0)
                  AND p.ApprovedStatus <> 'Rejected'
                GROUP BY p.WalletId";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int wid = r.GetInt32(0);
                purchCents[wid] = r.GetInt64(1);
                purchHours[wid] = r.GetDouble(2);
            }
        }

        // Session charges per wallet (paid sessions only) — uses LengthSeconds + AdminSeconds
        // to match the existing PcService billing convention.
        var sessSec  = new Dictionary<int, long>();
        var sessCents = new Dictionary<int, long>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT WalletId,
                       COALESCE(SUM(CASE WHEN IsFreeSession = 0 THEN LengthSeconds + AdminSeconds ELSE 0 END), 0) AS secs,
                       COALESCE(SUM(CASE WHEN IsFreeSession = 0
                                         THEN ((LengthSeconds + AdminSeconds) * ChargedRateCentsPerHour) / 3600
                                         ELSE 0 END), 0) AS cents
                FROM sess_sessions
                WHERE WalletId IN ({ids})
                GROUP BY WalletId";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int wid = r.GetInt32(0);
                sessSec[wid]   = r.GetInt64(1);
                sessCents[wid] = r.GetInt64(2);
            }
        }

        // Effective rate per wallet:
        //   1. Most recent approved session's ChargedRateCentsPerHour (already cents/hour).
        //   2. Else most recent Auditing purchase's rate (amount/hours × 100 to cents/hour).
        //   3. Else 0.
        var rateCents = new Dictionary<int, int>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT WalletId, ChargedRateCentsPerHour
                FROM (
                    SELECT WalletId, ChargedRateCentsPerHour, SessionId,
                           ROW_NUMBER() OVER (PARTITION BY WalletId ORDER BY SessionId DESC) AS rn
                    FROM sess_sessions
                    WHERE WalletId IN ({ids})
                      AND VerifiedStatus = 'Approved'
                      AND ChargedRateCentsPerHour > 0
                ) WHERE rn = 1";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rateCents[r.GetInt32(0)] = r.GetInt32(1);
        }
        foreach (var w in wallets.Where(w => !rateCents.ContainsKey(w.WalletId)))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT i.AmountPaid, i.HoursBought
                FROM fin_purchases p
                JOIN fin_purchase_items i ON i.PurchaseId = p.PurchaseId
                WHERE p.WalletId = @w
                  AND i.ItemType = 'Auditing'
                  AND i.HoursBought > 0
                  AND (p.IsDeleted IS NULL OR p.IsDeleted = 0)
                  AND p.ApprovedStatus <> 'Rejected'
                ORDER BY p.PurchaseId DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@w", w.WalletId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                double amt = r.GetDouble(0), hrs = r.GetDouble(1);
                if (hrs > 0) rateCents[w.WalletId] = (int)Math.Round(amt / hrs * 100);
            }
        }

        // CS review charges per wallet — only solo CS reviews billed to PC
        // (matches PcService: s.AuditorId IS NULL AND cr.Notes = 'Bill' AND ChargedCentsRatePerHour > 0)
        var crSec   = new Dictionary<int, long>();
        var crCents = new Dictionary<int, long>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cr.WalletId,
                       COALESCE(SUM(cr.ReviewLengthSeconds), 0) AS secs,
                       COALESCE(SUM((cr.ReviewLengthSeconds * cr.ChargedCentsRatePerHour) / 3600), 0) AS cents
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE cr.WalletId IN ({ids})
                  AND s.AuditorId IS NULL
                  AND cr.Notes = 'Bill'
                  AND COALESCE(cr.ChargedCentsRatePerHour, 0) > 0
                GROUP BY cr.WalletId";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int wid = r.GetInt32(0);
                crSec[wid]   = r.GetInt64(1);
                crCents[wid] = r.GetInt64(2);
            }
        }

        return wallets.Select(w => new WalletBalance(
            WalletId:          w.WalletId,
            PcId:              w.PcId,
            Currency:          w.Currency,
            Name:              w.Name,
            IsActive:          w.IsActive,
            PurchasedCents:    purchCents.GetValueOrDefault(w.WalletId),
            PurchasedHours:    purchHours.GetValueOrDefault(w.WalletId),
            UsedSessionCents:  sessCents.GetValueOrDefault(w.WalletId),
            UsedSessionHours:  sessSec.GetValueOrDefault(w.WalletId)   / 3600.0,
            UsedCsReviewCents: crCents.GetValueOrDefault(w.WalletId),
            UsedCsReviewHours: crSec.GetValueOrDefault(w.WalletId)     / 3600.0,
            EffectiveRateCentsPerHour: rateCents.GetValueOrDefault(w.WalletId)
        )).ToList();
    }

    private WalletBalance ComputeBalance(Wallet w) =>
        GetBalancesForPc(w.PcId).First(b => b.WalletId == w.WalletId);

    // ── Transfer ─────────────────────────────────────────────────

    /// <summary>
    /// Transfer an amount (in whole currency units — NIS/EUR/USD, matching
    /// fin_purchase_items.AmountPaid) from a source wallet to a target wallet
    /// (or, if <paramref name="toWalletIdOverride"/> is null, to the target PC's
    /// first matching-currency wallet — auto-created if none exists).
    /// Records two mirror purchases linked via TransferPurchaseId for audit.
    /// </summary>
    public (int SourcePurchaseId, int TargetPurchaseId, int TargetWalletId)
        Transfer(int fromWalletId, int toPcId, long amount, string? notes, int createdByPersonId,
                 int? toWalletIdOverride = null)
    {
        if (amount == 0) throw new InvalidOperationException("Amount must be non-zero.");
        var from = GetWallet(fromWalletId) ?? throw new InvalidOperationException("Source wallet not found.");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        // Locate or create target wallet
        int targetWalletId;
        if (toWalletIdOverride.HasValue)
        {
            // Explicit target wallet — validate it belongs to the target PC and matches currency
            using var ck = conn.CreateCommand();
            ck.Transaction = tx;
            ck.CommandText = "SELECT PcId, Currency FROM fin_wallets WHERE WalletId = @w";
            ck.Parameters.AddWithValue("@w", toWalletIdOverride.Value);
            using var rr = ck.ExecuteReader();
            if (!rr.Read()) throw new InvalidOperationException("Target wallet not found.");
            if (rr.GetInt32(0) != toPcId) throw new InvalidOperationException("Target wallet does not belong to the chosen PC.");
            if (rr.GetString(1) != from.Currency) throw new InvalidOperationException("Target wallet currency does not match source currency.");
            if (toWalletIdOverride.Value == fromWalletId)
                throw new InvalidOperationException("Source and target wallet are the same — nothing to transfer.");
            targetWalletId = toWalletIdOverride.Value;
        }
        else
        {
            using var find = conn.CreateCommand();
            find.Transaction = tx;
            find.CommandText = @"
                SELECT WalletId FROM fin_wallets
                WHERE PcId = @pc AND Currency = @cur AND IsActive = 1
                ORDER BY WalletId LIMIT 1";
            find.Parameters.AddWithValue("@pc",  toPcId);
            find.Parameters.AddWithValue("@cur", from.Currency);
            using var r = find.ExecuteReader();
            if (r.Read())
            {
                targetWalletId = r.GetInt32(0);
                if (targetWalletId == fromWalletId)
                    throw new InvalidOperationException("Source and target wallet are the same — nothing to transfer.");
            }
            else
            {
                r.Close();
                // Pick a name that doesn't collide with an existing (possibly inactive)
                // wallet at the target PC — UNIQUE(PcId, Name) would otherwise throw.
                string candidate = from.Currency + " wallet";
                int suffix = 2;
                while (WalletNameExists(conn, tx, toPcId, candidate))
                    candidate = $"{from.Currency} wallet ({suffix++})";

                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"
                    INSERT INTO fin_wallets (PcId, Currency, Name, Notes, CreatedByPersonId)
                    VALUES (@pc, @cur, @name, @notes, @by);
                    SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("@pc",    toPcId);
                ins.Parameters.AddWithValue("@cur",   from.Currency);
                ins.Parameters.AddWithValue("@name",  candidate);
                ins.Parameters.AddWithValue("@notes", "Created via transfer");
                ins.Parameters.AddWithValue("@by",    createdByPersonId);
                targetWalletId = Convert.ToInt32(ins.ExecuteScalar());
            }
        }

        // Insert source purchase (negative)
        int sourcePurchaseId;
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO fin_purchases (PcId, WalletId, PurchaseDate, Notes, ApprovedStatus, CreatedByPersonId, Currency)
                VALUES (@pc, @w, date('now'), @notes, 'Approved', @by, @cur);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@pc",    from.PcId);
            cmd.Parameters.AddWithValue("@w",     from.WalletId);
            cmd.Parameters.AddWithValue("@notes", "Transfer out: " + (notes ?? ""));
            cmd.Parameters.AddWithValue("@by",    createdByPersonId);
            cmd.Parameters.AddWithValue("@cur",   from.Currency);
            sourcePurchaseId = Convert.ToInt32(cmd.ExecuteScalar());

            using var item = conn.CreateCommand();
            item.Transaction = tx;
            item.CommandText = @"
                INSERT INTO fin_purchase_items (PurchaseId, ItemType, HoursBought, AmountPaid)
                VALUES (@p, 'Transfer', 0, @amt)";
            item.Parameters.AddWithValue("@p",   sourcePurchaseId);
            item.Parameters.AddWithValue("@amt", -amount);
            item.ExecuteNonQuery();
        }

        // Insert target purchase (positive) + link back
        int targetPurchaseId;
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO fin_purchases (PcId, WalletId, PurchaseDate, Notes, ApprovedStatus, CreatedByPersonId, Currency, TransferPurchaseId)
                VALUES (@pc, @w, date('now'), @notes, 'Approved', @by, @cur, @src);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@pc",    toPcId);
            cmd.Parameters.AddWithValue("@w",     targetWalletId);
            cmd.Parameters.AddWithValue("@notes", "Transfer in: " + (notes ?? ""));
            cmd.Parameters.AddWithValue("@by",    createdByPersonId);
            cmd.Parameters.AddWithValue("@cur",   from.Currency);
            cmd.Parameters.AddWithValue("@src",   sourcePurchaseId);
            targetPurchaseId = Convert.ToInt32(cmd.ExecuteScalar());

            using var item = conn.CreateCommand();
            item.Transaction = tx;
            item.CommandText = @"
                INSERT INTO fin_purchase_items (PurchaseId, ItemType, HoursBought, AmountPaid)
                VALUES (@p, 'Transfer', 0, @amt)";
            item.Parameters.AddWithValue("@p",   targetPurchaseId);
            item.Parameters.AddWithValue("@amt", amount);
            item.ExecuteNonQuery();

            // Update source with the mirror link too
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = @"UPDATE fin_purchases SET TransferPurchaseId = @t WHERE PurchaseId = @s";
            upd.Parameters.AddWithValue("@t", targetPurchaseId);
            upd.Parameters.AddWithValue("@s", sourcePurchaseId);
            upd.ExecuteNonQuery();
        }

        tx.Commit();
        return (sourcePurchaseId, targetPurchaseId, targetWalletId);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static bool WalletNameExists(SqliteConnection conn, SqliteTransaction tx, int pcId, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM fin_wallets WHERE PcId = @pc AND Name = @n LIMIT 1";
        cmd.Parameters.AddWithValue("@pc", pcId);
        cmd.Parameters.AddWithValue("@n",  name);
        return cmd.ExecuteScalar() != null;
    }

    private static Wallet ReadWallet(SqliteDataReader r) => new(
        r.GetInt32(0),
        r.GetInt32(1),
        r.GetString(2),
        r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.GetInt32(5) == 1,
        r.GetInt32(6),
        r.GetString(7));

    /// <summary>Update the WalletId of an existing session (used by Work Review).</summary>
    public void SetSessionWallet(int sessionId, int walletId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sess_sessions SET WalletId = @w WHERE SessionId = @s";
        cmd.Parameters.AddWithValue("@w", walletId);
        cmd.Parameters.AddWithValue("@s", sessionId);
        cmd.ExecuteNonQuery();
    }

    public void SetCsReviewWallet(int csReviewId, int walletId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE cs_reviews SET WalletId = @w WHERE CsReviewId = @cr";
        cmd.Parameters.AddWithValue("@w",  walletId);
        cmd.Parameters.AddWithValue("@cr", csReviewId);
        cmd.ExecuteNonQuery();
    }
}
