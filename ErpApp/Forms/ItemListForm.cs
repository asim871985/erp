using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches the "Item List" window in screenshot 2 — grid with Add New / Edit / Delete / Search.</summary>
public class ItemListForm : AppFormBase
{
    private readonly DataGridView grid = new();
    private readonly TextBox txtSearch = new() { Width = 220 };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnAddNew = new() { Text = "Add New" };
    private readonly Button btnEdit = new() { Text = "Edit" };
    private readonly Button btnDelete = new() { Text = "Delete" };

    public ItemListForm()
    {
        Text = "Item List";
        Width = 1050;
        Height = 550;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadGrid();
    }

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        btnAddNew.Click += (s, e) => OpenDialog(null);
        btnEdit.Click += (s, e) => EditSelected();
        btnDelete.Click += (s, e) => DeleteSelected();
        top.Controls.Add(btnAddNew);
        top.Controls.Add(btnEdit);
        top.Controls.Add(btnDelete);

        var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        btnSearch.Click += (s, e) => LoadGrid(txtSearch.Text.Trim());
        txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) LoadGrid(txtSearch.Text.Trim()); };
        searchPanel.Controls.Add(btnSearch);
        searchPanel.Controls.Add(txtSearch);
        searchPanel.Controls.Add(new Label { Text = "Search Item...", AutoSize = true, Padding = new Padding(0, 8, 5, 0) });

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.CellDoubleClick += (s, e) => EditSelected();

        Controls.Add(grid);
        Controls.Add(searchPanel);
        Controls.Add(top);
    }

    private void LoadGrid(string? search = null)
    {
        try
        {
            string sql = @"
                SELECT item_id AS ""ID"", item_name AS ""Item Name"", model AS ""Model"",
                       side_size AS ""Side/Size"", brand_name AS ""Brand"", uom_name AS ""UOM"",
                       category AS ""Category"", qty AS ""Qty"", rate AS ""Sales Price"",
                       purchase_price AS ""Purchase Price"", amount AS ""Amount"",
                       min_stock AS ""Reorder Level"", hsn_code AS ""HSN / Code"",
                       status AS ""Status""
                FROM vw_item_list";
            var pars = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " WHERE item_name ILIKE @s OR model ILIKE @s OR brand_name ILIKE @s";
                pars["s"] = $"%{search}%";
            }
            sql += " ORDER BY item_id";

            var table = DbHelper.ExecuteQuery(sql, pars);
            grid.DataSource = table;
            if (grid.Columns.Contains("ID")) grid.Columns["ID"]!.Visible = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load items: " + ex.Message);
        }
    }

    private void OpenDialog(int? itemId)
    {
        using var dlg = new AddItemDialog(itemId);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            LoadGrid(txtSearch.Text.Trim());
    }

    private void EditSelected()
    {
        if (grid.CurrentRow == null) { MessageBox.Show("Select a row to edit."); return; }
        OpenDialog(Convert.ToInt32(grid.CurrentRow.Cells["ID"].Value));
    }

    private void DeleteSelected()
    {
        if (grid.CurrentRow == null) { MessageBox.Show("Select a row to delete."); return; }
        if (MessageBox.Show("Delete selected item?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        int id = Convert.ToInt32(grid.CurrentRow.Cells["ID"].Value);
        try
        {
            DbHelper.ExecuteNonQuery("DELETE FROM item_master WHERE item_id=@id",
                new Dictionary<string, object?> { ["id"] = id });
            LoadGrid(txtSearch.Text.Trim());
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed (item may be used in an invoice): " + ex.Message);
        }
    }
}
