using ErpApp.Data;

namespace ErpApp.Forms;

public class MainForm : AppFormBase
{
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel lblCompany = new();
    private readonly ToolStripStatusLabel lblFinancialYear = new();
    private readonly ToolStripStatusLabel lblUser = new();
    private readonly ToolStripStatusLabel lblDbStatus = new();
    private readonly ToolStripStatusLabel lblDateTime = new();
    private readonly System.Windows.Forms.Timer clockTimer = new();

    public MainForm()
    {
        Text = "ERP Software - Inventory & Accounting System";
        IsMdiContainer = true;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1536;
        Height = 1024;

        BuildMenu();
        BuildStatusBar();

        Load += MainForm_Load;
        clockTimer.Interval = 1000;
        clockTimer.Tick += (s, e) => lblDateTime.Text = DateTime.Now.ToString("dd/MM/yyyy hh:mm tt");
        clockTimer.Start();
    }

    private void MainForm_Load(object? sender, EventArgs e)
    {
        lblCompany.Text = "Company: " + AppConfig.CompanyName;
        lblUser.Text = "User: " + AppConfig.CurrentUser;

        bool ok = DbHelper.TestConnection(out string msg);
        lblDbStatus.Text = ok ? "Database: PostgreSQL \u25CF" : "Database: Disconnected \u25CF";
        lblDbStatus.ForeColor = ok ? Color.Green : Color.Red;

        if (ok)
        {
            try
            {
                var profile = DbHelper.ExecuteQuery("SELECT company_name FROM company_profile ORDER BY company_id LIMIT 1");
                if (profile.Rows.Count > 0)
                    AppConfig.SetCompanyName(profile.Rows[0]["company_name"].ToString() ?? AppConfig.CompanyName);

                var fy = DbHelper.ExecuteQuery("SELECT fy_name FROM financial_year WHERE is_current ORDER BY fy_id DESC LIMIT 1");
                if (fy.Rows.Count > 0)
                    AppConfig.SetFinancialYear(fy.Rows[0]["fy_name"].ToString() ?? AppConfig.FinancialYear);
            }
            catch { /* fall back silently to appsettings.json defaults */ }
        }
        lblCompany.Text = "Company: " + AppConfig.CompanyName;
        lblFinancialYear.Text = "Financial Year: " + AppConfig.FinancialYear;

        if (!ok)
        {
            MessageBox.Show(
                "Could not connect to PostgreSQL.\n\n" + msg +
                "\n\nCheck the connection string in appsettings.json and make sure the erp_db " +
                "database has been created from Database/schema.sql.",
                "Database Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------------------------------------------------------------
    // Menu bar — mirrors: Master | Transactions | Inventory | Accounting
    //                     | Reports | Tools | Utilities | Users | Window | Help
    // ---------------------------------------------------------------
    private void BuildMenu()
    {
        var menu = new MenuStrip();

        // MASTER
        var master = new ToolStripMenuItem("Master");
        master.DropDownItems.Add(Item("Item Master", (s, e) => OpenChild(new ItemMasterForm())));
        master.DropDownItems.Add(Item("Customer Master", (s, e) => OpenChild(new CustomerMasterForm())));
        master.DropDownItems.Add(Item("Supplier Master", (s, e) => OpenChild(new SupplierMasterForm())));
        master.DropDownItems.Add(Item("Unit of Measure", (s, e) => OpenChild(new UomMasterForm())));
        master.DropDownItems.Add(Item("Brand Master", (s, e) => OpenChild(new BrandMasterForm())));
        master.DropDownItems.Add(Item("Category Master", (s, e) => OpenChild(new CategoryMasterForm())));
        master.DropDownItems.Add(Item("Model Master", (s, e) => OpenChild(new ModelMasterForm())));
        master.DropDownItems.Add(Item("Warehouse Master", (s, e) => OpenChild(new WarehouseMasterForm())));
        master.DropDownItems.Add(Item("Account Master", (s, e) => OpenChild(new AccountMasterForm())));
        master.DropDownItems.Add(Item("Tax Master", (s, e) => OpenChild(new TaxMasterForm())));

        // TRANSACTIONS
        var trans = new ToolStripMenuItem("Transactions");
        trans.DropDownItems.Add(Item("Purchase", (s, e) => OpenChild(new PurchaseForm())));
        trans.DropDownItems.Add(Item("Purchase Invoice List", (s, e) => OpenChild(new PurchaseInvoiceListForm())));
        trans.DropDownItems.Add(Item("Purchase Return", (s, e) => OpenChild(new PurchaseReturnForm())));
        trans.DropDownItems.Add(Item("Purchase Return List", (s, e) => OpenChild(new PurchaseReturnListForm())));
        trans.DropDownItems.Add(Item("Sales", (s, e) => OpenChild(new SalesInvoiceForm())));
        trans.DropDownItems.Add(Item("Sales Invoice List", (s, e) => OpenChild(new SalesInvoiceListForm())));
        trans.DropDownItems.Add(Item("Sales Return", (s, e) => OpenChild(new SalesReturnForm())));
        trans.DropDownItems.Add(Item("Sales Return List", (s, e) => OpenChild(new SalesReturnListForm())));
        trans.DropDownItems.Add(Item("Stock Transfer", (s, e) => OpenChild(new StockTransferForm())));
        trans.DropDownItems.Add(Item("Stock Adjustment", (s, e) => OpenChild(new StockAdjustmentForm())));
        trans.DropDownItems.Add(Item("Payment", (s, e) => OpenChild(new PaymentForm())));
        trans.DropDownItems.Add(Item("Payment List", (s, e) => OpenChild(new PaymentListForm())));
        trans.DropDownItems.Add(Item("Receipt", (s, e) => OpenChild(new ReceiptForm())));
        trans.DropDownItems.Add(Item("Receipt List", (s, e) => OpenChild(new ReceiptListForm())));
        trans.DropDownItems.Add(Item("Journal Voucher", (s, e) => OpenChild(new JournalVoucherForm())));
        trans.DropDownItems.Add(Item("Contra Entry", (s, e) => OpenChild(new ContraEntryForm())));

        // INVENTORY
        var inv = new ToolStripMenuItem("Inventory");
        inv.DropDownItems.Add(Item("Items", (s, e) => OpenChild(new ItemListForm())));
        inv.DropDownItems.Add(Item("Stock Summary", (s, e) => OpenChild(new StockSummaryForm())));
        inv.DropDownItems.Add(Item("Stock Ledger", (s, e) => OpenChild(new StockLedgerForm())));
        inv.DropDownItems.Add(Item("Stock Movement", (s, e) => OpenChild(new StockMovementForm())));
        inv.DropDownItems.Add(Item("Reorder Level", (s, e) => OpenChild(new ReorderLevelForm())));
        inv.DropDownItems.Add(Item("Item Price List", (s, e) => OpenChild(new ItemPriceListForm())));

        // ACCOUNTING
        var acc = new ToolStripMenuItem("Accounting");
        acc.DropDownItems.Add(Item("Chart of Accounts", (s, e) => OpenChild(new AccountMasterForm())));
        acc.DropDownItems.Add(Item("Ledger", (s, e) => OpenChild(new LedgerForm())));
        acc.DropDownItems.Add(Item("Trial Balance", (s, e) => OpenChild(new TrialBalanceForm())));
        acc.DropDownItems.Add(Item("Profit && Loss A/C", (s, e) => OpenChild(new ProfitLossForm())));
        acc.DropDownItems.Add(Item("Balance Sheet", (s, e) => OpenChild(new BalanceSheetForm())));
        acc.DropDownItems.Add(Item("Cash Flow Statement", (s, e) => OpenChild(new CashFlowStatementForm())));
        acc.DropDownItems.Add(Item("Bank Summary", (s, e) => OpenChild(new BankSummaryForm())));

        // REPORTS
        var rep = new ToolStripMenuItem("Reports");
        rep.DropDownItems.Add(Item("Purchase Report", (s, e) => OpenChild(new PurchaseReportForm())));
        rep.DropDownItems.Add(Item("Sales Report", (s, e) => OpenChild(new SalesReportForm())));
        rep.DropDownItems.Add(Item("Stock Report", (s, e) => OpenChild(new StockSummaryForm())));
        rep.DropDownItems.Add(Item("Item Ledger Report", (s, e) => OpenChild(new StockLedgerForm())));
        rep.DropDownItems.Add(Item("Stock Summary", (s, e) => OpenChild(new StockSummaryForm())));
        rep.DropDownItems.Add(Item("Account Statement", (s, e) => OpenChild(new LedgerForm())));
        rep.DropDownItems.Add(Item("Trial Balance", (s, e) => OpenChild(new TrialBalanceForm())));
        rep.DropDownItems.Add(Item("Profit && Loss Report", (s, e) => OpenChild(new ProfitLossForm())));
        rep.DropDownItems.Add(Item("Balance Sheet Report", (s, e) => OpenChild(new BalanceSheetForm())));

        // TOOLS
        var tools = new ToolStripMenuItem("Tools");
        tools.DropDownItems.Add(Item("Calculator", (s, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("calc.exe") { UseShellExecute = true })));
        tools.DropDownItems.Add(Item("Data Backup", (s, e) => { using var f = new DataBackupForm(); f.ShowDialog(this); }));
        tools.DropDownItems.Add(Item("Restore Backup", (s, e) => { using var f = new DataRestoreForm(); f.ShowDialog(this); }));
        tools.DropDownItems.Add(Item("Database Log", (s, e) => OpenChild(new DatabaseLogForm())));

        // UTILITIES
        var util = new ToolStripMenuItem("Utilities");
        util.DropDownItems.Add(Item("Settings", (s, e) => { using var f = new SettingsForm(); f.ShowDialog(this); }));
        util.DropDownItems.Add(Item("Company Profile", (s, e) => { using var f = new CompanyProfileForm(); f.ShowDialog(this); }));
        util.DropDownItems.Add(Item("Financial Year", (s, e) => OpenChild(new FinancialYearForm())));
        util.DropDownItems.Add(Item("Document Numbering", (s, e) => OpenChild(new DocumentNumberingForm())));

        // USERS
        var users = new ToolStripMenuItem("Users");
        users.DropDownItems.Add(Item("Manage Users", (s, e) => OpenChild(new ManageUsersForm())));
        users.DropDownItems.Add(Item("Change Password", (s, e) => { using var f = new ChangePasswordForm(); f.ShowDialog(this); }));
        users.DropDownItems.Add(Item("Logout", (s, e) => { MessageBox.Show("Logged out."); Application.Restart(); }));

        // WINDOW
        var window = new ToolStripMenuItem("Window");
        window.DropDownItems.Add(Item("Cascade", (s, e) => LayoutMdi(MdiLayout.Cascade)));
        window.DropDownItems.Add(Item("Tile Horizontal", (s, e) => LayoutMdi(MdiLayout.TileHorizontal)));
        window.DropDownItems.Add(Item("Tile Vertical", (s, e) => LayoutMdi(MdiLayout.TileVertical)));
        window.DropDownItems.Add(Item("Close All", (s, e) => { foreach (Form f in MdiChildren.ToArray()) f.Close(); }));

        // HELP
        var help = new ToolStripMenuItem("Help");
        help.DropDownItems.Add(Item("About", (s, e) => MessageBox.Show(
            "ERP Software - Inventory & Accounting System\nBuilt with C# WinForms + PostgreSQL",
            "About")));

        menu.Items.AddRange(new ToolStripItem[]
        {
            master, trans, inv, acc, rep, tools, util, users, window, help
        });

        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private static ToolStripMenuItem Item(string text, EventHandler handler)
    {
        var mi = new ToolStripMenuItem(text);
        mi.Click += handler;
        return mi;
    }

    private void NotImplemented(object? sender, EventArgs e)
    {
        string name = (sender as ToolStripMenuItem)?.Text ?? "This module";
        MessageBox.Show($"'{name}' is not implemented in this build.\n\n" +
                         "The database table(s) for it already exist in schema.sql — " +
                         "wire up a form the same way ItemMasterForm / SalesInvoiceForm are built.",
            "Not Implemented", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenChild(Form child)
    {
        // Avoid duplicate windows of the same type
        foreach (Form f in MdiChildren)
        {
            if (f.GetType() == child.GetType())
            {
                f.Activate();
                child.Dispose();
                return;
            }
        }
        MdiHelper.ShowCentered(this, child);
    }

    private void BuildStatusBar()
    {
        lblCompany.Text = "Company: -";
        lblFinancialYear.Text = "Financial Year: -";
        lblUser.Text = "User: -";
        lblDbStatus.Text = "Database: -";
        lblDateTime.Text = DateTime.Now.ToString("dd/MM/yyyy hh:mm tt");

        lblCompany.Spring = false;
        var spacer = new ToolStripStatusLabel { Spring = true };

        statusStrip.Items.AddRange(new ToolStripItem[]
        {
            lblCompany, new ToolStripSeparator(),
            lblFinancialYear, new ToolStripSeparator(),
            lblUser, spacer,
            lblDbStatus, new ToolStripSeparator(),
            lblDateTime
        });
        Controls.Add(statusStrip);
    }
}
