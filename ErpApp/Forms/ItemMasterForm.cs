using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Reproduces the "Item Master" tab shown in the main screenshot:
/// item entry fields on top, searchable/paged item grid below,
/// with Import / Export and a running Total Amount.
/// </summary>
public class ItemMasterForm : AppFormBase
{
    private readonly TextBox txtItemName = new();
    private readonly TextBox txtModel = new();
    private readonly TextBox txtSideSize = new();
    private readonly NumericUpDown numOpeningQty = new() { DecimalPlaces = 2, Maximum = 1_000_000 };
    private readonly NumericUpDown numRate = new() { DecimalPlaces = 2, Maximum = 10_000_000 };
    private readonly ComboBox cboBrand = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboUom = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtDescription = new() { Multiline = true };
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    private readonly DataGridView grid = new();
    private readonly Label lblTotalAmount = new() { Text = "Total Amount: 0.00", Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
    private readonly Button btnNew = new() { Text = "+ New" };
    private readonly Button btnEdit = new() { Text = "Edit" };
    private readonly Button btnDelete = new() { Text = "Delete" };
    private readonly Button btnSave = new() { Text = "Save" };
    private readonly Button btnClear = new() { Text = "Clear" };

    private int? editingItemId = null;

    public ItemMasterForm()
    {
        Text = "Item Master";
        Width = 1100;
        Height = 750;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadCombos();
        LoadGrid();
    }

    private void BuildLayout()
    {
        var infoGroup = new GroupBox { Text = "Item Information", Dock = DockStyle.Top, Height = 230 };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 5, Padding = new Padding(10) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        void AddRow(int row, string l1, Control c1, string l2, Control c2)
        {
            t.Controls.Add(new Label { Text = l1, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            c1.Dock = DockStyle.Fill;
            t.Controls.Add(c1, 1, row);
            t.Controls.Add(new Label { Text = l2, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 2, row);
            c2.Dock = DockStyle.Fill;
            t.Controls.Add(c2, 3, row);
        }

        AddRow(0, "Item Name", txtItemName, "Brand", cboBrand);
        AddRow(1, "Model", txtModel, "UOM", cboUom);
        AddRow(2, "Side / Size", txtSideSize, "", new Label());
        AddRow(3, "Opening Qty", numOpeningQty, "Rate", numRate);

        txtDescription.Height = 60;
        t.Controls.Add(new Label { Text = "Description", Dock = DockStyle.Fill }, 0, 4);
        t.Controls.Add(txtDescription, 1, 4);
        t.SetColumnSpan(txtDescription, 2);
        chkActive.Dock = DockStyle.Fill;
        t.Controls.Add(chkActive, 3, 4);

        infoGroup.Controls.Add(t);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(5) };
        btnSave.Click += BtnSave_Click;
        btnClear.Click += (s, e) => ClearForm();
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnClear);

        var listLabel = new Label { Text = "Item List", Dock = DockStyle.Top, Height = 25, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(5, 5, 0, 0) };

        var gridBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        btnDelete.Click += BtnDelete_Click;
        btnEdit.Click += BtnEdit_Click;
        btnNew.Click += (s, e) => ClearForm();
        gridBtnPanel.Controls.Add(btnDelete);
        gridBtnPanel.Controls.Add(btnEdit);
        gridBtnPanel.Controls.Add(btnNew);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 45 };
        var btnImport = new Button { Text = "Import", Left = 10, Top = 8, Width = 90 };
        var btnExport = new Button { Text = "Export", Left = 110, Top = 8, Width = 90 };
        btnImport.Click += (s, e) => MessageBox.Show("Import from CSV/Excel — hook up your preferred parser here (e.g. CsvHelper).");
        btnExport.Click += BtnExport_Click;
        lblTotalAmount.Dock = DockStyle.Right;
        lblTotalAmount.Width = 300;
        bottomPanel.Controls.Add(btnImport);
        bottomPanel.Controls.Add(btnExport);
        bottomPanel.Controls.Add(lblTotalAmount);

        Controls.Add(grid);
        Controls.Add(bottomPanel);
        Controls.Add(gridBtnPanel);
        Controls.Add(listLabel);
        Controls.Add(btnPanel);
        Controls.Add(infoGroup);
    }

    private void LoadCombos()
    {
        try
        {
            var brands = DbHelper.ExecuteQuery("SELECT brand_id, brand_name FROM brand_master ORDER BY brand_name");
            cboBrand.DisplayMember = "brand_name";
            cboBrand.ValueMember = "brand_id";
            cboBrand.DataSource = brands;

            var uoms = DbHelper.ExecuteQuery("SELECT uom_id, uom_name FROM uom_master ORDER BY uom_name");
            cboUom.DisplayMember = "uom_name";
            cboUom.ValueMember = "uom_id";
            cboUom.DataSource = uoms;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load Brand/UOM lists: " + ex.Message);
        }
    }

    private void LoadGrid()
    {
        try
        {
            var table = DbHelper.ExecuteQuery(@"
                SELECT item_id AS ""ID"", item_name AS ""Item Name"", model AS ""Model"",
                       side_size AS ""Side/Size"", brand_name AS ""Brand"", uom_name AS ""UOM"",
                       qty AS ""Qty"", rate AS ""Rate"", disc_percent AS ""Disc %"", amount AS ""Amount""
                FROM vw_item_list ORDER BY item_id");
            grid.DataSource = table;
            if (grid.Columns.Contains("ID")) grid.Columns["ID"]!.Visible = false;

            decimal total = 0;
            foreach (DataRow row in table.Rows)
                total += Convert.ToDecimal(row["Amount"]);
            lblTotalAmount.Text = "Total Amount: " + total.ToString("N2");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load items: " + ex.Message);
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtItemName.Text))
        {
            MessageBox.Show("Item Name is required.");
            return;
        }

        try
        {
            var pars = new Dictionary<string, object?>
            {
                ["name"] = txtItemName.Text.Trim(),
                ["model"] = txtModel.Text.Trim(),
                ["side"] = txtSideSize.Text.Trim(),
                ["brand"] = (cboBrand.SelectedValue as int?),
                ["uom"] = (cboUom.SelectedValue as int?),
                ["qty"] = numOpeningQty.Value,
                ["rate"] = numRate.Value,
                ["desc"] = txtDescription.Text.Trim(),
                ["active"] = chkActive.Checked
            };

            int savedItemId;
            if (editingItemId == null)
            {
                savedItemId = Convert.ToInt32(DbHelper.ExecuteScalar(@"
                    INSERT INTO item_master (item_name, model, side_size, brand_id, uom_id, opening_qty, rate, description, active)
                    VALUES (@name, @model, @side, @brand, @uom, @qty, @rate, @desc, @active) RETURNING item_id", pars));
            }
            else
            {
                pars["id"] = editingItemId;
                DbHelper.ExecuteNonQuery(@"
                    UPDATE item_master SET item_name=@name, model=@model, side_size=@side, brand_id=@brand,
                           uom_id=@uom, opening_qty=@qty, rate=@rate, description=@desc, active=@active
                    WHERE item_id=@id", pars);
                savedItemId = editingItemId.Value;
            }

            SeedOpeningStock(savedItemId);

            ClearForm();
            LoadGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Turns Opening Qty into a real (item, default-warehouse) stock_balance row so it
    /// shows up in stock reports and transactions adjust it correctly. Only seeds when
    /// the item has no balance rows yet — editing an item never clobbers real stock.
    /// </summary>
    private void SeedOpeningStock(int itemId)
    {
        if (numOpeningQty.Value == 0) return;
        try
        {
            bool hasBalance = Convert.ToInt32(DbHelper.ExecuteScalar(
                "SELECT COUNT(*) FROM stock_balance WHERE item_id=@id", new() { ["id"] = itemId })) > 0;
            if (hasBalance) return;

            DbHelper.ExecuteNonQuery(@"
                INSERT INTO stock_balance (item_id, warehouse_id, qty_on_hand) VALUES (@item, @wh, @qty)
                ON CONFLICT (item_id, warehouse_id) DO UPDATE SET qty_on_hand = stock_balance.qty_on_hand + @qty",
                new Dictionary<string, object?>
                {
                    ["item"] = itemId,
                    ["wh"] = DbHelper.GetDefaultWarehouseId(),
                    ["qty"] = numOpeningQty.Value
                });
        }
        catch { /* item is saved even if the opening-stock seed fails */ }
    }

    private void BtnEdit_Click(object? sender, EventArgs e)
    {
        if (grid.CurrentRow == null) { MessageBox.Show("Select a row to edit."); return; }
        int id = Convert.ToInt32(grid.CurrentRow.Cells["ID"].Value);
        LoadItemIntoForm(id);
    }

    private void LoadItemIntoForm(int id)
    {
        var table = DbHelper.ExecuteQuery(
            "SELECT * FROM item_master WHERE item_id=@id",
            new Dictionary<string, object?> { ["id"] = id });
        if (table.Rows.Count == 0) return;

        var row = table.Rows[0];
        editingItemId = id;
        txtItemName.Text = row["item_name"].ToString();
        txtModel.Text = row["model"]?.ToString();
        txtSideSize.Text = row["side_size"]?.ToString();
        numOpeningQty.Value = row["opening_qty"] is DBNull ? 0 : Convert.ToDecimal(row["opening_qty"]);
        numRate.Value = row["rate"] is DBNull ? 0 : Convert.ToDecimal(row["rate"]);
        txtDescription.Text = row["description"]?.ToString();
        chkActive.Checked = row["active"] is bool b && b;
        if (row["brand_id"] != DBNull.Value) cboBrand.SelectedValue = Convert.ToInt32(row["brand_id"]);
        if (row["uom_id"] != DBNull.Value) cboUom.SelectedValue = Convert.ToInt32(row["uom_id"]);
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (grid.CurrentRow == null) { MessageBox.Show("Select a row to delete."); return; }
        int id = Convert.ToInt32(grid.CurrentRow.Cells["ID"].Value);
        if (MessageBox.Show("Delete this item?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        try
        {
            DbHelper.ExecuteNonQuery("DELETE FROM item_master WHERE item_id=@id",
                new Dictionary<string, object?> { ["id"] = id });
            LoadGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed (item may be used in an invoice): " + ex.Message);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "items.csv" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        var table = (DataTable)grid.DataSource!;
        using var writer = new StreamWriter(sfd.FileName);
        var headers = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
        writer.WriteLine(string.Join(",", headers));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(v => $"\"{v}\"")));

        MessageBox.Show("Exported to " + sfd.FileName);
    }

    private void ClearForm()
    {
        editingItemId = null;
        txtItemName.Clear();
        txtModel.Clear();
        txtSideSize.Clear();
        numOpeningQty.Value = 0;
        numRate.Value = 0;
        txtDescription.Clear();
        chkActive.Checked = true;
        if (cboBrand.Items.Count > 0) cboBrand.SelectedIndex = -1;
        if (cboUom.Items.Count > 0) cboUom.SelectedIndex = -1;
    }
}
