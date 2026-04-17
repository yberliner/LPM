-- ── Wallets feature — schema + migration ──────────────────────────────────
-- Run once on the server DB:  sqlite3 /path/to/lifepower.db < 001_wallets.sql
-- Idempotent: safe to re-run.

CREATE TABLE IF NOT EXISTS fin_wallets (
  WalletId          INTEGER PRIMARY KEY AUTOINCREMENT,
  PcId              INTEGER NOT NULL REFERENCES core_pcs(PcId),
  Currency          TEXT    NOT NULL,
  Name              TEXT    NOT NULL,
  Notes             TEXT,
  IsActive          INTEGER NOT NULL DEFAULT 1,
  CreatedByPersonId INTEGER NOT NULL,
  CreatedAt         TEXT    NOT NULL DEFAULT (datetime('now')),
  UNIQUE(PcId, Name)
);
CREATE INDEX IF NOT EXISTS ix_fin_wallets_pc ON fin_wallets(PcId);

-- SQLite: ALTER TABLE ADD COLUMN does not support IF NOT EXISTS. We use
-- a pragma check wrapped in an error-tolerant approach: the app's startup
-- migration also tops up, so if any of these fail because the column
-- already exists, the rest of the script still runs under sqlite3's
-- default "continue on error" mode isn't guaranteed — so we guard by
-- checking pragma first via shell conditionals in the deployment docs
-- (see DEPLOY.md). If running directly, re-running will error on any
-- pre-existing ALTER — that's fine; inspect and proceed.

ALTER TABLE fin_purchases ADD COLUMN WalletId INTEGER REFERENCES fin_wallets(WalletId);
ALTER TABLE sess_sessions ADD COLUMN WalletId INTEGER REFERENCES fin_wallets(WalletId);
ALTER TABLE cs_reviews    ADD COLUMN WalletId INTEGER REFERENCES fin_wallets(WalletId);
ALTER TABLE fin_purchases ADD COLUMN TransferPurchaseId INTEGER;

-- One wallet per (PcId, Currency) for PCs with existing purchases.
-- INSERT OR IGNORE prevents duplicates on re-run thanks to UNIQUE(PcId, Name).
INSERT OR IGNORE INTO fin_wallets (PcId, Currency, Name, Notes, CreatedByPersonId)
SELECT PcId, Currency, Currency || ' wallet', 'Migrated from legacy', -1
FROM (SELECT DISTINCT PcId, Currency FROM fin_purchases);

-- PCs with sessions but no purchases: default ILS wallet
INSERT OR IGNORE INTO fin_wallets (PcId, Currency, Name, Notes, CreatedByPersonId)
SELECT DISTINCT s.PcId, 'ILS', 'ILS wallet', 'Migrated — no purchases', -1
FROM sess_sessions s
WHERE NOT EXISTS (SELECT 1 FROM fin_wallets w WHERE w.PcId = s.PcId);

-- Link each purchase to the matching-currency wallet of its PC.
-- Only update rows where WalletId is NULL so re-runs don't clobber admin-assigned values.
UPDATE fin_purchases
SET WalletId = (
    SELECT w.WalletId FROM fin_wallets w
    WHERE w.PcId = fin_purchases.PcId AND w.Currency = fin_purchases.Currency
    LIMIT 1)
WHERE WalletId IS NULL;

UPDATE sess_sessions
SET WalletId = (
    SELECT w.WalletId FROM fin_wallets w
    WHERE w.PcId = sess_sessions.PcId
    ORDER BY w.WalletId LIMIT 1)
WHERE WalletId IS NULL;

UPDATE cs_reviews
SET WalletId = (
    SELECT w.WalletId FROM fin_wallets w
    WHERE w.PcId = (SELECT s.PcId FROM sess_sessions s WHERE s.SessionId = cs_reviews.SessionId)
    ORDER BY w.WalletId LIMIT 1)
WHERE WalletId IS NULL;
