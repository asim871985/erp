using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches Inventory mockup form 5 "Reorder Level" — items whose stock has fallen below their minimum.</summary>
public class ReorderLevelForm : AppFormBase
{
    private readonly ComboBox cboItem = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly DataGridView grid = new();

    public ReorderLevelForm()
    {
        Text = "Reorder Level";
        Width = 900;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadItems();
        RunReport();
    }

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        top.Controls.Add(new Label { Text = "Item", AutoSize = true, Padding = new Padding(0, 8, 5, 0) });
        cboItem.Width = 250;
        top.Controls.Add(cboItem);
        btnSearch.Click += (s, e) => RunReport();
        top.Controls.Add(btnSearch);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.CellFormatting += Grid_CellFormatting;

        Controls.Add(grid);
        Controls.Add(top);
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (grid.Columns[e.ColumnIndex].Name != "Status") return;
        if (e.Value?.ToString() == "LOW")
        {
            e.CellStyle!.ForeColor = Color.Firebrick;
            e.CellStyle.Font = new Font(grid.Font, FontStyle.Bold);
        }
        else
        {
            e.CellStyle!.ForeColor = Color.SeaGreen;
        }
    }

    private void LoadItems()
    {
        try
        {
            var items = DbHelper.ExecuteQuery("SELECT item_id, item_name FROM item_master WHERE active ORDER BY item_name");
            var withAll = items.Clone();
            var blank = withAll.NewRow();
            blank["item_id"] = DBNull.Value;
            blank["item_name"] = "All";
            withAll.Rows.Add(blank);
            foreach (DataRow r in items.Rows) withAll.ImportRow(r);
            cboItem.DisplayMember = "item_name";
            cboItem.ValueMember = "item_id";
            cboItem.DataSource = withAll;
            cboItem.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load items: " + ex.Message);
        }
    }

    private void RunReport()
    {
        try
        {
            string sql = @"
                SELECT item_name AS ""Item Name"", model AS ""Model"", qty AS ""Current Stock"",
                       min_stock AS ""Reorder Level"",
                       CASE WHEN qty < min_stock THEN 'LOW' ELSE 'OK' END AS ""Status""
                FROM vw_item_list WHERE 1=1";
            var pars = new Dictionary<string, object?>();

            if (cboItem.SelectedValue is int itemId)
            {
                sql += " AND item_id=@id";
                pars["id"] = itemId;
            }
            sql += " ORDER BY item_name";

            grid.DataSource = DbHelper.ExecuteQuery(sql, pars);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }
}
