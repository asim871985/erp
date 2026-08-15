using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Balance Sheet as of a chosen date. Assets and Liabilities/Equity balances use the same
/// signed-opening-balance logic as Trial Balance. Cumulative Income − Expense up to the
/// As-Of date is folded into Equity as "Current Earnings" so Assets = Liabilities + Equity.
/// </summary>
public class BalanceSheetForm : AppFormBase
{
    private readonly DateTimePicker dtAsOf = new() { Format = DateTimePickerFormat.Short };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnExport = new() { Text = "Export" };
    private readonly DataGridView grid = new();
    private readonly Label lblStatus = new() { Font = new Font("Segoe UI", 9, FontStyle.Bold) };

    public BalanceSheetForm()
    {
        Text = "Balance Sheet";
        Width = 850;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        RunReport();
    }

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        top.Controls.Add(new Label { Text = "As of Date", AutoSize = true, Padding = new Padding(0, 8, 5, 0) });
        dtAsOf.Value = DateTime.Today;
        top.Controls.Add(dtAsOf);
        btnSearch.Click += (s, e) => RunReport();
        top.Controls.Add(btnSearch);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        btnExport.Left = 10; btnExport.Top = 8; btnExport.Width = 90;
        btnExport.Click += BtnExport_Click;
        bottom.Controls.Add(btnExport);

        lblStatus.Dock = DockStyle.Top;
        lblStatus.Height = 25;
        lblStatus.Padding = new Padding(8, 4, 0, 0);

        Controls.Add(grid);
        Controls.Add(bottom);
        Controls.Add(lblStatus);
        Controls.Add(top);
    }

    private decimal SignedBalance(DataRow r)
    {
        decimal opening = Convert.ToDecimal(r["opening_balance"]);
        string balanceType = r["balance_type"].ToString() ?? "Dr";
        decimal signedOpening = balanceType == "Dr" ? opening : -opening;
        decimal debitSum = Convert.ToDecimal(r["total_debit"]);
        decimal creditSum = Convert.ToDecimal(r["total_credit"]);
        return signedOpening + debitSum - creditSum;
    }

    private void RunReport()
    {
        try
        {
            var accounts = DbHelper.ExecuteQuery(@"
                SELECT a.account_name, a.account_type, a.balance_type, a.opening_balance,
                       COALESCE(SUM(CASE WHEN l.entry_date <= @asof THEN l.debit ELSE 0 END), 0) AS total_debit,
                       COALESCE(SUM(CASE WHEN l.entry_date <= @asof THEN l.credit ELSE 0 END), 0) AS total_credit
                FROM chart_of_accounts a
                LEFT JOIN ledger_entry l ON l.account_id = a.account_id
                WHERE a.active AND a.account_type IN ('ASSET','BANK','LIABILITY','EQUITY')
                GROUP BY a.account_id, a.account_name, a.account_type, a.balance_type, a.opening_balance
                ORDER BY a.account_name",
                new Dictionary<string, object?> { ["asof"] = dtAsOf.Value.Date });

            var display = new DataTable();
            display.Columns.Add("Particulars", typeof(string));
            display.Columns.Add("Amount", typeof(string));

            decimal totalAssets = 0, totalLiabilities = 0, totalEquity = 0;

            display.Rows.Add("ASSETS", "");
            foreach (DataRow r in accounts.Rows)
            {
                if (r["account_type"].ToString() is not ("ASSET" or "BANK")) continue;
                decimal bal = SignedBalance(r);
                if (bal == 0) continue;
                totalAssets += bal;
                display.Rows.Add("    " + r["account_name"], bal.ToString("N2"));
            }
            display.Rows.Add("Total Assets", totalAssets.ToString("N2"));
            display.Rows.Add("", "");

            display.Rows.Add("LIABILITIES", "");
            foreach (DataRow r in accounts.Rows)
            {
                if (r["account_type"].ToString() != "LIABILITY") continue;
                decimal bal = -SignedBalance(r); // liabilities are naturally Cr; flip sign to show as positive
                if (bal == 0) continue;
                totalLiabilities += bal;
                display.Rows.Add("    " + r["account_name"], bal.ToString("N2"));
            }
            display.Rows.Add("Total Liabilities", totalLiabilities.ToString("N2"));
            display.Rows.Add("", "");

            display.Rows.Add("EQUITY", "");
            foreach (DataRow r in accounts.Rows)
            {
                if (r["account_type"].ToString() != "EQUITY") continue;
                decimal bal = -SignedBalance(r); // equity is naturally Cr
                if (bal == 0) continue;
                totalEquity += bal;
                display.Rows.Add("    " + r["account_name"], bal.ToString("N2"));
            }

            // Fold cumulative Income - Expense up to the As-Of date into Equity as Current Earnings
            var plResult = DbHelper.ExecuteQuery(@"
                SELECT
                  COALESCE(SUM(CASE WHEN a.account_type='INCOME' THEN l.credit - l.debit ELSE 0 END),0) -
                  COALESCE(SUM(CASE WHEN a.account_type='EXPENSE' THEN l.debit - l.credit ELSE 0 END),0) AS net_earnings
                FROM ledger_entry l JOIN chart_of_accounts a ON a.account_id = l.account_id
                WHERE l.entry_date <= @asof AND a.account_type IN ('INCOME','EXPENSE')",
                new Dictionary<string, object?> { ["asof"] = dtAsOf.Value.Date });
            decimal currentEarnings = plResult.Rows.Count > 0 && plResult.Rows[0]["net_earnings"] != DBNull.Value
                ? Convert.ToDecimal(plResult.Rows[0]["net_earnings"]) : 0;
            display.Rows.Add("    Current Earnings (Income - Expenses)", currentEarnings.ToString("N2"));
            totalEquity += currentEarnings;

            display.Rows.Add("Total Equity", totalEquity.ToString("N2"));
            display.Rows.Add("", "");
            display.Rows.Add("Total Liabilities + Equity", (totalLiabilities + totalEquity).ToString("N2"));

            grid.DataSource = display;

            decimal diff = totalAssets - (totalLiabilities + totalEquity);
            bool balanced = Math.Round(diff, 2) == 0;
            lblStatus.Text = balanced
                ? "\u2713 Balanced: Assets = Liabilities + Equity"
                : $"\u26A0 Out of balance by {Math.Abs(diff):N2} (Assets vs Liabilities+Equity)";
            lblStatus.ForeColor = balanced ? Color.SeaGreen : Color.Firebrick;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (grid.DataSource is not DataTable table) return;
        using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "balance_sheet.csv" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        using var writer = new StreamWriter(sfd.FileName);
        writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(v => $"\"{v}\"")));
        MessageBox.Show("Exported to " + sfd.FileName);
    }
}
