-- ============================================================
-- ERP Software - Inventory & Accounting System
-- PostgreSQL Database Schema
-- ============================================================
-- Run as: psql -U postgres -f schema.sql
-- ============================================================

DROP DATABASE IF EXISTS erp_db;
CREATE DATABASE erp_db;

\c erp_db

-- ============================================================
-- MASTER TABLES
-- ============================================================

CREATE TABLE company_profile (
    company_id      SERIAL PRIMARY KEY,
    company_name    VARCHAR(150) NOT NULL,
    address         VARCHAR(250),
    phone           VARCHAR(50),
    email           VARCHAR(100),
    ntn             VARCHAR(50),
    strn            VARCHAR(50),
    logo_path       VARCHAR(250),
    currency            VARCHAR(10) DEFAULT 'PKR',
    fiscal_year_start   VARCHAR(20) DEFAULT 'January',
    enable_notifications BOOLEAN DEFAULT TRUE,
    multi_currency      BOOLEAN DEFAULT FALSE
);

CREATE TABLE financial_year (
    fy_id           SERIAL PRIMARY KEY,
    fy_name         VARCHAR(20) NOT NULL,      -- e.g. 2025-2026
    start_date      DATE NOT NULL,
    end_date        DATE NOT NULL,
    is_current      BOOLEAN DEFAULT FALSE,
    is_active       BOOLEAN DEFAULT TRUE
);

CREATE TABLE document_numbering (
    doc_type        VARCHAR(30) PRIMARY KEY,   -- INVOICE, RECEIPT, PAYMENT, PURCHASE, JOURNAL, CONTRA
    prefix          VARCHAR(10) NOT NULL,
    suffix          VARCHAR(10) DEFAULT '',
    next_number     INTEGER NOT NULL DEFAULT 1,
    padding         INTEGER NOT NULL DEFAULT 5
);

CREATE TABLE uom_master (
    uom_id          SERIAL PRIMARY KEY,
    uom_name        VARCHAR(20) NOT NULL UNIQUE,  -- PCS, MTR, BAG, KG...
    uom_code        VARCHAR(20),
    description     VARCHAR(250),
    active          BOOLEAN DEFAULT TRUE
);

CREATE TABLE brand_master (
    brand_id        SERIAL PRIMARY KEY,
    brand_name      VARCHAR(100) NOT NULL UNIQUE,
    description     VARCHAR(250),
    active          BOOLEAN DEFAULT TRUE
);

CREATE TABLE category_master (
    category_id     SERIAL PRIMARY KEY,
    category_name   VARCHAR(100) NOT NULL UNIQUE,
    description     VARCHAR(250),
    active          BOOLEAN DEFAULT TRUE
);

CREATE TABLE model_master (
    model_id        SERIAL PRIMARY KEY,
    model_name      VARCHAR(100) NOT NULL,
    brand_id        INTEGER REFERENCES brand_master(brand_id),
    description     VARCHAR(250),
    active          BOOLEAN DEFAULT TRUE
);

CREATE TABLE warehouse_master (
    warehouse_id    SERIAL PRIMARY KEY,
    warehouse_name  VARCHAR(100) NOT NULL,
    location        VARCHAR(200),
    description     VARCHAR(250),
    active          BOOLEAN DEFAULT TRUE
);

CREATE TABLE tax_master (
    tax_id          SERIAL PRIMARY KEY,
    tax_name        VARCHAR(50) NOT NULL,
    tax_percent     NUMERIC(5,2) NOT NULL DEFAULT 0,
    description     VARCHAR(250),
    active          BOOLEAN DEFAULT TRUE
);

CREATE TABLE chart_of_accounts (
    account_id      SERIAL PRIMARY KEY,
    account_code    VARCHAR(20) UNIQUE,
    account_name    VARCHAR(150) NOT NULL,
    account_type    VARCHAR(30) NOT NULL,   -- ASSET, LIABILITY, EQUITY, INCOME, EXPENSE
    parent_id       INTEGER REFERENCES chart_of_accounts(account_id),
    opening_balance NUMERIC(18,2) DEFAULT 0,
    balance_type    VARCHAR(2) DEFAULT 'Dr', -- Dr / Cr
    description     VARCHAR(250),
    active          BOOLEAN DEFAULT TRUE
);

CREATE TABLE customer_master (
    customer_id     SERIAL PRIMARY KEY,
    customer_name   VARCHAR(150) NOT NULL,
    address         VARCHAR(250),
    mobile          VARCHAR(30),
    email           VARCHAR(100),
    credit_limit    NUMERIC(18,2) DEFAULT 0,
    opening_balance NUMERIC(18,2) DEFAULT 0,
    account_id      INTEGER REFERENCES chart_of_accounts(account_id),
    active           BOOLEAN DEFAULT TRUE
);

CREATE TABLE supplier_master (
    supplier_id     SERIAL PRIMARY KEY,
    supplier_name   VARCHAR(150) NOT NULL,
    address         VARCHAR(250),
    mobile          VARCHAR(30),
    email           VARCHAR(100),
    credit_limit    NUMERIC(18,2) DEFAULT 0,
    opening_balance NUMERIC(18,2) DEFAULT 0,
    account_id      INTEGER REFERENCES chart_of_accounts(account_id),
    active           BOOLEAN DEFAULT TRUE
);

CREATE TABLE item_master (
    item_id         SERIAL PRIMARY KEY,
    item_name       VARCHAR(150) NOT NULL,
    model           VARCHAR(100),
    side_size       VARCHAR(50),
    brand_id        INTEGER REFERENCES brand_master(brand_id),
    uom_id          INTEGER REFERENCES uom_master(uom_id),
    item_type       VARCHAR(30) DEFAULT 'Stock Item',
    category_id     INTEGER REFERENCES category_master(category_id),
    barcode         VARCHAR(50),
    opening_qty     NUMERIC(18,3) DEFAULT 0,
    rate            NUMERIC(18,2) DEFAULT 0,          -- Sales Price
    purchase_price  NUMERIC(18,2) DEFAULT 0,
    tax_percent     NUMERIC(5,2) DEFAULT 0,
    hsn_code        VARCHAR(30),
    min_stock       NUMERIC(18,3) DEFAULT 0,          -- Reorder Level
    description     VARCHAR(500),
    image_path      VARCHAR(250),
    active          BOOLEAN DEFAULT TRUE,
    created_at      TIMESTAMP DEFAULT now()
);

-- current on-hand stock, per item per warehouse (maintained by stock movements / triggers)
CREATE TABLE stock_balance (
    item_id         INTEGER NOT NULL REFERENCES item_master(item_id),
    warehouse_id    INTEGER NOT NULL REFERENCES warehouse_master(warehouse_id),
    qty_on_hand     NUMERIC(18,3) DEFAULT 0,
    PRIMARY KEY (item_id, warehouse_id)
);

-- ============================================================
-- SALES
-- ============================================================

CREATE TABLE sales_invoice (
    invoice_id      SERIAL PRIMARY KEY,
    invoice_no      VARCHAR(30) UNIQUE NOT NULL,
    invoice_date    DATE NOT NULL DEFAULT CURRENT_DATE,
    customer_id     INTEGER REFERENCES customer_master(customer_id),
    address         VARCHAR(250),
    mobile          VARCHAR(30),
    payment_terms   VARCHAR(20) DEFAULT 'Cash',   -- Cash / Credit
    salesman        VARCHAR(100),
    ref_no          VARCHAR(30),
    due_date        DATE,
    sub_total       NUMERIC(18,2) DEFAULT 0,
    discount        NUMERIC(18,2) DEFAULT 0,
    tax             NUMERIC(18,2) DEFAULT 0,
    grand_total     NUMERIC(18,2) DEFAULT 0,
    amount_in_words VARCHAR(250),
    created_at      TIMESTAMP DEFAULT now()
);

CREATE TABLE sales_invoice_item (
    line_id         SERIAL PRIMARY KEY,
    invoice_id      INTEGER REFERENCES sales_invoice(invoice_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL,
    rate            NUMERIC(18,2) NOT NULL,
    disc_percent    NUMERIC(5,2) DEFAULT 0,
    amount          NUMERIC(18,2) NOT NULL
);

CREATE TABLE sales_return (
    return_id       SERIAL PRIMARY KEY,
    return_no       VARCHAR(30) UNIQUE NOT NULL,
    return_date     DATE NOT NULL DEFAULT CURRENT_DATE,
    invoice_id      INTEGER REFERENCES sales_invoice(invoice_id),
    customer_id     INTEGER REFERENCES customer_master(customer_id),
    remarks         VARCHAR(250),
    total_amount    NUMERIC(18,2) DEFAULT 0
);

CREATE TABLE sales_return_item (
    line_id         SERIAL PRIMARY KEY,
    return_id       INTEGER REFERENCES sales_return(return_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL,
    rate            NUMERIC(18,2) NOT NULL,
    disc_percent    NUMERIC(5,2) DEFAULT 0,
    amount          NUMERIC(18,2) NOT NULL
);

-- ============================================================
-- PURCHASE
-- ============================================================

CREATE TABLE purchase_bill (
    purchase_id     SERIAL PRIMARY KEY,
    bill_no         VARCHAR(30) UNIQUE NOT NULL,
    bill_date       DATE NOT NULL DEFAULT CURRENT_DATE,
    supplier_id     INTEGER REFERENCES supplier_master(supplier_id),
    ref_no          VARCHAR(30),
    credit_days     INTEGER DEFAULT 0,
    due_date        DATE,
    remarks         VARCHAR(250),
    sub_total       NUMERIC(18,2) DEFAULT 0,
    discount        NUMERIC(18,2) DEFAULT 0,
    tax             NUMERIC(18,2) DEFAULT 0,
    grand_total     NUMERIC(18,2) DEFAULT 0,
    created_at      TIMESTAMP DEFAULT now()
);

CREATE TABLE purchase_bill_item (
    line_id         SERIAL PRIMARY KEY,
    purchase_id     INTEGER REFERENCES purchase_bill(purchase_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL,
    rate            NUMERIC(18,2) NOT NULL,
    disc_percent    NUMERIC(5,2) DEFAULT 0,
    amount          NUMERIC(18,2) NOT NULL
);

CREATE TABLE purchase_return (
    return_id       SERIAL PRIMARY KEY,
    return_no       VARCHAR(30) UNIQUE NOT NULL,
    return_date     DATE NOT NULL DEFAULT CURRENT_DATE,
    purchase_id     INTEGER REFERENCES purchase_bill(purchase_id),
    supplier_id     INTEGER REFERENCES supplier_master(supplier_id),
    remarks         VARCHAR(250),
    total_amount    NUMERIC(18,2) DEFAULT 0
);

CREATE TABLE purchase_return_item (
    line_id         SERIAL PRIMARY KEY,
    return_id       INTEGER REFERENCES purchase_return(return_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL,
    rate            NUMERIC(18,2) NOT NULL,
    disc_percent    NUMERIC(5,2) DEFAULT 0,
    amount          NUMERIC(18,2) NOT NULL
);

-- ============================================================
-- STOCK MOVEMENTS
-- ============================================================

CREATE TABLE stock_movement (
    movement_id     SERIAL PRIMARY KEY,
    movement_date   DATE NOT NULL DEFAULT CURRENT_DATE,
    item_id         INTEGER REFERENCES item_master(item_id),
    warehouse_id    INTEGER REFERENCES warehouse_master(warehouse_id),
    movement_type   VARCHAR(20) NOT NULL,   -- IN / OUT / TRANSFER / ADJUSTMENT
    qty             NUMERIC(18,3) NOT NULL,
    reference_type  VARCHAR(30),            -- SALES / PURCHASE / TRANSFER / ADJUSTMENT
    reference_id    INTEGER,
    remarks         VARCHAR(250)
);

CREATE TABLE stock_transfer (
    transfer_id     SERIAL PRIMARY KEY,
    transfer_no     VARCHAR(30) UNIQUE NOT NULL,
    transfer_date   DATE NOT NULL DEFAULT CURRENT_DATE,
    from_warehouse_id INTEGER REFERENCES warehouse_master(warehouse_id),
    to_warehouse_id   INTEGER REFERENCES warehouse_master(warehouse_id),
    remarks         VARCHAR(250),
    created_at      TIMESTAMP DEFAULT now()
);

CREATE TABLE stock_transfer_item (
    line_id         SERIAL PRIMARY KEY,
    transfer_id     INTEGER REFERENCES stock_transfer(transfer_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL
);

CREATE TABLE stock_adjustment (
    adjustment_id   SERIAL PRIMARY KEY,
    adjustment_no   VARCHAR(30) UNIQUE NOT NULL,
    adjustment_date DATE NOT NULL DEFAULT CURRENT_DATE,
    adjustment_type VARCHAR(10) NOT NULL,   -- Increase / Decrease
    warehouse_id    INTEGER REFERENCES warehouse_master(warehouse_id),
    remarks         VARCHAR(250),
    created_at      TIMESTAMP DEFAULT now()
);

CREATE TABLE stock_adjustment_item (
    line_id         SERIAL PRIMARY KEY,
    adjustment_id   INTEGER REFERENCES stock_adjustment(adjustment_id) ON DELETE CASCADE,
    item_id         INTEGER REFERENCES item_master(item_id),
    qty             NUMERIC(18,3) NOT NULL,
    rate            NUMERIC(18,2) DEFAULT 0,
    amount          NUMERIC(18,2) DEFAULT 0
);

-- ============================================================
-- ACCOUNTING - LEDGER TRANSACTIONS (Receipt / Payment / Journal / Contra)
-- ============================================================

CREATE TABLE ledger_entry (
    entry_id        SERIAL PRIMARY KEY,
    entry_date      DATE NOT NULL DEFAULT CURRENT_DATE,
    voucher_no      VARCHAR(30) NOT NULL,
    voucher_type    VARCHAR(20) NOT NULL,   -- Sales Invoice, Purchase Bill, Receipt, Payment, Journal, Contra
    account_id      INTEGER REFERENCES chart_of_accounts(account_id),
    particulars     VARCHAR(250),
    debit           NUMERIC(18,2) DEFAULT 0,
    credit          NUMERIC(18,2) DEFAULT 0,
    reference_id    INTEGER,
    created_at      TIMESTAMP DEFAULT now()
);

CREATE TABLE receipt_voucher (
    receipt_id      SERIAL PRIMARY KEY,
    receipt_no      VARCHAR(30) UNIQUE NOT NULL,
    receipt_date    DATE NOT NULL DEFAULT CURRENT_DATE,
    account_id      INTEGER REFERENCES chart_of_accounts(account_id),
    payment_mode    VARCHAR(20) DEFAULT 'Cash',
    received_by     VARCHAR(100),
    reference       VARCHAR(250),
    amount          NUMERIC(18,2) NOT NULL
);

CREATE TABLE payment_voucher (
    payment_id      SERIAL PRIMARY KEY,
    payment_no      VARCHAR(30) UNIQUE NOT NULL,
    payment_date    DATE NOT NULL DEFAULT CURRENT_DATE,
    account_id      INTEGER REFERENCES chart_of_accounts(account_id),
    payment_mode    VARCHAR(20) DEFAULT 'Cash',
    paid_by         VARCHAR(100),
    reference       VARCHAR(250),
    amount          NUMERIC(18,2) NOT NULL
);

CREATE TABLE journal_voucher (
    journal_id      SERIAL PRIMARY KEY,
    voucher_no      VARCHAR(30) UNIQUE NOT NULL,
    voucher_date    DATE NOT NULL DEFAULT CURRENT_DATE,
    narration       VARCHAR(250)
);

CREATE TABLE journal_voucher_item (
    line_id         SERIAL PRIMARY KEY,
    journal_id      INTEGER REFERENCES journal_voucher(journal_id) ON DELETE CASCADE,
    account_id      INTEGER REFERENCES chart_of_accounts(account_id),
    description     VARCHAR(250),
    debit           NUMERIC(18,2) DEFAULT 0,
    credit          NUMERIC(18,2) DEFAULT 0
);

CREATE TABLE contra_entry (
    contra_id       SERIAL PRIMARY KEY,
    voucher_no      VARCHAR(30) UNIQUE NOT NULL,
    voucher_date    DATE NOT NULL DEFAULT CURRENT_DATE,
    from_account_id INTEGER REFERENCES chart_of_accounts(account_id),
    to_account_id   INTEGER REFERENCES chart_of_accounts(account_id),
    amount          NUMERIC(18,2) NOT NULL,
    narration       VARCHAR(250)
);

CREATE TABLE users_master (
    user_id         SERIAL PRIMARY KEY,
    username        VARCHAR(50) UNIQUE NOT NULL,
    password_hash   VARCHAR(250) NOT NULL,
    full_name       VARCHAR(100),
    role            VARCHAR(30) DEFAULT 'User',
    active          BOOLEAN DEFAULT TRUE
);

CREATE TABLE database_log (
    log_id          SERIAL PRIMARY KEY,
    log_time        TIMESTAMP DEFAULT now(),
    username        VARCHAR(50),
    action          VARCHAR(250)
);

-- ============================================================
-- INDEXES
-- ============================================================
CREATE INDEX idx_item_name ON item_master(item_name);
CREATE INDEX idx_invoice_customer ON sales_invoice(customer_id);
CREATE INDEX idx_ledger_account ON ledger_entry(account_id);
CREATE INDEX idx_stock_movement_item ON stock_movement(item_id);

-- ============================================================
-- VIEW: Item List with computed Qty/Amount (matches Item List screen)
-- ============================================================
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
-- SEED: default document numbering
-- ============================================================
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
('STOCK_ADJUSTMENT', 'ADJ-', 1, 5);

INSERT INTO uom_master (uom_name) VALUES ('PCS'), ('MTR'), ('KG'), ('BAG'), ('BOX'), ('LTR');

INSERT INTO category_master (category_name) VALUES
('Tiles'), ('Marble & Granite'), ('Sanitary Ware'), ('Pipes & Fittings'), ('Cement & Building Materials');

-- Default warehouse every transaction falls back to until you add more
INSERT INTO warehouse_master (warehouse_name, location, description, active)
VALUES ('Main Warehouse', 'Head Office', 'Default warehouse', TRUE);

INSERT INTO chart_of_accounts (account_code, account_name, account_type, balance_type) VALUES
('1000', 'Cash in Hand', 'ASSET', 'Dr'),
('1001', 'Bank Account', 'BANK', 'Dr'),
('1100', 'Accounts Receivable', 'ASSET', 'Dr'),
('2100', 'Accounts Payable', 'LIABILITY', 'Cr'),
('4000', 'Sales', 'INCOME', 'Cr'),
('5000', 'Purchases', 'EXPENSE', 'Dr');

INSERT INTO customer_master (customer_name, account_id, address) VALUES
('Walk In Customer', (SELECT account_id FROM chart_of_accounts WHERE account_code='1100'), '-');

INSERT INTO company_profile (company_name, address, phone, email, ntn, strn) VALUES
('ABC Traders (Pvt) Ltd.', '123, Business Street, Lahore, Pakistan', '042-1234567', 'info@abctraders.com', '1234567-8', '1234567890123');

INSERT INTO financial_year (fy_name, start_date, end_date, is_current, is_active) VALUES
('2025-2026', '2025-07-01', '2026-06-30', TRUE, TRUE);

INSERT INTO users_master (username, password_hash, full_name, role) VALUES
('admin', 'admin', 'Administrator', 'Admin'); -- NOTE: replace with proper hashing (see README)
