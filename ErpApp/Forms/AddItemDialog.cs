using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches the "Add New Item" popup in screenshot 2, extended with the
/// Category / Barcode / Item Type / Purchase Price / Reorder Level fields shown
/// in the Inventory menu mockup's "Items" form.</summary>
public class AddItemDialog : AppFormBase
{
    public int? SavedItemId { get; private set; }

    private readonly TextBox txtItemName = new();
    private readonly TextBox txtModel = new();
    private readonly TextBox txtSideSize = new();
    private readonly TextBox txtBrand = new();
    private readonly ComboBox cboUom = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboItemType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboCategory = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox txtBarcode = new();
    private readonly NumericUpDown numOpeningQty = new() { DecimalPlaces = 2, Maximum = 1_000_000 };
    private readonly NumericUpDown numRate = new() { DecimalPlaces = 2, Maximum = 10_000_000 };
    private readonly NumericUpDown numPurchasePrice = new() { DecimalPlaces = 2, Maximum = 10_000_000 };
    private readonly NumericUpDown numReorderLevel = new() { DecimalPlaces = 2, Maximum = 1_000_000 };
    private readonly NumericUpDown numTax = new() { DecimalPlaces = 2, Maximum = 100 };
    private readonly TextBox txtHsn = new();
    private readonly TextBox txtDescription = new() { Multiline = true };
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };
    private readonly PictureBox picBox = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.WhiteSmoke,
        SizeMode = PictureBoxSizeMode.Zoom,
        Width = 200, Height = 160
    };

    private readonly int? editItemId;

    public AddItemDialog(int? itemId = null)
    {
        editItemId = itemId;
        Text = "Add New Item";
        Width = 800;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        BuildLayout();
        LoadUom();
        LoadCategories();
        if (editItemId != null) LoadForEdit(editItemId.Value);
    }

    private void BuildLayout()
    {
        var title = new Label { Text = "Add New Item", Dock = DockStyle.Top, Height = 35, Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 8, Padding = new Padding(10) };
        for (int i = 0; i < 4; i++) main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        for (int i = 0; i < main.RowCount; i++) main.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / main.RowCount));

        void L(int row, int col, string text) => main.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, col, row);
        void C(int row, int col, Control c, int span = 1) { c.Dock = DockStyle.Fill; main.Controls.Add(c, col, row); if (span > 1) main.SetColumnSpan(c, span); }

        L(0, 0, "Item Name *"); C(0, 1, txtItemName, 2);
        picBox.Anchor = AnchorStyles.Top;
        var picPanel = new Panel { Dock = DockStyle.Fill };
        picBox.Location = new Point(0, 0);
        picPanel.Controls.Add(picBox);
        var lblNoImage = new Label { Text = "No Image", ForeColor = Color.Gray, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        picBox.Controls.Add(lblNoImage);
        main.Controls.Add(picPanel, 4, 0);
        main.SetRowSpan(picPanel, 4);

        cboItemType.Items.AddRange(new object[] { "Stock Item", "Service Item", "Non-Stock Item" });
        cboItemType.SelectedIndex = 0;
        L(1, 0, "Model"); C(1, 1, txtModel);
        L(1, 2, "Item Type"); C(1, 3, cboItemType);

        L(2, 0, "Side / Size"); C(2, 1, txtSideSize);
        L(2, 2, "Barcode"); C(2, 3, txtBarcode);

        L(3, 0, "Brand"); C(3, 1, txtBrand);
        L(3, 2, "UOM"); C(3, 3, cboUom);

        L(4, 0, "Category"); C(4, 1, cboCategory);
        L(4, 2, "Reorder Level"); C(4, 3, numReorderLevel);

        L(5, 0, "Opening Qty"); C(5, 1, numOpeningQty);
        L(5, 2, "Sales Price"); C(5, 3, numRate);

        L(6, 0, "Purchase Price"); C(6, 1, numPurchasePrice);
        L(6, 2, "Tax %"); C(6, 3, numTax);

        // HSN sits in its own labeled panel under the picture box (column 4 is
        // reserved for the picture in rows 0-3, so we build a mini label+field here).
        var hsnPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        hsnPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        hsnPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        hsnPanel.Controls.Add(new Label { Text = "HSN / Code", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        txtHsn.Dock = DockStyle.Fill;
        hsnPanel.Controls.Add(txtHsn, 0, 1);
        main.Controls.Add(hsnPanel, 4, 6);

        txtDescription.Height = 55;
        L(7, 0, "Description"); C(7, 1, txtDescription, 3);
        chkActive.Dock = DockStyle.Fill;
        main.Controls.Add(chkActive, 4, 7);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(10) };
        var btnSave = new Button { Text = "Save", Width = 90 };
        var btnClear = new Button { Text = "Clear", Width = 90 };
        var btnCancel = new Button { Text = "Cancel", Width = 90 };
        btnSave.Click += BtnSave_Click;
        btnClear.Click += (s, e) => ClearForm();
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnClear);
        btnPanel.Controls.Add(btnCancel);

        Controls.Add(main);
        Controls.Add(btnPanel);
        Controls.Add(title);
    }

    private void LoadUom()
    {
        try
        {
            var uoms = DbHelper.ExecuteQuery("SELECT uom_id, uom_name FROM uom_master ORDER BY uom_name");
            cboUom.DisplayMember = "uom_name";
            cboUom.ValueMember = "uom_id";
            cboUom.DataSource = uoms;
        }
        catch { /* UOM list optional */ }
    }

    private void LoadCategories()
    {
        try
        {
            var categories = DbHelper.ExecuteQuery("SELECT category_name FROM category_master WHERE active ORDER BY category_name");
            cboCategory.Items.Clear();
            foreach (DataRow r in categories.Rows) cboCategory.Items.Add(r["category_name"].ToString()!);
        }
        catch { /* Category list optional — Master > Category Master manages it */ }
    }

    private void LoadForEdit(int id)
    {
        var table = DbHelper.ExecuteQuery(@"
            SELECT im.*, b.brand_name, cm.category_name FROM item_master im
            LEFT JOIN brand_master b ON b.brand_id = im.brand_id
            LEFT JOIN category_master cm ON cm.category_id = im.category_id
            WHERE item_id=@id", new Dictionary<string, object?> { ["id"] = id });
        if (table.Rows.Count == 0) return;
        var row = table.Rows[0];

        txtItemName.Text = row["item_name"].ToString();
        txtModel.Text = row["model"]?.ToString();
        txtSideSize.Text = row["side_size"]?.ToString();
        txtBrand.Text = row["brand_name"]?.ToString();
        cboCategory.Text = row["category_name"]?.ToString();
        txtBarcode.Text = row["barcode"]?.ToString();
        numOpeningQty.Value = row["opening_qty"] is DBNull ? 0 : Convert.ToDecimal(row["opening_qty"]);
        numRate.Value = row["rate"] is DBNull ? 0 : Convert.ToDecimal(row["rate"]);
        numPurchasePrice.Value = row["purchase_price"] is DBNull ? 0 : Convert.ToDecimal(row["purchase_price"]);
        numReorderLevel.Value = row["min_stock"] is DBNull ? 0 : Convert.ToDecimal(row["min_stock"]);
        numTax.Value = row["tax_percent"] is DBNull ? 0 : Convert.ToDecimal(row["tax_percent"]);
        txtHsn.Text = row["hsn_code"]?.ToString();
        txtDescription.Text = row["description"]?.ToString();
        chkActive.Checked = row["active"] is bool b && b;
        if (row["uom_id"] != DBNull.Value) cboUom.SelectedValue = Convert.ToInt32(row["uom_id"]);
        string? itemType = row["item_type"]?.ToString();
        if (!string.IsNullOrEmpty(itemType) && cboItemType.Items.Contains(itemType))
            cboItemType.SelectedItem = itemType;
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
            // Resolve / create brand by name typed (simple upsert-by-name)
            int? brandId = null;
            if (!string.IsNullOrWhiteSpace(txtBrand.Text))
            {
                var existing = DbHelper.ExecuteScalar("SELECT brand_id FROM brand_master WHERE brand_name=@n",
                    new Dictionary<string, object?> { ["n"] = txtBrand.Text.Trim() });
                if (existing != null) brandId = Convert.ToInt32(existing);
                else
                {
                    brandId = Convert.ToInt32(DbHelper.ExecuteScalar(
                        "INSERT INTO brand_master (brand_name) VALUES (@n) RETURNING brand_id",
                        new Dictionary<string, object?> { ["n"] = txtBrand.Text.Trim() }));
                }
            }

            // Resolve / create category by name typed (same upsert-by-name pattern as Brand).
            // To manage the full list (description, active flag), use Master > Category Master.
            int? categoryId = null;
            if (!string.IsNullOrWhiteSpace(cboCategory.Text))
            {
                var existingCat = DbHelper.ExecuteScalar("SELECT category_id FROM category_master WHERE category_name=@n",
                    new Dictionary<string, object?> { ["n"] = cboCategory.Text.Trim() });
                if (existingCat != null) categoryId = Convert.ToInt32(existingCat);
                else
                {
                    categoryId = Convert.ToInt32(DbHelper.ExecuteScalar(
                        "INSERT INTO category_master (category_name) VALUES (@n) RETURNING category_id",
                        new Dictionary<string, object?> { ["n"] = cboCategory.Text.Trim() }));
                }
            }

            var pars = new Dictionary<string, object?>
            {
                ["name"] = txtItemName.Text.Trim(),
                ["model"] = txtModel.Text.Trim(),
                ["side"] = txtSideSize.Text.Trim(),
                ["brand"] = brandId,
                ["uom"] = cboUom.SelectedValue as int?,
                ["type"] = cboItemType.SelectedItem?.ToString() ?? "Stock Item",
                ["category"] = categoryId,
                ["barcode"] = txtBarcode.Text.Trim(),
                ["qty"] = numOpeningQty.Value,
                ["rate"] = numRate.Value,
                ["pprice"] = numPurchasePrice.Value,
                ["reorder"] = numReorderLevel.Value,
                ["tax"] = numTax.Value,
                ["hsn"] = txtHsn.Text.Trim(),
                ["desc"] = txtDescription.Text.Trim(),
                ["active"] = chkActive.Checked
            };

            if (editItemId == null)
            {
                SavedItemId = Convert.ToInt32(DbHelper.ExecuteScalar(@"
                    INSERT INTO item_master (item_name, model, side_size, brand_id, uom_id, item_type, category_id,
                        barcode, opening_qty, rate, purchase_price, min_stock, tax_percent, hsn_code, description, active)
                    VALUES (@name, @model, @side, @brand, @uom, @type, @category, @barcode, @qty, @rate, @pprice, @reorder, @tax, @hsn, @desc, @active)
                    RETURNING item_id", pars));
            }
            else
            {
                pars["id"] = editItemId;
                DbHelper.ExecuteNonQuery(@"
                    UPDATE item_master SET item_name=@name, model=@model, side_size=@side, brand_id=@brand,
                           uom_id=@uom, item_type=@type, category_id=@category, barcode=@barcode,
                           opening_qty=@qty, rate=@rate, purchase_price=@pprice, min_stock=@reorder,
                           tax_percent=@tax, hsn_code=@hsn, description=@desc, active=@active
                    WHERE item_id=@id", pars);
                SavedItemId = editItemId;
            }

            SeedOpeningStock();

            DialogResult = DialogResult.OK;
            Close();
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
    private void SeedOpeningStock()
    {
        if (numOpeningQty.Value == 0 || SavedItemId == 0) return;
        try
        {
            bool hasBalance = Convert.ToInt32(DbHelper.ExecuteScalar(
                "SELECT COUNT(*) FROM stock_balance WHERE item_id=@id", new() { ["id"] = SavedItemId })) > 0;
            if (hasBalance) return;

            DbHelper.ExecuteNonQuery(@"
                INSERT INTO stock_balance (item_id, warehouse_id, qty_on_hand) VALUES (@item, @wh, @qty)
                ON CONFLICT (item_id, warehouse_id) DO UPDATE SET qty_on_hand = stock_balance.qty_on_hand + @qty",
                new Dictionary<string, object?>
                {
                    ["item"] = SavedItemId,
                    ["wh"] = DbHelper.GetDefaultWarehouseId(),
                    ["qty"] = numOpeningQty.Value
                });
        }
        catch { /* item is saved even if the opening-stock seed fails */ }
    }

    private void ClearForm()
    {
        txtItemName.Clear(); txtModel.Clear(); txtSideSize.Clear(); txtBrand.Clear();
        cboCategory.Text = ""; txtBarcode.Clear();
        numOpeningQty.Value = 0; numRate.Value = 0; numPurchasePrice.Value = 0;
        numReorderLevel.Value = 0; numTax.Value = 0;
        txtHsn.Clear(); txtDescription.Clear(); chkActive.Checked = true;
        cboItemType.SelectedIndex = 0;
        if (cboUom.Items.Count > 0) cboUom.SelectedIndex = -1;
    }
}
