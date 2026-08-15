using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches the "Ledger" window in screenshot 2 — account picker, date range, running balance grid.</summary>
public class LedgerForm : AppFormBase
{
    private readonly ComboBox cboAccount = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnPrint = new() { Text = "Print" };

    private readonly Label lblName = new();
    private readonly Label lblAddress = new();
    private readonly Label lblMobile = new();
    private readonly Label lblOpeningBalance = new();
    private readonly Label lblCurrentBalance = new() { Font = new Font("Segoe UI", 10, FontStyle.Bold) };

    private readonly DataGridView grid = new();

    public LedgerForm()
    {
        Text = "Ledger";
        Width = 950;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadAccounts();
    }

    private void BuildLayout()
    {
        var topPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 45, ColumnCount = 7, Padding = new Padding(8) };
        topPanel.Controls.Add(new Label { Text = "Account", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        cboAccount.Dock = DockStyle.Fill;
        topPanel.Controls.Add(cboAccount, 1, 0);
        topPanel.Controls.Add(new Label { Text = "From Date", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        dtFrom.Dock = DockStyle.Fill;
        dtFrom.Value = new DateTime(DateTime.Today.Year, 1, 1);
        topPanel.Controls.Add(dtFrom, 3, 0);
        topPanel.Controls.Add(new Label { Text = "To Date", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4, 0);
        dtTo.Dock = DockStyle.Fill;
        dtTo.Value = DateTime.Today;
        topPanel.Controls.Add(dtTo, 5, 0);
        btnSearch.Dock = DockStyle.Fill;
        btnSearch.Click += (s, e) => LoadLedger();
        topPanel.Controls.Add(btnSearch, 6, 0);

        var mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 220, FixedPanel = FixedPanel.Panel1 };

        var infoGroup = new GroupBox { Text = "Account Info", Dock = DockStyle.Fill };
        var infoLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        for (int i = 0; i < infoLayout.RowCount; i++) infoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / infoLayout.RowCount));
        infoLayout.Controls.Add(Labeled("Name :", lblName));
        infoLayout.Controls.Add(Labeled("Address :", lblAddress));
        infoLayout.Controls.Add(Labeled("Mobile :", lblMobile));
        infoLayout.Controls.Add(Labeled("Opening Balance :", lblOpeningBalance));
        infoLayout.Controls.Add(Labeled("Current Balance :", lblCurrentBalance));
        infoGroup.Controls.Add(infoLayout);
        mainSplit.Panel1.Controls.Add(infoGroup);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        mainSplit.Panel2.Controls.Add(grid);

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        btnPrint.Click += (s, e) => MessageBox.Show("Wire this up to the pdf skill to export a printable ledger statement.");
        bottomPanel.Controls.Add(btnPrint);

        Controls.Add(mainSplit);
        Controls.Add(bottomPanel);
        Controls.Add(topPanel);
    }

    private static Panel Labeled(string caption, Label valueLabel)
    {
        var p = new Panel { Height = 30, Dock = DockStyle.Top };
        var cap = new Label { Text = caption, Dock = DockStyle.Left, Width = 110, TextAlign = ContentAlignment.MiddleLeft };
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        p.Controls.Add(valueLabel);
        p.Controls.Add(cap);
        return p;
    }

    private void LoadAccounts()
    {
        try
        {
            // Union of chart-of-accounts + customer/supplier "sub-ledgers"
            var table = DbHelper.ExecuteQuery(@"
                SELECT account_id, account_name FROM chart_of_accounts
                UNION
                SELECT c.account_id, c.customer_name FROM customer_master c WHERE c.account_id IS NOT NULL
                UNION
                SELECT s.account_id, s.supplier_name FROM supplier_master s WHERE s.account_id IS NOT NULL
                ORDER BY account_name");
            cboAccount.DisplayMember = "account_name";
            cboAccount.ValueMember = "account_id";
            cboAccount.DataSource = table;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load accounts: " + ex.Message);
        }
    }

    private void LoadLedger()
    {
        if (cboAccount.SelectedValue == null) { MessageBox.Show("Select an account."); return; }
        int accountId = Convert.ToInt32(cboAccount.SelectedValue);

        try
        {
            // Account info (try customer first, then supplier, then plain COA)
            var custInfo = DbHelper.ExecuteQuery("SELECT customer_name, address, mobile FROM customer_master WHERE account_id=@id",
                new Dictionary<string, object?> { ["id"] = accountId });
            if (custInfo.Rows.Count > 0)
            {
                lblName.Text = custInfo.Rows[0]["customer_name"].ToString();
                lblAddress.Text = custInfo.Rows[0]["address"]?.ToString() ?? "-";
                lblMobile.Text = custInfo.Rows[0]["mobile"]?.ToString() ?? "-";
            }
            else
            {
                lblName.Text = cboAccount.Text;
                lblAddress.Text = "-";
                lblMobile.Text = "-";
            }

            // Opening balance = the account's cumulative balance before the From
            // date: signed COA opening balance + all pre-period entries. This
            // matches Trial Balance / Balance Sheet (which use the same signed
            // opening logic), so the ledger's Current Balance agrees with them.
            var openingResult = DbHelper.ExecuteQuery(@"
                SELECT CASE WHEN balance_type='Dr' THEN opening_balance ELSE -opening_balance END
                       + COALESCE((SELECT SUM(debit - credit) FROM ledger_entry
                                   WHERE account_id=@id AND entry_date < @from), 0) AS opening
                FROM chart_of_accounts WHERE account_id=@id",
                new Dictionary<string, object?> { ["id"] = accountId, ["from"] = dtFrom.Value.Date });
            decimal openingBalance = openingResult.Rows.Count > 0 && openingResult.Rows[0]["opening"] != DBNull.Value
                ? Convert.ToDecimal(openingResult.Rows[0]["opening"]) : 0;
            lblOpeningBalance.Text = openingBalance.ToString("N2");

            var table = DbHelper.ExecuteQuery(@"
                SELECT entry_date AS ""Date"", voucher_no AS ""Vch No."", voucher_type AS ""Vch Type"",
                       particulars AS ""Particulars"", debit AS ""Debit"", credit AS ""Credit""
                FROM ledger_entry
                WHERE account_id=@id AND entry_date BETWEEN @from AND @to
                ORDER BY entry_date, entry_id",
                new Dictionary<string, object?> { ["id"] = accountId, ["from"] = dtFrom.Value.Date, ["to"] = dtTo.Value.Date });

            // running balance column
            table.Columns.Add("Balance", typeof(string));
            decimal running = Convert.ToDecimal(lblOpeningBalance.Text.Replace(",", ""));
            decimal totalDebit = 0, totalCredit = 0;
            foreach (DataRow row in table.Rows)
            {
                decimal debit = Convert.ToDecimal(row["Debit"]);
                decimal credit = Convert.ToDecimal(row["Credit"]);
                running += debit - credit;
                totalDebit += debit;
                totalCredit += credit;
                row["Balance"] = Math.Abs(running).ToString("N2") + (running >= 0 ? " Dr" : " Cr");
            }

            grid.DataSource = table;
            lblCurrentBalance.Text = Math.Abs(running).ToString("N2") + (running >= 0 ? " Dr" : " Cr");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load ledger: " + ex.Message);
        }
    }
}
