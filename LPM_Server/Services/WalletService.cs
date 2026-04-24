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

    public void UpdateWallet(int walletId, string name, string? notes, bool isActive, string? newCurrency = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // If caller wants a currency change, validate safety server-side.
        if (!string.IsNullOrWhiteSpace(newCurrency))
        {
            newCurrency = newCurrency.Trim().ToUpperInvariant();
            if (newCurrency is not ("ILS" or "EUR" or "USD"))
                throw new InvalidOperationException($"Unsupported currency '{newCurrency}'.");

            using var curCmd = conn.CreateCommand();
            curCmd.CommandText = "SELECT Currency FROM fin_wallets WHERE WalletId = @id";
            curCmd.Parameters.AddWithValue("@id", walletId);
            var oldCurrency = curCmd.ExecuteScalar() as string
                              ?? throw new InvalidOperationException("Wallet not found.");
            if (!string.Equals(oldCurrency, newCurrency, StringComparison.OrdinalIgnoreCase))
            {
                if (!CanChangeCurrency(walletId, out var reason))
                    throw new InvalidOperationException(reason ?? "Currency cannot be changed — wallet has charges.");
            }
            else
            {
                newCurrency = null; // no-op
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = newCurrency == null
            ? @"UPDATE fin_wallets
                   SET Name = @name, Notes = @notes, IsActive = @active
                 WHERE WalletId = @id"
            : @"UPDATE fin_wallets
                   SET Name = @name, Notes = @notes, IsActive = @active, Currency = @cur
                 WHERE WalletId = @id";
        cmd.Parameters.AddWithValue("@id",    walletId);
        cmd.Parameters.AddWithValue("@name",  name);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        if (newCurrency != null) cmd.Parameters.AddWithValue("@cur", newCurrency);
        cmd.ExecuteNonQuery();
    }

    // ── References / safety checks ───────────────────────────────

    /// <summary>One row that references a wallet. Used to show the user what
    /// is blocking a currency change or a wallet delete.</summary>
    public record WalletReferenceRow(
        string Table,         // "Purchases" | "Sessions" | "CsReviews"
        int    RowId,
        string Summary,
        bool   HasCharge,     // true = blocks currency change (non-zero money flow)
        string DateText,      // ISO date-ish for sorting/display
        bool   IsDeleted);    // soft-deleted rows (purchases only)

    /// <summary>Enumerate every row in purchases, sessions, and cs_reviews that
    /// references this wallet — directly (WalletId = W) and, when W is the PC's
    /// first-active wallet, via the NULL-WalletId fallback attribution.
    /// HasCharge flags rows that actually moved money in the wallet's currency.</summary>
    public IReadOnlyList<WalletReferenceRow> GetWalletReferences(int walletId)
    {
        var wallet = GetWallet(walletId);
        if (wallet == null) return Array.Empty<WalletReferenceRow>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Is this the PC's first-active wallet? If so, NULL-WalletId rows on the same PC attribute here.
        bool isFirstActive;
        using (var chk = conn.CreateCommand())
        {
            chk.CommandText = @"
                SELECT WalletId FROM fin_wallets
                WHERE PcId = @pc AND IsActive = 1
                ORDER BY WalletId LIMIT 1";
            chk.Parameters.AddWithValue("@pc", wallet.PcId);
            var firstActive = chk.ExecuteScalar() as long?;
            isFirstActive = firstActive.HasValue && (int)firstActive.Value == walletId;
        }

        var rows = new List<WalletReferenceRow>();

        // ── Purchases ─────────────────────────────────────────────
        // SUM(items.AmountPaid) gives the header amount; zero → no monetary effect.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT p.PurchaseId, p.PurchaseDate, p.IsDeleted,
                       COALESCE(SUM(pi.AmountPaid), 0) AS TotalAmt,
                       COUNT(pi.PurchaseItemId)        AS NItems
                FROM fin_purchases p
                LEFT JOIN fin_purchase_items pi ON pi.PurchaseId = p.PurchaseId
                WHERE p.WalletId = @w
                   {(isFirstActive ? "OR (p.WalletId IS NULL AND p.PcId = @pc)" : "")}
                GROUP BY p.PurchaseId, p.PurchaseDate, p.IsDeleted
                ORDER BY p.PurchaseDate DESC, p.PurchaseId DESC";
            cmd.Parameters.AddWithValue("@w", walletId);
            if (isFirstActive) cmd.Parameters.AddWithValue("@pc", wallet.PcId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id        = r.GetInt32(0);
                string date   = r.IsDBNull(1) ? "" : r.GetString(1);
                bool deleted  = r.GetInt32(2) == 1;
                long amt      = r.GetInt64(3);
                int nItems    = r.GetInt32(4);

                // Soft-deleted purchases never count as "charged" — they blocked delete
                // only by the existing TryDeleteWallet row-count rule; their money effect
                // is gone from the balance aggregation.
                bool hasCharge = !deleted && amt != 0;
                var summary = $"#{id} · {nItems} item{(nItems == 1 ? "" : "s")} · {wallet.Currency} {amt:N0}"
                            + (deleted ? " · deleted" : "");
                rows.Add(new WalletReferenceRow("Purchases", id, summary, hasCharge, date, deleted));
            }
        }

        // ── Sessions ──────────────────────────────────────────────
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT s.SessionId, s.SessionDate, s.ChargeSeconds, s.ChargedRateCentsPerHour
                FROM sess_sessions s
                WHERE s.WalletId = @w
                   {(isFirstActive ? "OR (s.WalletId IS NULL AND s.PcId = @pc)" : "")}
                ORDER BY s.SessionDate DESC, s.SessionId DESC";
            cmd.Parameters.AddWithValue("@w", walletId);
            if (isFirstActive) cmd.Parameters.AddWithValue("@pc", wallet.PcId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id      = r.GetInt32(0);
                string date = r.IsDBNull(1) ? "" : r.GetString(1);
                int chSec   = r.GetInt32(2);
                int rate    = r.GetInt32(3);
                long cents  = (long)chSec * rate / 3600;
                bool hasCharge = cents > 0;
                string timeStr = FormatHms(chSec);
                string rateStr = rate > 0 ? $" @ {wallet.Currency} {rate/100.0:N0}/h" : "";
                string costStr = hasCharge ? $" = {wallet.Currency} {cents/100.0:N2}" : "";
                rows.Add(new WalletReferenceRow(
                    "Sessions", id, $"#{id} · {timeStr}{rateStr}{costStr}", hasCharge, date, false));
            }
        }

        // ── CS reviews ────────────────────────────────────────────
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT cr.CsReviewId, cr.ReviewedAt, cr.ReviewLengthSeconds, cr.ChargedCentsRatePerHour
                FROM cs_reviews cr
                { (isFirstActive
                    ? "LEFT JOIN sess_sessions s ON s.SessionId = cr.SessionId"
                    : "") }
                WHERE cr.WalletId = @w
                   {(isFirstActive ? "OR (cr.WalletId IS NULL AND s.PcId = @pc)" : "")}
                ORDER BY cr.ReviewedAt DESC, cr.CsReviewId DESC";
            cmd.Parameters.AddWithValue("@w", walletId);
            if (isFirstActive) cmd.Parameters.AddWithValue("@pc", wallet.PcId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id      = r.GetInt32(0);
                string date = r.IsDBNull(1) ? "" : r.GetString(1);
                int sec     = r.GetInt32(2);
                int rate    = r.GetInt32(3);
                long cents  = (long)sec * rate / 3600;
                bool hasCharge = cents > 0;
                string timeStr = FormatHms(sec);
                string rateStr = rate > 0 ? $" @ {wallet.Currency} {rate/100.0:N0}/h" : "";
                string costStr = hasCharge ? $" = {wallet.Currency} {cents/100.0:N2}" : "";
                rows.Add(new WalletReferenceRow(
                    "CsReviews", id, $"#{id} · {timeStr}{rateStr}{costStr}", hasCharge, date, false));
            }
        }

        // Charged first, then by date desc (SQL already ordered each group by date desc)
        return rows
            .OrderByDescending(x => x.HasCharge)
            .ThenBy(x => x.Table)
            .ThenByDescending(x => x.DateText)
            .ToList();
    }

    /// <summary>True iff no row on this wallet has produced a non-zero monetary effect.</summary>
    public bool CanChangeCurrency(int walletId, out string? reason)
    {
        var refs = GetWalletReferences(walletId);
        var charged = refs.Where(r => r.HasCharge).ToList();
        if (charged.Count == 0) { reason = null; return true; }

        int pCount  = charged.Count(r => r.Table == "Purchases");
        int sCount  = charged.Count(r => r.Table == "Sessions");
        int crCount = charged.Count(r => r.Table == "CsReviews");
        var parts = new List<string>();
        if (pCount  > 0) parts.Add($"{pCount} purchase{(pCount==1?"":"s")}");
        if (sCount  > 0) parts.Add($"{sCount} session{(sCount==1?"":"s")}");
        if (crCount > 0) parts.Add($"{crCount} CS review{(crCount==1?"":"s")}");
        reason = "Currency locked — " + string.Join(", ", parts) + " charged on this wallet.";
        return false;
    }

    private static string FormatHms(int seconds)
    {
        if (seconds <= 0) return "0:00";
        int h = seconds / 3600;
        int m = (seconds % 3600) / 60;
        return h > 0 ? $"{h}:{m:D2}:00" : $"0:{m:D2}";
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

    /// <summary>
    /// Auto-picks the target wallet for a delete-with-reassign. Prefers the PC's
    /// lowest-WalletId active wallet (matching the "first-active" attribution rule
    /// used by balance aggregation). Falls back to the lowest-WalletId inactive one.
    /// Returns null if no sibling wallet exists.
    /// </summary>
    public int? ResolveReassignTarget(int sourceWalletId)
    {
        var src = GetWallet(sourceWalletId);
        if (src == null) return null;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT WalletId FROM fin_wallets
            WHERE PcId = @pc AND WalletId != @id
            ORDER BY IsActive DESC, WalletId
            LIMIT 1";
        cmd.Parameters.AddWithValue("@pc", src.PcId);
        cmd.Parameters.AddWithValue("@id", sourceWalletId);
        var r = cmd.ExecuteScalar();
        if (r == null || r == DBNull.Value) return null;
        return Convert.ToInt32(r);
    }

    /// <summary>
    /// Delete a wallet after moving every FK reference to <paramref name="toWalletId"/>.
    /// Only allowed when the source wallet has no charged rows (CanChangeCurrency),
    /// since migrating any zero-charge row across currencies changes attribution by
    /// exactly 0. Atomic — all UPDATEs + DELETE run inside one transaction, and
    /// CanChangeCurrency is re-checked inside it to close the open-modal race.
    /// </summary>
    public bool TryDeleteWalletWithReassign(int fromWalletId, int toWalletId, int byUserId, out string? errorMessage)
    {
        errorMessage = null;

        // Lightweight validations before taking a transaction.
        var src = GetWallet(fromWalletId);
        if (src == null) { errorMessage = "Wallet not found."; return false; }
        if (fromWalletId == toWalletId) { errorMessage = "Target must be a different wallet."; return false; }
        var dst = GetWallet(toWalletId);
        if (dst == null)                  { errorMessage = "Target wallet not found."; return false; }
        if (dst.PcId != src.PcId)         { errorMessage = "Target wallet belongs to a different PC."; return false; }

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // BEGIN IMMEDIATE: take the RESERVED lock up front so the re-check below and
        // all subsequent UPDATEs/DELETE happen atomically with respect to other writers.
        // Without this, another connection could commit a charge between our check and
        // our first UPDATE, and we'd delete a wallet that silently became unsafe.
        using var tx = conn.BeginTransaction(System.Data.IsolationLevel.Serializable, deferred: false);

        // Re-check "no charged rows" now that we hold the writer lock. `CanChangeCurrency`
        // opens its own connection; that's fine — it reads the DB as-committed, and
        // because we hold RESERVED, no other writer can commit between this check and
        // our own writes.
        if (!CanChangeCurrency(fromWalletId, out var why))
        {
            errorMessage = why ?? "Wallet has charges — cannot delete.";
            tx.Rollback();
            return false;
        }

        int pMoved, sMoved, crMoved;
        using (var u1 = conn.CreateCommand())
        {
            u1.Transaction = tx;
            u1.CommandText = "UPDATE fin_purchases SET WalletId = @to WHERE WalletId = @from";
            u1.Parameters.AddWithValue("@to",   toWalletId);
            u1.Parameters.AddWithValue("@from", fromWalletId);
            pMoved = u1.ExecuteNonQuery();
        }
        using (var u2 = conn.CreateCommand())
        {
            u2.Transaction = tx;
            u2.CommandText = "UPDATE sess_sessions SET WalletId = @to WHERE WalletId = @from";
            u2.Parameters.AddWithValue("@to",   toWalletId);
            u2.Parameters.AddWithValue("@from", fromWalletId);
            sMoved = u2.ExecuteNonQuery();
        }
        using (var u3 = conn.CreateCommand())
        {
            u3.Transaction = tx;
            u3.CommandText = "UPDATE cs_reviews SET WalletId = @to WHERE WalletId = @from";
            u3.Parameters.AddWithValue("@to",   toWalletId);
            u3.Parameters.AddWithValue("@from", fromWalletId);
            crMoved = u3.ExecuteNonQuery();
        }
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM fin_wallets WHERE WalletId = @id";
            del.Parameters.AddWithValue("@id", fromWalletId);
            del.ExecuteNonQuery();
        }

        tx.Commit();
        Console.WriteLine(
            $"[WalletSvc] Deleted wallet {fromWalletId} (PC {src.PcId}, '{src.Currency} {src.Name}'); " +
            $"moved {pMoved} purchases + {sMoved} sessions + {crMoved} CS reviews to wallet {toWalletId} " +
            $"('{dst.Currency} {dst.Name}') by userId={byUserId}");
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
        var walletSet = wallets.Select(w => w.WalletId).ToHashSet();
        int? firstActiveWalletId = wallets.Where(w => w.IsActive)
                                          .Select(w => (int?)w.WalletId)
                                          .FirstOrDefault();

        // Budget reset date for this PC (applied to purchases and sessions).
        string? resetDate;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT ResetDate FROM fin_budget_reset
                                WHERE PcId = @pc AND IsActive = 1
                                ORDER BY ResetDate DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@pc", pcId);
            resetDate = cmd.ExecuteScalar() as string;
        }
        string rdPurchFilter  = resetDate != null ? " AND p.PurchaseDate >= @rd"  : "";
        string rdPurchFilter2 = resetDate != null ? " AND p2.PurchaseDate >= @rd" : "";
        string rdSessFilter   = resetDate != null ? " AND s.SessionDate >= @rd"   : "";

        // A. Purchases per wallet — Auditing + Transfer items (transfers move balance between
        // wallets and must be reflected). Book/Course/etc. are excluded so non-audit purchases
        // don't inflate Hours Left. Reset-date filtered; Rejected excluded.
        // NULL WalletId purchases on this PC are attributed to the first active wallet (mirrors
        // the orphan-session handling so legacy pre-wallet data is not silently lost).
        // fin_purchase_items.AmountPaid is stored in whole currency units; multiply by 100 for cents.
        int fallbackWalletId = firstActiveWalletId ?? -1;
        var purchCents = new Dictionary<int, long>();
        var purchHours = new Dictionary<int, double>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT COALESCE(p.WalletId, @fallback) AS EffWalletId,
                       COALESCE(SUM(i.AmountPaid) * 100, 0) AS cents,
                       COALESCE(SUM(i.HoursBought), 0)      AS hrs
                FROM fin_purchases p
                JOIN fin_purchase_items i ON i.PurchaseId = p.PurchaseId
                WHERE p.PcId = @pc
                  AND COALESCE(p.WalletId, @fallback) IN ({ids})
                  AND (p.IsDeleted IS NULL OR p.IsDeleted = 0)
                  AND p.ApprovedStatus <> 'Rejected'
                  AND i.ItemType IN ('Auditing', 'Transfer')
                  {rdPurchFilter}
                GROUP BY EffWalletId";
            cmd.Parameters.AddWithValue("@pc", pcId);
            cmd.Parameters.AddWithValue("@fallback", fallbackWalletId);
            if (resetDate != null) cmd.Parameters.AddWithValue("@rd", resetDate);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int wid = r.GetInt32(0);
                if (!walletSet.Contains(wid)) continue; // safety — shouldn't happen
                purchCents[wid] = r.GetInt64(1);
                purchHours[wid] = r.GetDouble(2);
            }
        }

        // Per-wallet last-Auditing-purchase rate (cents/hr) — used both as fallback inside the
        // zero-rate session chain and as the effective rate when no rated session exists.
        // Sums all Auditing items of the latest qualifying purchase (matches old PcService logic).
        // NULL-WalletId purchases on this PC are attributed to the first active wallet, consistent
        // with the aggregation above.
        var lastPurchaseRateCents = new Dictionary<int, int>();
        foreach (var w in wallets)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT SUM(i.AmountPaid), SUM(i.HoursBought)
                FROM fin_purchase_items i
                JOIN fin_purchases p ON p.PurchaseId = i.PurchaseId
                WHERE p.PcId = @pc
                  AND COALESCE(p.WalletId, @fallback) = @w
                  AND i.ItemType = 'Auditing'
                  AND (p.IsDeleted IS NULL OR p.IsDeleted = 0)
                  AND p.ApprovedStatus <> 'Rejected'
                  {rdPurchFilter}
                  AND p.PurchaseId = (
                      SELECT p2.PurchaseId
                      FROM fin_purchases p2
                      JOIN fin_purchase_items i2 ON i2.PurchaseId = p2.PurchaseId
                      WHERE p2.PcId = @pc
                        AND COALESCE(p2.WalletId, @fallback) = @w
                        AND i2.ItemType = 'Auditing'
                        AND (p2.IsDeleted IS NULL OR p2.IsDeleted = 0)
                        AND p2.ApprovedStatus <> 'Rejected'
                        {rdPurchFilter2}
                      GROUP BY p2.PurchaseId
                      HAVING SUM(i2.AmountPaid) <> 0
                      ORDER BY p2.PurchaseId DESC LIMIT 1
                  )";
            cmd.Parameters.AddWithValue("@pc", pcId);
            cmd.Parameters.AddWithValue("@w", w.WalletId);
            cmd.Parameters.AddWithValue("@fallback", fallbackWalletId);
            if (resetDate != null) cmd.Parameters.AddWithValue("@rd", resetDate);
            using var r = cmd.ExecuteReader();
            if (r.Read() && !r.IsDBNull(0) && !r.IsDBNull(1))
            {
                double amt = r.GetDouble(0), hrs = r.GetDouble(1);
                if (Math.Abs(hrs) > 0) lastPurchaseRateCents[w.WalletId] = (int)Math.Round(Math.Abs(amt / hrs) * 100);
            }
        }

        // B. Billable sessions (AuditorId IS NOT NULL, IsFreeSession = 0).
        // NULL WalletId is attributed to the PC's first active wallet so orphan history isn't lost.
        var sessionsByWallet = new Dictionary<int, List<(int rate, int admin, int length)>>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT s.SessionId,
                       s.WalletId,
                       COALESCE(s.ChargedRateCentsPerHour, 0),
                       COALESCE(s.AdminSeconds, 0),
                       s.LengthSeconds
                FROM sess_sessions s
                WHERE s.PcId = @pc
                  AND s.AuditorId IS NOT NULL
                  AND s.IsFreeSession = 0
                  {rdSessFilter}
                ORDER BY s.SessionId";
            cmd.Parameters.AddWithValue("@pc", pcId);
            if (resetDate != null) cmd.Parameters.AddWithValue("@rd", resetDate);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int? rowWallet = r.IsDBNull(1) ? null : r.GetInt32(1);
                int? effWalletN = rowWallet ?? firstActiveWalletId;
                if (effWalletN is not int eff || !walletSet.Contains(eff)) continue;
                if (!sessionsByWallet.ContainsKey(eff)) sessionsByWallet[eff] = new();
                sessionsByWallet[eff].Add((r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)));
            }
        }

        // Per-wallet: apply the old backward-looking rate fallback chain (matches
        // PcService.GetAllPcBalances' pooled logic, now scoped per wallet). Also derive the
        // effective rate = last session with rate > 0 on that wallet (regardless of approval),
        // else the last-purchase rate, else 0.
        var usedSessionCents = new Dictionary<int, long>();
        var usedSessionSecs  = new Dictionary<int, long>();
        var effectiveRate    = new Dictionary<int, int>();
        foreach (var kv in sessionsByWallet)
        {
            int wid = kv.Key;
            int lastSeenRate = 0;
            int lastRatedSessionRate = 0;
            decimal centsAcc = 0m;
            long secsAcc = 0;
            int purchaseRate = lastPurchaseRateCents.GetValueOrDefault(wid);

            foreach (var s in kv.Value)
            {
                int rate;
                if (s.rate > 0) { rate = s.rate; lastSeenRate = s.rate; lastRatedSessionRate = s.rate; }
                else            { rate = lastSeenRate > 0 ? lastSeenRate : purchaseRate; }
                centsAcc += (decimal)rate * (s.admin + s.length) / 3600m;
                secsAcc  += s.admin + s.length;
            }

            usedSessionCents[wid] = (long)Math.Round(centsAcc);
            usedSessionSecs[wid]  = secsAcc;
            effectiveRate[wid]    = lastRatedSessionRate > 0 ? lastRatedSessionRate : purchaseRate;
        }
        // Wallets with no sessions still need an effective rate from the last purchase.
        foreach (var w in wallets)
            if (!effectiveRate.ContainsKey(w.WalletId))
                effectiveRate[w.WalletId] = lastPurchaseRateCents.GetValueOrDefault(w.WalletId);

        // C. Solo CS reviews billed to PC.
        // Effective wallet = cr.WalletId ?? s.WalletId ?? first-active wallet.
        // Accumulate cents in decimal, round once at the end (fixes per-row integer-division drift).
        var crCentsAcc = new Dictionary<int, decimal>();
        var crSecs     = new Dictionary<int, long>();
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT cr.WalletId,
                       s.WalletId,
                       cr.ChargedCentsRatePerHour,
                       COALESCE(cr.ReviewLengthSeconds, 0)
                FROM cs_reviews cr
                JOIN sess_sessions s ON s.SessionId = cr.SessionId
                WHERE s.PcId = @pc
                  AND s.AuditorId IS NULL
                  AND (cr.Notes IS NULL OR cr.Notes <> 'Free')
                  AND COALESCE(cr.ChargedCentsRatePerHour, 0) > 0
                  {rdSessFilter}";
            cmd.Parameters.AddWithValue("@pc", pcId);
            if (resetDate != null) cmd.Parameters.AddWithValue("@rd", resetDate);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int? crWallet = r.IsDBNull(0) ? null : r.GetInt32(0);
                int? sWallet  = r.IsDBNull(1) ? null : r.GetInt32(1);
                int? effWalletN = crWallet ?? sWallet ?? firstActiveWalletId;
                if (effWalletN is not int eff || !walletSet.Contains(eff)) continue;

                int rate = r.GetInt32(2);
                int len  = r.GetInt32(3);
                crCentsAcc[eff] = crCentsAcc.GetValueOrDefault(eff) + (decimal)rate * len / 3600m;
                crSecs[eff]     = crSecs.GetValueOrDefault(eff) + len;
            }
        }
        var crCents = crCentsAcc.ToDictionary(kv => kv.Key, kv => (long)Math.Round(kv.Value));

        return wallets.Select(w => new WalletBalance(
            WalletId:          w.WalletId,
            PcId:              w.PcId,
            Currency:          w.Currency,
            Name:              w.Name,
            IsActive:          w.IsActive,
            PurchasedCents:    purchCents.GetValueOrDefault(w.WalletId),
            PurchasedHours:    purchHours.GetValueOrDefault(w.WalletId),
            UsedSessionCents:  usedSessionCents.GetValueOrDefault(w.WalletId),
            UsedSessionHours:  usedSessionSecs.GetValueOrDefault(w.WalletId) / 3600.0,
            UsedCsReviewCents: crCents.GetValueOrDefault(w.WalletId),
            UsedCsReviewHours: crSecs.GetValueOrDefault(w.WalletId) / 3600.0,
            EffectiveRateCentsPerHour: effectiveRate.GetValueOrDefault(w.WalletId)
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
                VALUES (@pc, @w, date('now', 'localtime'), @notes, 'Approved', @by, @cur);
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
                VALUES (@pc, @w, date('now', 'localtime'), @notes, 'Approved', @by, @cur, @src);
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
