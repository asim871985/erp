using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Profit &amp; Loss (Income Statement) for a chosen period, built from INCOME/EXPENSE ledger activity.</summary>
public class ProfitLossForm : AppFormBase
{
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnExport = new() { Text = "Export" };
    private readonly DataGridView grid = new();
    private readonly Label lblNetResult = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

    public ProfitLossForm()
    {
        Text = "Profit & Loss Account";
        Width = 850;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        RunReport();
    }

    private void BuildLayout()
    {
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 45, ColumnCount = 5, Padding = new Padding(8) };
        top.Controls.Add(new Label { Text = "From", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        dtFrom.Dock = DockStyle.Fill;
        dtFrom.Value = new DateTime(DateTime.Today.Year, 1, 1);
        top.Controls.Add(dtFrom, 1, 0);
        top.Controls.Add(new Label { Text = "To", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        dtTo.Dock = DockStyle.Fill;
        dtTo.Value = DateTime.Today;
        top.Controls.Add(dtTo, 3, 0);
        btnSearch.Dock = DockStyle.Fill;
        btnSearch.Click += (s, e) => RunReport();
        top.Controls.Add(btnSearch, 4, 0);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 50 };
        btnExport.Left = 10; btnExport.Top = 12; btnExport.Width = 90;
        btnExport.Click += BtnExport_Click;
        lblNetResult.Dock = DockStyle.Fill;
        bottom.Controls.Add(lblNetResult);
        bottom.Controls.Add(btnExport);

        Controls.Add(grid);
        Controls.Add(bottom);
        Controls.Add(top);
    }

    private void RunReport()
    {
        try
        {
            var pars = new Dictionary<string, object?> { ["from"] = dtFrom.Value.Date, ["to"] = dtTo.Value.Date };

            var incomeRows = DbHelper.ExecuteQuery(@"
                SELECT a.account_name,
                       COALESCE(SUM(l.credit),0) - COALESCE(SUM(l.debit),0) AS amount
                FROM chart_of_accounts a
                JOIN ledger_entry l ON l.account_id = a.account_id AND l.entry_date BETWEEN @from AND @to
                WHERE a.account_type='INCOME'
                GROUP BY a.account_name HAVING COALESCE(SUM(l.credit),0) - COALESCE(SUM(l.debit),0) <> 0
                ORDER BY a.account_name", pars);

            var expenseRows = DbHelper.ExecuteQuery(@"
                SELECT a.account_name,
                       COALESCE(SUM(l.debit),0) - COALESCE(SUM(l.credit),0) AS amount
                FROM chart_of_accounts a
                JOIN ledger_entry l ON l.account_id = a.account_id AND l.entry_date BETWEEN @from AND @to
                WHERE a.account_type='EXPENSE'
                GROUP BY a.account_name HAVING COALESCE(SUM(l.debit),0) - COALESCE(SUM(l.credit),0) <> 0
                ORDER BY a.account_name", pars);

            var display = new DataTable();
            display.Columns.Add("Particulars", typeof(string));
            display.Columns.Add("Amount", typeof(string));

            display.Rows.Add("INCOME", "");
            decimal totalIncome = 0;
            foreach (DataRow r in incomeRows.Rows)
            {
                decimal amt = Convert.ToDecimal(r["amount"]);
                totalIncome += amt;
                display.Rows.Add("    " + r["account_name"], amt.ToString("N2"));
            }
            display.Rows.Add("Total Income", totalIncome.ToString("N2"));
            display.Rows.Add("", "");

            display.Rows.Add("EXPENSES", "");
            decimal totalExpense = 0;
            foreach (DataRow r in expenseRows.Rows)
            {
                decimal amt = Convert.ToDecimal(r["amount"]);
                totalExpense += amt;
                display.Rows.Add("    " + r["account_name"], amt.ToString("N2"));
            }
            display.Rows.Add("Total Expenses", totalExpense.ToString("N2"));

            grid.DataSource = display;

            decimal net = totalIncome - totalExpense;
            lblNetResult.Text = (net >= 0 ? "Net Profit: " : "Net Loss: ") + Math.Abs(net).ToString("N2");
            lblNetResult.ForeColor = net >= 0 ? Color.SeaGreen : Color.Firebrick;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (grid.DataSource is not DataTable table) return;
        using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "profit_and_loss.csv" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        using var writer = new StreamWriter(sfd.FileName);
        writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(v => $"\"{v}\"")));
        MessageBox.Show("Exported to " + sfd.FileName);
    }
}
