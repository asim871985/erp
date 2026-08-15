using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Accounting > Bank Summary — per-bank opening / cash-in / cash-out / closing
/// balances for a date range, built from the ledger for all BANK-type accounts.
/// Cash in Hand (1000) is not a bank account and stays on the Cash Flow
/// Statement's combined cash+bank view.
/// </summary>
public class BankSummaryForm : AppFormBase
{
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboBank = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnExport = new() { Text = "Export" };
    private readonly DataGridView grid = new();
    private readonly Label lblTotal = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

    public BankSummaryForm()
    {
        Text = "Bank Summary";
        Width = 900;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadBanks();
        RunReport();
    }

    private void BuildLayout()
    {
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 45, ColumnCount = 8, Padding = new Padding(8) };
        for (int i = 0; i < 8; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));

        top.Controls.Add(new Label { Text = "From", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        dtFrom.Dock = DockStyle.Fill;
        dtFrom.Value = new DateTime(DateTime.Today.Year, 1, 1);
        top.Controls.Add(dtFrom, 1, 0);

        top.Controls.Add(new Label { Text = "To", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        dtTo.Dock = DockStyle.Fill;
        dtTo.Value = DateTime.Today;
        top.Controls.Add(dtTo, 3, 0);

        top.Controls.Add(new Label { Text = "Bank", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4, 0);
        cboBank.Dock = DockStyle.Fill;
        top.Controls.Add(cboBank, 5, 0);

        btnSearch.Dock = DockStyle.Fill;
        btnSearch.Click += (s, e) => RunReport();
        top.Controls.Add(btnSearch, 6, 0);

        btnExport.Dock = DockStyle.Fill;
        btnExport.Click += BtnExport_Click;
        top.Controls.Add(btnExport, 7, 0);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 50 };
        lblTotal.Dock = DockStyle.Fill;
        bottom.Controls.Add(lblTotal);

        Controls.Add(grid);
        Controls.Add(bottom);
        Controls.Add(top);
    }

    private void LoadBanks()
    {
        try
        {
            // (All Banks) row carries a NULL value id, so SelectedValue is null when "All" is picked
            var banks = DbHelper.ExecuteQuery("SELECT account_id, account_name FROM chart_of_accounts WHERE account_type='BANK' AND active ORDER BY account_name");
            var withAll = banks.Clone();
            var blank = withAll.NewRow();
            blank["account_id"] = DBNull.Value;
            blank["account_name"] = "(All Banks)";
            withAll.Rows.Add(blank);
            foreach (DataRow r in banks.Rows) withAll.ImportRow(r);
            cboBank.DisplayMember = "account_name";
            cboBank.ValueMember = "account_id";
            cboBank.DataSource = withAll;
            cboBank.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load banks: " + ex.Message);
        }
    }

    private void RunReport()
    {
        try
        {
            var pars = new Dictionary<string, object?> { ["from"] = dtFrom.Value.Date, ["to"] = dtTo.Value.Date };
            string bankFilter = "";
            if (cboBank.SelectedValue is int bankId)
            {
                bankFilter = " AND a.account_id=@acc";
                pars["acc"] = bankId;
            }

            var rows = DbHelper.ExecuteQuery(@"
                SELECT a.account_name,
                       COALESCE(a.opening_balance,0) + COALESCE(SUM(CASE WHEN l.entry_date < @from THEN l.debit - l.credit ELSE 0 END),0) AS opening,
                       COALESCE(SUM(CASE WHEN l.entry_date BETWEEN @from AND @to THEN l.debit ELSE 0 END),0) AS cash_in,
                       COALESCE(SUM(CASE WHEN l.entry_date BETWEEN @from AND @to THEN l.credit ELSE 0 END),0) AS cash_out
                FROM chart_of_accounts a
                LEFT JOIN ledger_entry l ON l.account_id = a.account_id
                WHERE a.account_type='BANK' AND a.active" + bankFilter + @"
                GROUP BY a.account_id, a.account_name, a.opening_balance
                ORDER BY a.account_name", pars);

            var display = new DataTable();
            display.Columns.Add("Bank Account", typeof(string));
            display.Columns.Add("Opening Balance", typeof(string));
            display.Columns.Add("Cash In", typeof(string));
            display.Columns.Add("Cash Out", typeof(string));
            display.Columns.Add("Closing Balance", typeof(string));

            decimal totalOpening = 0, totalIn = 0, totalOut = 0, totalClosing = 0;
            foreach (DataRow r in rows.Rows)
            {
                decimal opening = Convert.ToDecimal(r["opening"]);
                decimal cashIn = Convert.ToDecimal(r["cash_in"]);
                decimal cashOut = Convert.ToDecimal(r["cash_out"]);
                decimal closing = opening + cashIn - cashOut;
                totalOpening += opening; totalIn += cashIn; totalOut += cashOut; totalClosing += closing;
                display.Rows.Add(r["account_name"].ToString(), opening.ToString("N2"), cashIn.ToString("N2"), cashOut.ToString("N2"), closing.ToString("N2"));
            }

            display.Rows.Add("TOTAL", totalOpening.ToString("N2"), totalIn.ToString("N2"), totalOut.ToString("N2"), totalClosing.ToString("N2"));

            grid.DataSource = display;
            lblTotal.Text = "Total Bank Balance: " + totalClosing.ToString("N2");
            lblTotal.ForeColor = totalClosing >= 0 ? Color.SeaGreen : Color.Firebrick;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (grid.DataSource is not DataTable table) return;
        using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "bank_summary.csv" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        using var writer = new StreamWriter(sfd.FileName);
        writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(v => $"\"{v}\"")));
        MessageBox.Show("Exported to " + sfd.FileName);
    }
}
