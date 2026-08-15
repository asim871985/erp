using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class SalesReportForm : AppFormBase
{
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboCustomer = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnExport = new() { Text = "Export" };
    private readonly DataGridView grid = new();
    private readonly Label lblTotal = new() { Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };

    public SalesReportForm()
    {
        Text = "Sales Report";
        Width = 950;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        BuildLayout();
        LoadCustomers();
        RunReport();
    }

    private void BuildLayout()
    {
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 45, ColumnCount = 7, Padding = new Padding(8) };
        top.Controls.Add(new Label { Text = "From", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        dtFrom.Dock = DockStyle.Fill;
        dtFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        top.Controls.Add(dtFrom, 1, 0);
        top.Controls.Add(new Label { Text = "To", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        dtTo.Dock = DockStyle.Fill;
        dtTo.Value = DateTime.Today;
        top.Controls.Add(dtTo, 3, 0);
        top.Controls.Add(new Label { Text = "Customer", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4, 0);
        cboCustomer.Dock = DockStyle.Fill;
        top.Controls.Add(cboCustomer, 5, 0);
        btnSearch.Dock = DockStyle.Fill;
        btnSearch.Click += (s, e) => RunReport();
        top.Controls.Add(btnSearch, 6, 0);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 45 };
        btnExport.Left = 10; btnExport.Top = 8; btnExport.Width = 90;
        btnExport.Click += BtnExport_Click;
        lblTotal.Dock = DockStyle.Right;
        lblTotal.Width = 300;
        bottom.Controls.Add(btnExport);
        bottom.Controls.Add(lblTotal);

        Controls.Add(grid);
        Controls.Add(bottom);
        Controls.Add(top);
    }

    private void LoadCustomers()
    {
        try
        {
            var table = DbHelper.ExecuteQuery("SELECT customer_id, customer_name FROM customer_master ORDER BY customer_name");
            var withAll = table.Clone();
            var blank = withAll.NewRow();
            blank["customer_id"] = DBNull.Value;
            blank["customer_name"] = "(All Customers)";
            withAll.Rows.Add(blank);
            foreach (DataRow r in table.Rows) withAll.ImportRow(r);
            cboCustomer.DisplayMember = "customer_name";
            cboCustomer.ValueMember = "customer_id";
            cboCustomer.DataSource = withAll;
            cboCustomer.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load customers: " + ex.Message);
        }
    }

    private void RunReport()
    {
        try
        {
            string sql = @"
                SELECT si.invoice_no AS ""Invoice No."", si.invoice_date AS ""Date"",
                       c.customer_name AS ""Customer"", si.payment_terms AS ""Terms"",
                       si.sub_total AS ""Sub Total"", si.discount AS ""Discount"",
                       si.tax AS ""Tax"", si.grand_total AS ""Grand Total""
                FROM sales_invoice si
                LEFT JOIN customer_master c ON c.customer_id = si.customer_id
                WHERE si.invoice_date BETWEEN @from AND @to";
            var pars = new Dictionary<string, object?> { ["from"] = dtFrom.Value.Date, ["to"] = dtTo.Value.Date };

            if (cboCustomer.SelectedValue is int custId)
            {
                sql += " AND si.customer_id=@cust";
                pars["cust"] = custId;
            }
            sql += " ORDER BY si.invoice_date, si.invoice_no";

            var table = DbHelper.ExecuteQuery(sql, pars);
            grid.DataSource = table;

            decimal total = 0;
            foreach (DataRow row in table.Rows) total += Convert.ToDecimal(row["Grand Total"]);
            lblTotal.Text = "Total Sales: " + total.ToString("N2") + $"  ({table.Rows.Count} invoices)";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (grid.DataSource is not DataTable table) return;
        using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "sales_report.csv" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        using var writer = new StreamWriter(sfd.FileName);
        writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(v => $"\"{v}\"")));
        MessageBox.Show("Exported to " + sfd.FileName);
    }
}
