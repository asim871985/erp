using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches Inventory mockup form 6 "Item Price List" — Sales Price vs Purchase Price by item.</summary>
public class ItemPriceListForm : AppFormBase
{
    private readonly ComboBox cboBrand = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboUom = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnExport = new() { Text = "Export" };
    private readonly DataGridView grid = new();

    public ItemPriceListForm()
    {
        Text = "Item Price List";
        Width = 950;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadFilters();
        RunReport();
    }

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        top.Controls.Add(new Label { Text = "Brand", AutoSize = true, Padding = new Padding(0, 8, 5, 0) });
        cboBrand.Width = 180;
        top.Controls.Add(cboBrand);
        top.Controls.Add(new Label { Text = "UOM", AutoSize = true, Padding = new Padding(10, 8, 5, 0) });
        cboUom.Width = 150;
        top.Controls.Add(cboUom);
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

        Controls.Add(grid);
        Controls.Add(bottom);
        Controls.Add(top);
    }

    private void LoadFilters()
    {
        try
        {
            var brands = DbHelper.ExecuteQuery("SELECT brand_name FROM brand_master ORDER BY brand_name");
            cboBrand.Items.Add("All");
            foreach (DataRow r in brands.Rows) cboBrand.Items.Add(r["brand_name"].ToString()!);
            cboBrand.SelectedIndex = 0;

            var uoms = DbHelper.ExecuteQuery("SELECT uom_name FROM uom_master ORDER BY uom_name");
            cboUom.Items.Add("All");
            foreach (DataRow r in uoms.Rows) cboUom.Items.Add(r["uom_name"].ToString()!);
            cboUom.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load filters: " + ex.Message);
        }
    }

    private void RunReport()
    {
        try
        {
            string sql = @"
                SELECT item_name AS ""Item Name"", model AS ""Model"", brand_name AS ""Brand"",
                       uom_name AS ""UOM"", rate AS ""Sales Price"", purchase_price AS ""Purchase Price""
                FROM vw_item_list WHERE 1=1";
            var pars = new Dictionary<string, object?>();

            if (cboBrand.SelectedItem?.ToString() is string b && b != "All") { sql += " AND brand_name=@b"; pars["b"] = b; }
            if (cboUom.SelectedItem?.ToString() is string u && u != "All") { sql += " AND uom_name=@u"; pars["u"] = u; }
            sql += " ORDER BY item_name";

            grid.DataSource = DbHelper.ExecuteQuery(sql, pars);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (grid.DataSource is not DataTable table) return;
        using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "item_price_list.csv" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        using var writer = new StreamWriter(sfd.FileName);
        writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(v => $"\"{v}\"")));
        MessageBox.Show("Exported to " + sfd.FileName);
    }
}
