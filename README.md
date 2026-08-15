# ERP Software — Inventory & Accounting System

A desktop ERP built with **C# WinForms (.NET 8)** and **PostgreSQL**, modeled on the
screens you shared: MDI shell with the full menu bar, Item Master, Item List,
Sales Invoice, Ledger, Receipt, and Payment.

## What's included

```
ERP-Solution/
├── ERP.sln
├── Database/
│   ├── schema.sql          ← full PostgreSQL schema + seed data (fresh install — drops erp_db first)
│   └── migration.sql       ← incremental, non-destructive updates for an existing erp_db
├── ErpApp.Tests/
│   └── PasswordHelperTests.cs ← xUnit tests for PasswordHelper (BCrypt + legacy fallbacks, no DB needed)
└── ErpApp/
    ├── ErpApp.csproj
    ├── appsettings.json     ← connection string lives here
    ├── Program.cs
    ├── Data/
    │   ├── AppConfig.cs     ← loads appsettings.json
    │   ├── DbHelper.cs      ← Npgsql wrapper (query/scalar/nonquery/transactions)
    │   ├── NumberToWords.cs ← "Six Hundred Thirty Only" style amount-in-words
    │   └── PasswordHelper.cs ← BCrypt password hashing (with legacy fallbacks)
    ├── Models/
    │   └── Models.cs
    └── Forms/
        ├── MainForm.cs            ← MDI parent, full menu (Master/Transactions/Inventory/…)
        ├── ItemMasterForm.cs      ← matches screenshot 1 (fields + paged grid + totals)
        ├── ItemListForm.cs        ← matches screenshot 2 bottom-left (grid + Add/Edit/Delete)
        ├── AddItemDialog.cs       ← matches screenshot 2 top-left ("Add New Item" popup)
        ├── StockSummaryForm.cs    ← Inventory mockup form 2
        ├── StockLedgerForm.cs     ← Inventory mockup form 3 (per-item, not per-account)
        ├── StockMovementForm.cs   ← Inventory mockup form 4
        ├── ReorderLevelForm.cs    ← Inventory mockup form 5
        ├── ItemPriceListForm.cs   ← Inventory mockup form 6
        ├── SalesInvoiceForm.cs    ← matches screenshot 2 top-right (full invoice + line grid)
        ├── PurchaseForm.cs        ← Transactions mockup form 1
        ├── PurchaseReturnForm.cs  ← Transactions mockup form 2
        ├── SalesReturnForm.cs     ← Transactions mockup form 4
        ├── StockTransferForm.cs   ← Transactions mockup form 5
        ├── StockTransferPrintForm.cs ← single transfer note preview + print
        ├── StockTransferDocumentRenderer.cs ← transfer note data-loader + drawing routine (From/To warehouses)
        ├── StockAdjustmentForm.cs ← Transactions mockup form 6
        ├── JournalVoucherForm.cs  ← Transactions mockup form 9 (multi-line, must balance)
        ├── ContraEntryForm.cs     ← Transactions mockup form 10
        ├── InvoicePrintForm.cs    ← single invoice/return preview + print (Sales/Purchase Invoice/Return)
        ├── InvoiceDocumentRenderer.cs ← shared data-loader + drawing routine (PrintDocType: 4 doc types)
        ├── BatchInvoicePrinter.cs ← prints/previews several invoices/returns as one multi-page job
        ├── SalesInvoiceListForm.cs    ← browse/search all Sales Invoices; Edit, Delete, Print (single or bulk)
        ├── PurchaseInvoiceListForm.cs ← same, for Purchase Bills
        ├── SalesReturnListForm.cs     ← same, for Sales Returns
        ├── PurchaseReturnListForm.cs  ← same, for Purchase Returns
        ├── VoucherPrintForm.cs        ← single Receipt/Payment preview + print
        ├── VoucherDocumentRenderer.cs ← shared data-loader + drawing routine for vouchers
        ├── BatchVoucherPrinter.cs     ← prints/previews several vouchers as one multi-page job
        ├── ReceiptListForm.cs         ← browse/search all Receipts; Edit, Delete, Print (single or bulk)
        ├── PaymentListForm.cs         ← same, for Payments
        ├── LedgerForm.cs          ← matches screenshot 2 middle-left (account statement)
        ├── TrialBalanceForm.cs    ← Accounting > Trial Balance
        ├── ProfitLossForm.cs      ← Accounting > Profit & Loss A/C
        ├── BalanceSheetForm.cs    ← Accounting > Balance Sheet
        ├── CashFlowStatementForm.cs ← Accounting > Cash Flow Statement
        ├── BankSummaryForm.cs     ← Accounting > Bank Summary (per-bank balances)
        ├── ReceiptForm.cs         ← matches screenshot 2 middle-right
        ├── PaymentForm.cs         ← matches screenshot 2 bottom-right
        ├── DataBackupForm.cs      ← Tools > Data Backup (shells out to pg_dump)
        ├── DataRestoreForm.cs     ← Tools > Restore Backup (shells out to psql)
        ├── DatabaseLogForm.cs     ← Tools > Database Log (browses database_log)
        ├── ManageUsersForm.cs     ← Users > Manage Users (CRUD, password hashing)
        ├── ChangePasswordForm.cs  ← Users > Change Password
        ├── ItemPickerDialog.cs    ← popup used to add a line to an invoice
        ├── AppFormBase.cs         ← base class for every form: Enter acts like Tab
        ├── LoginForm.cs           ← shown before MainForm; authenticates against users_master
        ├── MdiHelper.cs           ← centers every MDI child window on open (see note below)
        ├── SimpleMasterFormBase.cs← shared scaffold for the simple Master CRUD screens
        ├── CustomerMasterForm.cs
        ├── SupplierMasterForm.cs
        ├── UomMasterForm.cs
        ├── BrandMasterForm.cs
        ├── CategoryMasterForm.cs
        ├── ModelMasterForm.cs
        ├── WarehouseMasterForm.cs
        ├── AccountMasterForm.cs   ← Account Master + Chart of Accounts
        ├── TaxMasterForm.cs
        ├── PurchaseReportForm.cs
        ├── SalesReportForm.cs
        ├── SettingsForm.cs
        ├── CompanyProfileForm.cs
        ├── FinancialYearForm.cs
        └── DocumentNumberingForm.cs
```

## What's fully working vs. stubbed

**Fully working (real PostgreSQL CRUD):**
- Item Master — add/edit/delete items, brand & UOM combo boxes, live totals
- Item List — searchable grid, Add New / Edit / Delete
- **Master menu — all wired up:** Customer Master, Supplier Master, Unit of Measure,
  Brand Master, Category Master, Model Master, Warehouse Master, Account Master,
  Tax Master — each is a full add/edit/delete screen following the same
  fields-on-top + grid-below pattern (built on a shared `SimpleMasterFormBase`
  so they're easy to extend)
- Chart of Accounts (Accounting menu) reuses Account Master
- Sales Invoice — pick customer, add item lines, auto invoice numbering,
  auto stock movement + stock balance update, ledger posting, amount-in-words
- **Transactions menu — all 10 wired up:** Purchase, Purchase Return, Sales,
  Sales Return, Stock Transfer, Stock Adjustment, Payment, Receipt, Journal
  Voucher, Contra Entry — each posts to the ledger and/or stock tables as
  appropriate (see "How the transaction forms post" below)
- **Inventory menu — all wired up:** Items (list + Add/Edit/Delete), Stock
  Summary (Brand/UOM/**Warehouse**/Category/Low-Stock filters + KPI panel —
  picking one warehouse shows each item's qty at that warehouse; "All
  Warehouses" totals across them), Stock Ledger (per-item IN/OUT running
  balance built from `stock_movement`, with a **Warehouse** filter and a
  Warehouse column — the opening balance is computed per warehouse),
  Stock Movement (browsable movement log with a **Warehouse** column +
  filter and In/Out/Net KPIs — transfer legs count as IN/OUT on the right
  side), Reorder Level (color-coded OK/LOW status), Item Price List (Sales
  Price vs Purchase Price, CSV export)
- The Add/Edit Item dialog now also captures **Item Type, Category, Barcode,
  Purchase Price, and Reorder Level** — added because the new Inventory
  reports need them (Stock Summary's Category filter, Item Price List's
  Purchase Price column, Reorder Level's threshold). Category is a proper
  master table now (`category_master`, managed at Master > Category Master)
  rather than free text — the dialog's Category field is a combo that lists
  existing categories but still lets you type a new one (same upsert-by-name
  pattern as the Brand field: type a name that doesn't exist yet and it's
  created on save).
- Purchase Invoice / Sales Invoice **printable view** — a letterhead-style
  document (company header, badge, party info, item table, totals, amount in
  words, signature line) opened from the Print button on Purchase/Sales forms;
  supports on-screen preview and real printing via `PrintDocument`. Invoices
  and returns printed since per-warehouse balances show the **Warehouse** the
  stock came from/arrived at (read from the document's `stock_movement`
  rows; documents saved before the feature print without it)
- **Stock Transfer printable note** — the transfer form's Print button now
  opens a letterhead-style note with the **From and To warehouses** front and
  center, the item list (name/model/UOM/qty), total qty, remarks, and a
  signature line (preview + print via `PrintDocument`)
- Ledger — account picker, date range, running Dr/Cr balance
- Receipt / Payment — auto voucher numbering, ledger posting, amount-in-words
- Purchase Report / Sales Report — date range + party filter, grid, totals, CSV export
- Settings, Company Profile — save currency/fiscal-year-start/notifications and
  company letterhead details (with logo upload) to `company_profile`
- Financial Year — add/edit years, with "Current Year" enforced as a single flag
- Document Numbering — edit prefix/suffix/next-number/padding per document type,
  with upsert so you can also add brand-new document types

### Editing, deleting, and printing invoices, returns, and vouchers

The same List → Edit / Delete / Print pattern is now used everywhere a
document is saved, all under the **Transactions** menu:

| Document | List screen | Edit re-opens | Print uses |
|---|---|---|---|
| Sales Invoice | Sales Invoice List | `SalesInvoiceForm(id)` | `InvoicePrintForm` / `BatchInvoicePrinter` |
| Purchase Bill | Purchase Invoice List | `PurchaseForm(id)` | `InvoicePrintForm` / `BatchInvoicePrinter` |
| Sales Return | Sales Return List | `SalesReturnForm(id)` | `InvoicePrintForm` / `BatchInvoicePrinter` |
| Purchase Return | Purchase Return List | `PurchaseReturnForm(id)` | `InvoicePrintForm` / `BatchInvoicePrinter` |
| Receipt | Receipt List | `ReceiptForm(id)` | `VoucherPrintForm` / `BatchVoucherPrinter` |
| Payment | Payment List | `PaymentForm(id)` | `VoucherPrintForm` / `BatchVoucherPrinter` |

- **Edit** — reopens the entry form pre-loaded with that record. Saving
  reverses the old stock/ledger postings first, then re-posts the edited
  version under the same document number (`DbHelper.ReverseXxxPostings`
  handles this for each document type).
- **Delete** — select one row or many (Ctrl+Click / Shift+Click) and
  "Delete Selected" reverses stock/ledger effects before removing each one.
- **Print** — one row selected opens a single-document preview; several
  selected builds one print job with one document per page, so a batch goes
  to the printer in a single pass. Invoices/returns share `InvoiceDocumentData`
  + `InvoiceDocumentRenderer` (a 4-way `PrintDocType` enum: SalesInvoice,
  PurchaseBill, SalesReturn, PurchaseReturn); Receipt/Payment have their own
  simpler `VoucherDocumentData` + `VoucherDocumentRenderer` pair since a
  voucher is one amount, not a line-item table.

Every entry form (Sales/Purchase/Return/Receipt/Payment) also has its own
**Delete** button (for whatever's currently open) and a **Browse All...**
button that jumps straight to its list screen.

### Data Backup, Restore, Database Log, and Users (Tools/Utilities/Users menus)

- **Tools > Data Backup** shells out to `pg_dump` (must be on PATH — installed
  alongside any PostgreSQL server/client) to write a plain-SQL `.sql` backup
  file. The database password is passed via the `PGPASSWORD` environment
  variable, never on the visible command line.
- **Tools > Restore Backup** shells out to `psql` to run a chosen `.sql` file
  against the current database, after a confirmation prompt showing exactly
  which database/host it will hit. Intended for restoring into a freshly
  created empty database — running it against a database that already has
  data can produce "already exists" errors for duplicate rows.
- **Tools > Database Log** is a read-only browser (date range / user / text
  filters, CSV export) over the `database_log` table. `DbHelper.LogAction(...)`
  writes to it and is called from every Master screen (via
  `SimpleMasterFormBase`), every transaction Save/Delete, and password
  changes — logging never throws or blocks the real action if it fails.
- **Users > Manage Users** is full CRUD over `users_master` (username, full
  name, role, active, password). New passwords are hashed with BCrypt
  (BCrypt.Net-Next, work factor 11 — see `PasswordHelper`). Leave the
  Password field blank when editing an existing user to keep their current
  password.
- **Users > Change Password** lets the currently logged-in user (per
  `AppConfig.CurrentUser`) change their own password after verifying the
  current one. Verification goes through `PasswordHelper`, which still
  accepts a legacy SHA-256 hash or the plain-text `admin` seed so the very
  first login after setup works — and any legacy account that logs in is
  automatically re-hashed with BCrypt on the spot.

**Nothing left stubbed.** Every menu item across every menu now opens a real
screen — Trial Balance/P&L/Balance Sheet/Cash Flow Statement, Data
Backup/Restore, Database Log, and Manage Users/Change Password were the last
ones and are covered above. (The one exception: `MainForm.cs` still has a
generic `NotImplemented` helper method sitting unused in case you add new
menu items of your own before building their screens.)

### How the transaction forms post

Every form below now posts **balanced double-entry** — each transaction hits
two ledger accounts, so Trial Balance/Balance Sheet actually balance:

| Form | Stock effect | Ledger effect |
|---|---|---|
| Purchase | `+` stock (IN) | Credit supplier (payable ↑) / Debit Purchases (expense ↑) |
| Purchase Return | `-` stock (OUT) | Debit supplier (payable ↓) / Credit Purchases (expense ↓) |
| Sales | `-` stock (OUT) | Debit customer (receivable ↑) / Credit Sales (income ↑) |
| Sales Return | `+` stock (IN) | Credit customer (receivable ↓) / Debit Sales (income ↓) |
| Stock Transfer | movement logged per warehouse | none (no money moves) |
| Stock Adjustment | `+`/`-` stock per Increase/Decrease | none (add a Journal Voucher separately if you want to book the value) |
| Receipt | none | Credit the account (receivable ↓) / Debit Cash in Hand (1000) or the picked Bank Account |
| Payment | none | Debit the account (payable ↓) / Credit Cash in Hand (1000) or the picked Bank Account |
| Journal Voucher | none | Whatever the lines say — form blocks Save unless total debit = total credit |
| Contra Entry | none | Credit "From" account, Debit "To" account |

Receipt/Payment post to account code `1000` (Cash in Hand) when Payment Mode
is "Cash", or to a **real bank account of your choosing** for anything else
(Bank Transfer/Cheque/Card). To set that up, create the bank account in
Account Master with type **BANK** (the seeded "Bank Account" 1001 is already
BANK after migration) — the Receipt/Payment forms then show a "Bank Account"
picker that's enabled only for non-cash modes, and the non-cash leg posts to
whichever account you pick. Editing an old voucher still finds the account it
actually posted to (read from the ledger), and the printed voucher shows the
bank name. Sales/Purchase post to account code `4000` (Sales) / `5000`
(Purchases) respectively. If you rename or delete those seeded accounts,
update the codes in `DbHelper.GetAccountIdByCode` calls (in `SalesInvoiceForm`,
`PurchaseForm`, `SalesReturnForm`, `PurchaseReturnForm`, `ReceiptForm`,
`PaymentForm`) to match.

> **Note on historical data:** this double-entry fix only applies to
> transactions saved *after* you update the app. Sales/Purchase/Receipt/
> Payment/Return records saved with an earlier build only have one leg posted
> and won't retroactively balance in Trial Balance — either re-enter them or
> post a correcting Journal Voucher for the missing side.

### Per-warehouse stock balances

`stock_balance` is keyed on `(item_id, warehouse_id)` — one running on-hand
quantity per item **per warehouse**, so stock is genuinely split across
warehouses:

- **Sales Invoice / Purchase / Sales Return / Purchase Return** each have a
  Warehouse picker in the header (Stock Adjustment and Stock Transfer already
  had theirs) — the transaction hits exactly the warehouse you pick, and the
  `stock_movement` row records that warehouse.
- **Stock Transfer** now actually moves stock: it subtracts from the From
  warehouse's balance, adds to the To warehouse's, and logs both a
  `TRANSFER_OUT` and a `TRANSFER_IN` movement (the overall on-hand total is
  unchanged — it just lives in a different warehouse now).
- **Reversals** (Edit/Delete on any transaction) read the `warehouse_id` from
  `stock_movement` to restore each warehouse's balance exactly. Stock
  Transfer and Stock Adjustment also reverse their balance effects on Delete
  now (previously they only deleted the header row).
- **Opening stock** — creating an item with an Opening Qty seeds a real
  `stock_balance` row at the default (first active) warehouse, so the qty
  shows in reports and transactions adjust it (previously it was only a
  display fallback, and the first sale on a fresh item produced a negative
  balance).
- **Views/reports** — `vw_item_list` (used by Item List, Stock Summary, Item
  Price List, Reorder Level, Item Master) totals each item's quantity across
  all warehouses.

`migration.sql` upgrades an existing database in place: it seeds a default
"Main Warehouse", rebuilds `stock_balance` with the composite key (existing
rows land in the default warehouse; items with opening stock but no balance
row get it there too), and rebuilds `vw_item_list`. Old transactions saved
before this change have `warehouse_id = NULL` in `stock_movement` and their
reversals fall back to the default warehouse.

### Financial statements (Accounting menu)

- **Trial Balance** — every account's balance as of a chosen date (opening
  balance + ledger activity up to that date), split into Debit/Credit columns
  by sign, with a "balanced ✓ / out of balance ⚠" indicator comparing the two
  column totals.
- **Profit & Loss Account** — Income vs Expense ledger activity for a date
  range, ending in Net Profit or Net Loss. Period-based (no opening balance),
  since income/expense accounts don't carry one.
- **Balance Sheet** — Assets vs Liabilities + Equity as of a chosen date, with
  cumulative Income − Expense up to that date folded into Equity as "Current
  Earnings" so the two sides should match (with the same balanced ✓ / out of
  balance ⚠ indicator as Trial Balance).
- **Cash Flow Statement** — direct-method report over a date range: opening
  Cash + Bank balance, cash in/out grouped by voucher type (Receipt, Payment,
  Contra, Journal — whichever actually hit Cash in Hand `1000` or any
  `BANK`-type account), and a closing balance. There's no
  Operating/Investing/Financing classification — it's a flat "where did cash
  move" breakdown.
- **Bank Summary** — one row per `BANK` account (or a single bank, via the
  filter): opening balance, cash in, cash out, and closing balance for the
  date range, with a TOTAL row. Cash in Hand lives on the Cash Flow Statement
  instead, since it isn't a bank account.

All five are read-only reports (no Save/Edit) with a CSV Export button, and
are also reachable from the Reports menu (Stock Report/Item Ledger Report/
Account Statement reuse the Inventory and Ledger forms rather than
duplicating them).



### Login

There wasn't a login screen before — the app just hardcoded the current user
to `admin` and went straight to the main window. `LoginForm` now runs first
(`Program.cs`): it authenticates against `users_master` via `PasswordHelper`
(BCrypt hashes, plus legacy SHA-256 and plain-text `admin` fallbacks so a
fresh install can log in before anyone sets a real password; legacy hashes
are migrated to BCrypt on the spot after a successful login), locks out
after 5 failed attempts, and only then does
`Application.Run(new MainForm())` happen. `Users > Logout` restarts the app, which shows the login screen
again. After logging in, `MainForm` now opens empty — it no longer
auto-opens Item Master on startup; the main window is just the menu bar
until you pick something.

### Enter acts like Tab

`AppFormBase` — the base class every form in the app now inherits from
instead of `Form` directly — intercepts Enter and moves focus to the next
control, the same as Tab. Enter is left doing its normal job in three cases:
multiline text boxes (Remarks/Description/Narration — Enter inserts a
newline), buttons (Enter/Space already clicks a focused button), and
`DataGridView` (Enter already commits the cell and moves down/across; it
shouldn't tab you out of the grid entirely).

Deliberately **not** included: auto-clicking a button just because Enter
happened to tab onto it. Landing on a button only focuses it — press
Enter/Space again (or click) to activate it. This was a real bug I caught
while wiring this up: with button order Exit-then-Login in `LoginForm`,
auto-clicking on landing would have silently **exited the app** instead of
logging in, since Exit came first in tab order. Skipping the auto-click
sidesteps that whole class of problem everywhere, at the cost of needing one
extra Enter/click once focus reaches a button. (I did fix `LoginForm`'s
button order too, so Login is the first button reached from the password
field — Enter, Enter logs in.)

If you add a new form, inherit from `AppFormBase` instead of `Form` to get
this automatically.

### Label/textbox layout fix

Several forms had `TableLayoutPanel`s with a `RowCount` set (or rows added
implicitly) but **no `RowStyles`** — left that way, WinForms auto-sizes each
row to fit its content instead of giving every row an equal share of the
available height. That's what caused the gaps (gaps looked worst on forms
whose surrounding box height didn't match how many rows it actually held —
e.g. Brand Master's 2-row box was sized for the busier 4-row forms) and
uneven spacing you saw. Fixed everywhere it appeared:

- `SimpleMasterFormBase` (the shared scaffold behind 10 Master screens) now
  works out how many rows a subclass actually used and sizes both the
  `RowStyles` and the surrounding GroupBox height to match, instead of a
  fixed height that didn't fit every subclass.
- `AddItemDialog` (the densest form in the app) and 13 other forms
  (Purchase/Sales/Return headers, Receipt/Payment/Contra Entry, Item Master,
  Ledger, Company Profile, Settings, Change Password, Document Numbering,
  Journal Voucher, Stock Ledger's Opening Balance panel) all got explicit
  `RowStyles` added.
- If you add a new form with a multi-row `TableLayoutPanel`, give it explicit
  `RowStyles` (equal `Percent` shares is usually right) rather than leaving
  rows unstyled — that's the pattern to avoid.

### Why windows open centered

WinForms MDI child forms **ignore `FormStartPosition.CenterParent`/`CenterScreen`
entirely** — the framework always cascades them from the top-left of the MDI
client area instead, no matter what StartPosition is set to (this includes the
default Item Master window that opens on startup). Every place in the app
that opens a form as an MDI child goes through `MdiHelper.ShowCentered(...)`
instead of setting `MdiParent`/calling `Show()` directly, which manually
computes the MDI client area's actual size (`child.Parent.ClientSize` — once
a form becomes an MDI child, WinForms reparents it under the MdiParent's
internal `MdiClient` control, and that's what needs centering against, not
the outer form) and centers the child in it. If you add a new screen that
opens as an MDI child, use `MdiHelper.ShowCentered(mdiParent, child)` (or the
`OpenChild` helper already in `MainForm.cs` and every List form) rather than
`child.MdiParent = ...; child.Show();` directly, or it'll silently go back to
cascading from the corner.

## Setup

### 1. Database

**Brand-new setup** (no `erp_db` yet, or you're fine wiping it and starting over):

```bash
psql -U postgres -f Database/schema.sql
```

This creates the `erp_db` database, all tables, a starter chart of accounts,
a "Walk In Customer", default document-numbering prefixes (`INV-`, `RCPT-`,
`PAY-`, `PB-`, `JV-`, `CN-`, `SR-`, `PR-`, `ST-`, `ADJ-`), and an `admin` user row.
Note: the script starts with `DROP DATABASE IF EXISTS erp_db`, so it always
recreates from scratch — don't run this against a database with real data
you want to keep.

**You already have an `erp_db` with data in it, and the app errors with
something like `column "category" does not exist`:** that means the database
predates a schema update. Run the non-destructive migration instead — it only
adds what's missing (new columns get `ADD COLUMN IF NOT EXISTS`, new tables
get `CREATE TABLE IF NOT EXISTS`, new seed rows get `ON CONFLICT DO NOTHING`),
so it's safe to run against live data and safe to run more than once:

```bash
psql -U postgres -d erp_db -f Database/migration.sql
```

`migration.sql` brings a database up to date with everything added across
every round of this build: the Master-screen extra fields (credit limit,
active/description flags, UOM code, etc.), document-numbering suffix and new
document types, the return/transfer/adjustment/journal line-item tables, and
the `item_master` columns (`item_type`, `category`, `barcode`,
`purchase_price`) that the Inventory reports need — which is exactly what
was missing in the `category does not exist` error.

> The `admin` user's `password_hash` column currently stores plain text (`admin`)
> as a placeholder. Before going anywhere near production, hash it (e.g. with
> `BCrypt.Net-Next`) and check it in a real login form — none is wired up yet,
> since your screenshots don't show a login screen.

### 2. Connection string

Edit `ErpApp/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "ErpDb": "Host=localhost;Port=5432;Database=erp_db;Username=postgres;Password=postgres"
  }
}
```

### 3. Run

Requires **Windows** + **.NET 8 SDK** (WinForms is Windows-only) and **Visual Studio 2022**
(or `dotnet build` / `dotnet run` from a Developer Command Prompt).

```bash
cd ErpApp
dotnet restore
dotnet run
```

Or just open `ERP.sln` in Visual Studio and press F5.

### 4. Tests

`ErpApp.Tests` is an xUnit project covering `PasswordHelper` — BCrypt hashing,
and the legacy SHA-256 / plain-text-seed verification paths plus the
`NeedsRehash` migration logic. Pure logic tests, no database required:

```bash
dotnet test
```

### 5. Integration tests (scratch database)

`ErpApp.IntegrationTests` verifies the stock and bank flows against
**throwaway scratch databases** (one per test collection, named
`erp_db_test_<random>`) — each is created from `Database/schema.sql` before
its collection runs and dropped afterwards, so your real `erp_db` is never
touched. The tests drive the real `DbHelper` code through the exact statements
the forms run:

- **Stock flow** (`StockFlowTests`) — opening-stock seeding, purchase/transfer/
  sale/return/adjustment hitting the picked warehouse's balance, the aggregate
  `vw_item_list`, the Stock Summary per-warehouse query, and sales/transfer/
  adjustment reversals restoring each warehouse's balance.
- **Bank flow** (`BankFlowTests`) — non-cash receipts/payments posting their
  cash/bank leg to the picked `BANK` account (Cash stays on 1000), the
  edit/print bank-leg derivation, the receipt reversal, and the Bank Summary
  per-bank opening/in/out/closing query.
- **Cash Flow** (`CashFlowTests`) — its own isolated collection (so absolute
  sums aren't polluted by sibling tests) proving the statement sees money in
  any `BANK` account, not just the old 1001.
- **Login flow** (`LoginFlowTests`) — its own isolated collection: exercises
  the login auth path end-to-end, including the **BCrypt migration** of a
  plain-text password on first successful login (and that a fresh BCrypt hash
  is left alone), plus wrong-password/locked-account behavior.
- **Purchase & returns** (`PurchaseReturnFlowTests`) — the full Purchase / Sales
  Return / Purchase Return save flows: header + lines + the per-warehouse stock
  movement and balance, plus the two balanced ledger legs landing on the
  party's account (from the customer/supplier master) and Purchases 5000 / Sales
  4000 — and each real reversal method restoring the warehouse balance exactly.
- **Sales invoices** (`SalesInvoiceFlowTests`) — the full Sales Invoice save
  flow: OUT movements + balance at the picked warehouse (others untouched), the
  customer-debit / Sales-credit ledger legs, the discount case (header keeps the
  gross, both ledger legs post the NET grand), and the edit path
  (reverse → update → re-post) proving no double posting.
- **Balance Sheet** (`BalanceSheetTests`) — its own isolated collection:
  replicates the report's grouping — ASSET/BANK on the asset side (signed Dr),
  LIABILITY/EQUITY flipped positive, cumulative Income − Expense folded into
  equity as Current Earnings — and asserts Assets = Liabilities + Equity, that
  BANK accounts count as assets, and that entries dated after the As-Of date
  are ignored.
- **Profit & Loss** (`ProfitLossTests`) — its own isolated collection: posts
  income/expense activity and replicates the report's per-account aggregation
  (income as credit − debit, expense as debit − credit, nonzero only), the
  date-range filter, and the Net Profit / Net Loss result.
- **Stock Ledger** (`StockLedgerTests`) — the ledger's opening balance and
  movement classification: the all-warehouse and per-warehouse views of a
  purchase → transfer → sale flow, `TRANSFER_IN` counted as IN (matching the
  Stock Movement log), and pre-period movements folding into the opening
  balance.
- **General Ledger** (`LedgerTests`) — the Accounting &gt; Ledger report: opening
  balance as signed COA opening + pre-period entries (consistent with Trial
  Balance), Dr/Cr running balance across in-period entries, Cr openings for
  supplier/payable accounts, and the account-picker union of chart accounts +
  customer/supplier sub-ledgers.
- **Trial Balance** (`TrialBalanceTests`) — its own isolated collection: posts a
  balanced set of vouchers and replicates `TrialBalanceForm`'s computation
  (signed opening + debits − credits per account, zero-balance accounts
  skipped), asserting the per-account nets, Debit vs Credit totals balance, and
  the out-of-balance detection for unmatched Dr openings.

Parallelization is disabled for this project because each collection fixture
points the static `AppConfig.ConnectionString` at its own scratch database.

### 6. CI (GitHub Actions)

`.github/workflows/ci.yml` runs `dotnet test` (both test projects) on a
`windows-latest` runner — WinForms is Windows-only, and the integration suite
needs a real PostgreSQL. The job:

1. checks out and installs the .NET 8 SDK;
2. installs **PostgreSQL 17 via chocolatey** (password `postgres`), starts the
   service, puts `psql` on PATH, and waits until it answers `SELECT 1`;
3. **rewrites `ErpApp/appsettings.json`** to point at the CI database (the
   committed file holds the developer's local password — CI never uses it);
4. runs `dotnet test`, which executes the unit suite (18 tests) and the
   integration suite (53 tests, each collection creating and dropping its own
   `erp_db_test_<random>` scratch database).

The local equivalent of step 4 is just: PostgreSQL running + `psql` on PATH +
working credentials in `ErpApp/appsettings.json`, then `dotnet test`.

Requires PostgreSQL running locally + `psql` on PATH (same dependency as Data
Backup/Restore). Connection settings come from `ErpApp/appsettings.json`.

```bash
cd ErpApp.IntegrationTests
dotnet test
```

`ErpApp.IntegrationTests` is part of `ERP.sln`, so the default `dotnet test`
at the solution root runs both suites (unit tests stay DB-free; the
integration suite needs PostgreSQL up). On a machine without PostgreSQL, run
just the unit project: `dotnet test ErpApp.Tests`.

## Notes on the code style

- Forms are built entirely in code (no `.Designer.cs` split) so every control
  is easy to find and edit in one file — this also means you can open them in
  the Visual Studio designer, but you'll mostly want to edit the C# directly.
- `DbHelper` opens a short-lived connection per call, which is the simplest
  safe pattern for a desktop app talking to Postgres — Npgsql pools connections
  internally so this is cheap.
- Money-moving actions (Sales Invoice, Receipt, Payment) wrap their inserts in
  a single DB transaction (`DbHelper.ExecuteTransaction`) so a partial failure
  can't leave the ledger and the invoice/voucher tables out of sync.
- Document numbers (`INV-00047` etc.) are generated with a `SELECT ... FOR UPDATE`
  inside the same transaction, so two users saving invoices at the same time
  won't collide.

## Extending it

To add one of the stubbed screens (say, Purchase Bill):
1. Copy `SalesInvoiceForm.cs` → `PurchaseBillForm.cs`.
2. Swap `sales_invoice` / `sales_invoice_item` for `purchase_bill` / `purchase_bill_item`,
   and `customer_master` for `supplier_master`.
3. Flip the stock movement from `'OUT'` to `'IN'` (and the balance update from
   subtract to add).
4. Wire it into `MainForm.cs`'s `Transactions → Purchase` menu item.

Reports (Trial Balance, P&L, Balance Sheet, Stock Summary, etc.) are all just
`SELECT`/`GROUP BY` queries over `ledger_entry`, `stock_movement`, and
`chart_of_accounts` — display them the same way `LedgerForm` displays its grid.
