-- ============================================================
-- ERP Software — Incremental migration
-- ============================================================
-- Use this INSTEAD of schema.sql if you already have an erp_db
-- database with data in it and don't want to lose it.
--
-- schema.sql starts with `DROP DATABASE IF EXISTS erp_db`, which wipes
-- everything. This script only ADDS what's missing (new columns, new
-- tables, new seed rows) using IF NOT EXISTS / ON CONFLICT everywhere,
-- so it's safe to run against a database that already has real data,
-- and safe to run more than once.
--
-- Also run this if Trial Balance / Balance Sheet / Profit & Loss / Cash
-- Flow Statement report errors or won't balance — it re-seeds the core
-- accounts (Cash in Hand, Bank Account, Sales, Purchases, etc.) that the
-- Sales/Purchase/Receipt/Payment forms now post their second ledger leg to.
--
-- Also run this to pick up Category Master (Master > Category Master):
-- it creates category_master, backfills it from any free-text category
-- values you already typed on items, and points item_master.category_id
-- at the matching row.
--
-- Run with:
--   psql -U postgres -d erp_db -f Database/migration.sql
-- ============================================================

\c erp_db

-- ---------- company_profile: Settings fields ----------
ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS currency VARCHAR(10) DEFAULT 'PKR';
ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS fiscal_year_start VARCHAR(20) DEFAULT 'January';
ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS enable_notifications BOOLEAN DEFAULT TRUE;
ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS multi_currency BOOLEAN DEFAULT FALSE;

-- ---------- financial_year: Current Year flag ----------
ALTER TABLE financial_year ADD COLUMN IF NOT EXISTS is_current BOOLEAN DEFAULT FALSE;
-- if is_active doesn't exist yet on a very old copy of the table, add it too
ALTER TABLE financial_year ADD COLUMN IF NOT EXISTS is_active BOOLEAN DEFAULT TRUE;

-- ---------- document_numbering: suffix + new document types ----------
ALTER TABLE document_numbering ADD COLUMN IF NOT EXISTS suffix VARCHAR(10) DEFAULT '';

INSERT INTO document_numbering (doc_type, prefix, next_number, padding) VALUES
('INVOICE', 'INV-', 1, 5),
('RECEIPT', 'RCPT-', 1, 5),
('PAYMENT', 'PAY-', 1, 5),
('PURCHASE', 'PB-', 1, 5),
('JOURNAL', 'JV-', 1, 5),
('CONTRA', 'CN-', 1, 5),
('SALES_RETURN', 'SR-', 1, 5),
('PURCHASE_RETURN', 'PR-', 1, 5),
('STOCK_TRANSFER', 'ST-', 1, 5),
('STOCK_ADJUSTMENT', 'ADJ-', 1, 5)
ON CONFLICT (doc_type) DO NOTHING;

-- ---------- Master screens: active/description/etc columns ----------
ALTER TABLE uom_master ADD COLUMN IF NOT EXISTS uom_code VARCHAR(20);
ALTER TABLE uom_master ADD COLUMN IF NOT EXISTS description VARCHAR(250);
ALTER TABLE uom_master ADD COLUMN IF NOT EXISTS active BOOLEAN DEFAULT TRUE;

ALTER TABLE brand_master ADD COLUMN IF NOT EXISTS description VARCHAR(250);
ALTER TABLE brand_master ADD COLUMN IF NOT EXISTS active BOOLEAN DEFAULT TRUE;

ALTER TABLE model_master ADD COLUMN IF NOT EXISTS brand_id INTEGER REFERENCES brand_master(brand_id);
ALTER TABLE model_master ADD COLUMN IF NOT EXISTS description VARCHAR(250);
ALTER TABLE model_master ADD COLUMN IF NOT EXISTS active BOOLEAN DEFAULT TRUE;

ALTER TABLE warehouse_master ADD COLUMN IF NOT EXISTS description VARCHAR(250);
ALTER TABLE warehouse_master ADD COLUMN IF NOT EXISTS active BOOLEAN DEFAULT TRUE;

ALTER TABLE tax_master ADD COLUMN IF NOT EXISTS description VARCHAR(250);
ALTER TABLE tax_master ADD COLUMN IF NOT EXISTS active BOOLEAN DEFAULT TRUE;

ALTER TABLE chart_of_accounts ADD COLUMN IF NOT EXISTS description VARCHAR(250);
ALTER TABLE chart_of_accounts ADD COLUMN IF NOT EXISTS active BOOLEAN DEFAULT TRUE;

ALTER TABLE customer_master ADD COLUMN IF NOT EXISTS credit_limit NUMERIC(18,2) DEFAULT 0;
ALTER TABLE supplier_master ADD COLUMN IF NOT EXISTS credit_limit NUMERIC(18,2) DEFAULT 0;

-- ---------- Sales/Purchase headers: ref/due-date/remarks fields ----------
ALTER TABLE sales_invoice ADD COLUMN IF NOT EXISTS ref_no VARCHAR(30);
ALTER TABLE sales_invoice ADD COLUMN IF NOT EXISTS due_date DATE;

ALTER TABLE purchase_bill ADD COLUMN IF NOT EXISTS ref_no VARCHAR(30);
ALTER TABLE purchase_bill ADD COLUMN IF NOT EXISTS credit_days INTEGER DEFAULT 0;
ALTER TABLE purchase_bill ADD COLUMN IF NOT EXISTS due_date DATE;
ALTER TABLE purchase_bill ADD COLUMN IF NOT EXISTS remarks VARCHAR(250);

ALTER TABLE sales_return ADD COLUMN IF NOT EXISTS remarks VARCHAR(250);
ALTER TABLE purchase_return ADD COLUMN IF NOT EXISTS remarks VARCHAR(250);

-- ---------- New line-item tables for returns (only created if missing) ----------
CREATE TABLE IF NOT EXISTS sales_return_item (
    line_id         SERIAL PRIMARY KEY,
    return_id       INTEGER REFERENCES sales_return(return_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL,
    rate            NUMERIC(18,2) NOT NULL,
    disc_percent    NUMERIC(5,2) DEFAULT 0,
    amount          NUMERIC(18,2) NOT NULL
);

CREATE TABLE IF NOT EXISTS purchase_return_item (
    line_id         SERIAL PRIMARY KEY,
    return_id       INTEGER REFERENCES purchase_return(return_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL,
    rate            NUMERIC(18,2) NOT NULL,
    disc_percent    NUMERIC(5,2) DEFAULT 0,
    amount          NUMERIC(18,2) NOT NULL
);

-- ---------- Stock Transfer / Stock Adjustment header + item tables ----------
CREATE TABLE IF NOT EXISTS stock_transfer (
    transfer_id     SERIAL PRIMARY KEY,
    transfer_no     VARCHAR(30) UNIQUE NOT NULL,
    transfer_date   DATE NOT NULL DEFAULT CURRENT_DATE,
    from_warehouse_id INTEGER REFERENCES warehouse_master(warehouse_id),
    to_warehouse_id   INTEGER REFERENCES warehouse_master(warehouse_id),
    remarks         VARCHAR(250),
    created_at      TIMESTAMP DEFAULT now()
);

CREATE TABLE IF NOT EXISTS stock_transfer_item (
    line_id         SERIAL PRIMARY KEY,
    transfer_id     INTEGER REFERENCES stock_transfer(transfer_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL
);

CREATE TABLE IF NOT EXISTS stock_adjustment (
    adjustment_id   SERIAL PRIMARY KEY,
    adjustment_no   VARCHAR(30) UNIQUE NOT NULL,
    adjustment_date DATE NOT NULL DEFAULT CURRENT_DATE,
    adjustment_type VARCHAR(10) NOT NULL,
    warehouse_id    INTEGER REFERENCES warehouse_master(warehouse_id),
    remarks         VARCHAR(250),
    created_at      TIMESTAMP DEFAULT now()
);

CREATE TABLE IF NOT EXISTS stock_adjustment_item (
    line_id         SERIAL PRIMARY KEY,
    adjustment_id   INTEGER REFERENCES stock_adjustment(adjustment_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL,
    rate            NUMERIC(18,2) DEFAULT 0,
    amount          NUMERIC(18,2) DEFAULT 0
);

-- ---------- Journal Voucher line items ----------
CREATE TABLE IF NOT EXISTS journal_voucher_item (
    line_id         SERIAL PRIMARY KEY,
    journal_id      INTEGER REFERENCES journal_voucher(journal_id) ON DELETE CASCADE,
    account_id      INTEGER REFERENCES chart_of_accounts(account_id),
    description     VARCHAR(250),
    debit           NUMERIC(18,2) DEFAULT 0,
    credit          NUMERIC(18,2) DEFAULT 0
);

-- ---------- Contra Entry narration ----------
ALTER TABLE contra_entry ADD COLUMN IF NOT EXISTS narration VARCHAR(250);

-- ---------- Item Master: the columns causing the "column does not exist" error ----------
ALTER TABLE item_master ADD COLUMN IF NOT EXISTS item_type VARCHAR(30) DEFAULT 'Stock Item';
ALTER TABLE item_master ADD COLUMN IF NOT EXISTS category VARCHAR(100); -- old free-text column, kept for backfill below
ALTER TABLE item_master ADD COLUMN IF NOT EXISTS barcode VARCHAR(50);
ALTER TABLE item_master ADD COLUMN IF NOT EXISTS purchase_price NUMERIC(18,2) DEFAULT 0;

-- ---------- Category Master ----------
-- Category used to be a free-text column on item_master; it's now a proper
-- master table (Master > Category Master) like Brand/UOM/Warehouse.
CREATE TABLE IF NOT EXISTS category_master (
    category_id     SERIAL PRIMARY KEY,
    category_name   VARCHAR(100) NOT NULL UNIQUE,
    description     VARCHAR(250),
    active          BOOLEAN DEFAULT TRUE
);

INSERT INTO category_master (category_name) VALUES
('Tiles'), ('Marble & Granite'), ('Sanitary Ware'), ('Pipes & Fittings'), ('Cement & Building Materials')
ON CONFLICT (category_name) DO NOTHING;

ALTER TABLE item_master ADD COLUMN IF NOT EXISTS category_id INTEGER REFERENCES category_master(category_id);

-- Backfill: turn any existing free-text category values into category_master rows,
-- then point each item at the matching row. Safe to run more than once.
INSERT INTO category_master (category_name)
SELECT DISTINCT category FROM item_master WHERE category IS NOT NULL AND category <> ''
ON CONFLICT (category_name) DO NOTHING;

UPDATE item_master im
SET category_id = cm.category_id
FROM category_master cm
WHERE im.category = cm.category_name
  AND im.category_id IS NULL
  AND im.category IS NOT NULL AND im.category <> '';

-- ---------- Core accounts the double-entry postings depend on ----------
-- Sales/Purchase/Receipt/Payment now post two-sided ledger entries (see the
-- "double-entry fix" below) and look these up by account_code. Seed them if
-- your database predates them or they were somehow removed.
INSERT INTO chart_of_accounts (account_code, account_name, account_type, balance_type) VALUES
('1000', 'Cash in Hand', 'ASSET', 'Dr'),
('1001', 'Bank Account', 'ASSET', 'Dr'),
('1100', 'Accounts Receivable', 'ASSET', 'Dr'),
('2100', 'Accounts Payable', 'LIABILITY', 'Cr'),
('4000', 'Sales', 'INCOME', 'Cr'),
('5000', 'Purchases', 'EXPENSE', 'Dr')
ON CONFLICT (account_code) DO NOTHING;

-- ============================================================
-- PER-WAREHOUSE STOCK BALANCES
-- ============================================================
-- stock_balance moves from one row per item to one row per (item, warehouse).
-- Safe to run more than once: the rebuild only happens when the primary key
-- is still the old single-column (item_id) one.

-- 1. Make sure there's a default warehouse (used for backfill and by the app
--    as the fallback for new items' opening stock).
INSERT INTO warehouse_master (warehouse_name, location, description, active)
SELECT 'Main Warehouse', 'Head Office', 'Default warehouse', TRUE
WHERE NOT EXISTS (SELECT 1 FROM warehouse_master WHERE warehouse_name = 'Main Warehouse');

-- 2. Rebuild stock_balance with a composite (item_id, warehouse_id) key if it
--    still has the old single-column key. Existing rows land in the default
--    warehouse; items with opening stock but no balance row get their opening
--    qty there too. The old view is dropped first because it references
--    stock_balance and would otherwise block the rebuild; it's recreated below.
DROP VIEW IF EXISTS vw_item_list;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_index i
        JOIN pg_class c ON c.oid = i.indrelid
        WHERE c.relname = 'stock_balance' AND i.indisprimary
        GROUP BY i.indexrelid
        HAVING COUNT(*) > 1  -- already keyed on (item_id, warehouse_id)
    ) THEN
        ALTER TABLE stock_balance RENAME TO stock_balance_old;

        CREATE TABLE stock_balance (
            item_id         INTEGER NOT NULL REFERENCES item_master(item_id),
            warehouse_id    INTEGER NOT NULL REFERENCES warehouse_master(warehouse_id),
            qty_on_hand     NUMERIC(18,3) DEFAULT 0,
            PRIMARY KEY (item_id, warehouse_id)
        );

        INSERT INTO stock_balance (item_id, warehouse_id, qty_on_hand)
        SELECT item_id, (SELECT warehouse_id FROM warehouse_master ORDER BY warehouse_id LIMIT 1), qty_on_hand
        FROM stock_balance_old;

        -- items that had opening stock but no balance row yet
        INSERT INTO stock_balance (item_id, warehouse_id, qty_on_hand)
        SELECT i.item_id, (SELECT warehouse_id FROM warehouse_master ORDER BY warehouse_id LIMIT 1), i.opening_qty
        FROM item_master i
        WHERE i.opening_qty <> 0
          AND NOT EXISTS (SELECT 1 FROM stock_balance s WHERE s.item_id = i.item_id);

        DROP TABLE stock_balance_old;
    END IF;
END $$;

-- ============================================================
-- BANK ACCOUNTS
-- ============================================================
-- Receipts/Payments can now pick any BANK-type account for non-cash modes
-- instead of always posting to 1001. Convert the seeded Bank Account (and any
-- other obvious bank accounts a user created as plain assets) to the new type.
UPDATE chart_of_accounts SET account_type='BANK'
WHERE account_type='ASSET' AND (account_code='1001' OR account_name ILIKE '%bank%');

-- 3. vw_item_list now totals stock across warehouses (was a plain join that
--    would have duplicated rows once an item had more than one balance row).
CREATE OR REPLACE VIEW vw_item_list AS
SELECT
    i.item_id,
    i.item_name,
    i.model,
    i.side_size,
    b.brand_id,
    b.brand_name,
    u.uom_id,
    u.uom_name,
    cm.category_id,
    cm.category_name AS category,
    i.barcode,
    i.item_type,
    COALESCE(s.qty_on_hand, i.opening_qty) AS qty,
    i.rate,
    i.purchase_price,
    0::numeric(5,2) AS disc_percent,
    COALESCE(s.qty_on_hand, i.opening_qty) * i.rate AS amount,
    i.min_stock,
    i.hsn_code,
    CASE WHEN i.active THEN 'Active' ELSE 'Inactive' END AS status
FROM item_master i
LEFT JOIN brand_master b ON b.brand_id = i.brand_id
LEFT JOIN uom_master u ON u.uom_id = i.uom_id
LEFT JOIN category_master cm ON cm.category_id = i.category_id
-- stock_balance is now keyed on (item_id, warehouse_id); total across warehouses
LEFT JOIN (SELECT item_id, SUM(qty_on_hand) AS qty_on_hand FROM stock_balance GROUP BY item_id) s ON s.item_id = i.item_id;

-- ============================================================
-- Done. Verify with:  \d item_master   (should show category_id/barcode/item_type/purchase_price)
--                     \d stock_balance (PK should be (item_id, warehouse_id))
--                     \d warehouse_master  (should include the Main Warehouse seed)
-- ============================================================
