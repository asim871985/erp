using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Cash Flow Statement (direct method): tracks money actually moving through the
/// "Cash in Hand" (1000) and "Bank Account" (1001) ledger accounts over a period —
/// these are the only accounts Receipt, Payment, and Contra Entry post to.
/// </summary>
public class CashFlowStatementForm : AppFormBase
{
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnExport = new() { Text = "Export" };
    private readonly DataGridView grid = new();
    private readonly Label lblClosing = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

    public CashFlowStatementForm()
    {
        Text = "Cash Flow Statement";
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
        dtFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
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
        lblClosing.Dock = DockStyle.Fill;
        bottom.Controls.Add(lblClosing);
        bottom.Controls.Add(btnExport);

        Controls.Add(grid);
        Controls.Add(bottom);
        Controls.Add(top);
    }

    private void RunReport()
    {
        try
        {
            // Opening cash+bank balance = net of all cash/bank ledger activity strictly before From Date
            var openingResult = DbHelper.ExecuteQuery(@"
                SELECT COALESCE(SUM(l.debit - l.credit), 0) AS opening
                FROM ledger_entry l
                JOIN chart_of_accounts a ON a.account_id = l.account_id
                WHERE (a.account_code = '1000' OR a.account_type = 'BANK') AND l.entry_date < @from",
                new Dictionary<string, object?> { ["from"] = dtFrom.Value.Date });
            decimal opening = openingResult.Rows.Count > 0 && openingResult.Rows[0]["opening"] != DBNull.Value
                ? Convert.ToDecimal(openingResult.Rows[0]["opening"]) : 0;

            // Breakdown by voucher type within the period
            var byType = DbHelper.ExecuteQuery(@"
                SELECT l.voucher_type,
                       COALESCE(SUM(l.debit),0) AS cash_in,
                       COALESCE(SUM(l.credit),0) AS cash_out
                FROM ledger_entry l
                JOIN chart_of_accounts a ON a.account_id = l.account_id
                WHERE (a.account_code = '1000' OR a.account_type = 'BANK') AND l.entry_date BETWEEN @from AND @to
                GROUP BY l.voucher_type
                ORDER BY l.voucher_type",
                new Dictionary<string, object?> { ["from"] = dtFrom.Value.Date, ["to"] = dtTo.Value.Date });

            var display = new DataTable();
            display.Columns.Add("Particulars", typeof(string));
            display.Columns.Add("Cash In", typeof(string));
            display.Columns.Add("Cash Out", typeof(string));

            display.Rows.Add("Opening Cash & Bank Balance", opening >= 0 ? opening.ToString("N2") : "", opening < 0 ? Math.Abs(opening).ToString("N2") : "");
            display.Rows.Add("", "", "");
            display.Rows.Add("CASH MOVEMENT BY SOURCE", "", "");

            decimal totalIn = 0, totalOut = 0;
            foreach (DataRow r in byType.Rows)
            {
                decimal cashIn = Convert.ToDecimal(r["cash_in"]);
                decimal cashOut = Convert.ToDecimal(r["cash_out"]);
                totalIn += cashIn;
                totalOut += cashOut;
                display.Rows.Add("    " + r["voucher_type"],
                    cashIn > 0 ? cashIn.ToString("N2") : "",
                    cashOut > 0 ? cashOut.ToString("N2") : "");
            }

            display.Rows.Add("Total Cash In", totalIn.ToString("N2"), "");
            display.Rows.Add("Total Cash Out", "", totalOut.ToString("N2"));
            display.Rows.Add("", "", "");

            decimal netMovement = totalIn - totalOut;
            decimal closing = opening + netMovement;
            display.Rows.Add("Net Cash Movement", netMovement >= 0 ? netMovement.ToString("N2") : "", netMovement < 0 ? Math.Abs(netMovement).ToString("N2") : "");
            display.Rows.Add("Closing Cash & Bank Balance", closing >= 0 ? closing.ToString("N2") : "", closing < 0 ? Math.Abs(closing).ToString("N2") : "");

            grid.DataSource = display;
            lblClosing.Text = "Closing Balance: " + closing.ToString("N2");
            lblClosing.ForeColor = closing >= 0 ? Color.SeaGreen : Color.Firebrick;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (grid.DataSource is not DataTable table) return;
        using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "cash_flow_statement.csv" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        using var writer = new StreamWriter(sfd.FileName);
        writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(v => $"\"{v}\"")));
        MessageBox.Show("Exported to " + sfd.FileName);
    }
}
