using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Trial Balance: every account's closing balance as of a chosen date, split into
/// Debit/Credit columns by sign. Opening balance is converted to a signed debit-equivalent
/// using each account's balance_type before ledger activity up to the As-Of date is applied.
/// </summary>
public class TrialBalanceForm : AppFormBase
{
    private readonly DateTimePicker dtAsOf = new() { Format = DateTimePickerFormat.Short };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnExport = new() { Text = "Export" };
    private readonly DataGridView grid = new();
    private readonly Label lblStatus = new() { Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    private readonly Label lblTotals = new() { Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };

    public TrialBalanceForm()
    {
        Text = "Trial Balance";
        Width = 900;
        Height = 620;
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

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 45, ColumnCount = 2 };
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        btnExport.Left = 10;
        var exportPanel = new Panel { Dock = DockStyle.Fill };
        btnExport.Top = 8;
        btnExport.Click += BtnExport_Click;
        exportPanel.Controls.Add(btnExport);
        bottom.Controls.Add(exportPanel, 0, 0);
        lblTotals.Dock = DockStyle.Fill;
        bottom.Controls.Add(lblTotals, 1, 0);

        Controls.Add(grid);
        Controls.Add(bottom);
        Controls.Add(lblStatus);
        Controls.Add(top);
    }

    private void RunReport()
    {
        try
        {
            var accounts = DbHelper.ExecuteQuery(@"
                SELECT a.account_code, a.account_name, a.account_type, a.balance_type, a.opening_balance,
                       COALESCE(SUM(CASE WHEN l.entry_date <= @asof THEN l.debit ELSE 0 END), 0) AS total_debit,
                       COALESCE(SUM(CASE WHEN l.entry_date <= @asof THEN l.credit ELSE 0 END), 0) AS total_credit
                FROM chart_of_accounts a
                LEFT JOIN ledger_entry l ON l.account_id = a.account_id
                WHERE a.active
                GROUP BY a.account_id, a.account_code, a.account_name, a.account_type, a.balance_type, a.opening_balance
                ORDER BY a.account_code NULLS LAST, a.account_name",
                new Dictionary<string, object?> { ["asof"] = dtAsOf.Value.Date });

            var display = new DataTable();
            display.Columns.Add("Code", typeof(string));
            display.Columns.Add("Account Name", typeof(string));
            display.Columns.Add("Type", typeof(string));
            display.Columns.Add("Debit", typeof(string));
            display.Columns.Add("Credit", typeof(string));

            decimal totalDebit = 0, totalCredit = 0;
            foreach (DataRow r in accounts.Rows)
            {
                decimal opening = Convert.ToDecimal(r["opening_balance"]);
                string balanceType = r["balance_type"].ToString() ?? "Dr";
                decimal signedOpening = balanceType == "Dr" ? opening : -opening;

                decimal debitSum = Convert.ToDecimal(r["total_debit"]);
                decimal creditSum = Convert.ToDecimal(r["total_credit"]);
                decimal net = signedOpening + debitSum - creditSum;

                if (net == 0) continue; // skip zero-balance accounts, like a real trial balance would

                string debitCol = net > 0 ? net.ToString("N2") : "";
                string creditCol = net < 0 ? Math.Abs(net).ToString("N2") : "";
                if (net > 0) totalDebit += net; else totalCredit += Math.Abs(net);

                display.Rows.Add(r["account_code"]?.ToString() ?? "-", r["account_name"].ToString(),
                    r["account_type"].ToString(), debitCol, creditCol);
            }

            grid.DataSource = display;
            lblTotals.Text = $"Total Debit: {totalDebit:N2}    Total Credit: {totalCredit:N2}";

            bool balanced = Math.Round(totalDebit - totalCredit, 2) == 0;
            lblStatus.Text = balanced
                ? "\u2713 Books are balanced (Total Debit = Total Credit)"
                : $"\u26A0 Out of balance by {Math.Abs(totalDebit - totalCredit):N2} — check for unbalanced manual entries.";
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
        using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "trial_balance.csv" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        using var writer = new StreamWriter(sfd.FileName);
        writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(v => $"\"{v}\"")));
        MessageBox.Show("Exported to " + sfd.FileName);
    }
}
