using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches Inventory mockup form 2 "Stock Summary" — filters + KPI panel + grid.</summary>
public class StockSummaryForm : AppFormBase
{
    private readonly ComboBox cboBrand = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboUom = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboWarehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox chkLowStockOnly = new() { Text = "Low Stock Only" };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnClear = new() { Text = "Clear" };

    private readonly Label lblTotalItems = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label lblTotalQty = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label lblTotalValue = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label lblLowStockItems = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Firebrick };

    private readonly DataGridView grid = new();

    public StockSummaryForm()
    {
        Text = "Stock Summary";
        Width = 1000;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadFilters();
        RunReport();
    }

    private void BuildLayout()
    {
        var filterGroup = new GroupBox { Text = "Filters", Dock = DockStyle.Top, Height = 90 };
        var f = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, Padding = new Padding(10) };
        for (int i = 0; i < 6; i++) f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));

        f.Controls.Add(new Label { Text = "Brand", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        cboBrand.Dock = DockStyle.Fill;
        f.Controls.Add(cboBrand, 0, 1);

        f.Controls.Add(new Label { Text = "UOM", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        cboUom.Dock = DockStyle.Fill;
        f.Controls.Add(cboUom, 1, 1);

        f.Controls.Add(new Label { Text = "Category", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        cboCategory.Dock = DockStyle.Fill;
        f.Controls.Add(cboCategory, 2, 1);

        f.Controls.Add(new Label { Text = "Warehouse", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 3, 0);
        cboWarehouse.Dock = DockStyle.Fill;
        f.Controls.Add(cboWarehouse, 3, 1);

        chkLowStockOnly.Dock = DockStyle.Fill;
        f.Controls.Add(chkLowStockOnly, 4, 1);

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        btnSearch.Click += (s, e) => RunReport();
        btnClear.Click += (s, e) => { cboBrand.SelectedIndex = 0; cboUom.SelectedIndex = 0; cboCategory.SelectedIndex = 0; cboWarehouse.SelectedIndex = 0; chkLowStockOnly.Checked = false; RunReport(); };
        btnFlow.Controls.Add(btnSearch);
        btnFlow.Controls.Add(btnClear);
        f.Controls.Add(btnFlow, 5, 1);

        filterGroup.Controls.Add(f);

        var summaryGroup = new GroupBox { Text = "Summary", Dock = DockStyle.Top, Height = 90 };
        var s = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        for (int i = 0; i < 4; i++) s.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        Panel Kpi(string caption, Label valueLabel)
        {
            var p = new Panel { Dock = DockStyle.Fill };
            var cap = new Label { Text = caption, Dock = DockStyle.Top, Height = 18, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gray };
            valueLabel.Dock = DockStyle.Fill;
            p.Controls.Add(valueLabel);
            p.Controls.Add(cap);
            return p;
        }

        s.Controls.Add(Kpi("Total Items", lblTotalItems), 0, 0);
        s.Controls.Add(Kpi("Total Stock Qty", lblTotalQty), 1, 0);
        s.Controls.Add(Kpi("Total Stock Value", lblTotalValue), 2, 0);
        s.Controls.Add(Kpi("Low Stock Items", lblLowStockItems), 3, 0);
        summaryGroup.Controls.Add(s);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        Controls.Add(grid);
        Controls.Add(summaryGroup);
        Controls.Add(filterGroup);
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

            var cats = DbHelper.ExecuteQuery("SELECT category_name FROM category_master WHERE active ORDER BY category_name");
            cboCategory.Items.Add("All");
            foreach (DataRow r in cats.Rows) cboCategory.Items.Add(r["category_name"].ToString()!);
            cboCategory.SelectedIndex = 0;

            // (All Warehouses) row carries a NULL value id, so SelectedValue is null when "All" is picked
            var whs = DbHelper.ExecuteQuery("SELECT warehouse_id, warehouse_name FROM warehouse_master WHERE active ORDER BY warehouse_name");
            var withAll = whs.Clone();
            var blank = withAll.NewRow();
            blank["warehouse_id"] = DBNull.Value;
            blank["warehouse_name"] = "(All Warehouses)";
            withAll.Rows.Add(blank);
            foreach (DataRow r in whs.Rows) withAll.ImportRow(r);
            cboWarehouse.DisplayMember = "warehouse_name";
            cboWarehouse.ValueMember = "warehouse_id";
            cboWarehouse.DataSource = withAll;
            cboWarehouse.SelectedIndex = 0;
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
            // Shared WHERE clause + parameters for both the grid and the KPI summary.
            var pars = new Dictionary<string, object?>();
            var where = new List<string>();
            if (cboBrand.SelectedItem?.ToString() is string b && b != "All") { where.Add("brand_name=@b"); pars["b"] = b; }
            if (cboUom.SelectedItem?.ToString() is string u && u != "All") { where.Add("uom_name=@u"); pars["u"] = u; }
            if (cboCategory.SelectedItem?.ToString() is string c && c != "All") { where.Add("category=@c"); pars["c"] = c; }
            if (chkLowStockOnly.Checked) where.Add("qty < min_stock");
            string whereSql = where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where);

            int? whId = cboWarehouse.SelectedValue is int w ? w : (int?)null;

            string fromClause;
            if (whId == null)
            {
                // All warehouses: vw_item_list already totals each item across warehouses.
                fromClause = "FROM vw_item_list";
            }
            else
            {
                // One warehouse: per-item qty at that warehouse. Items with no balance
                // rows anywhere fall back to opening_qty — but only at the default
                // warehouse, since that's where opening stock is seeded.
                pars["wh"] = whId;
                pars["defwh"] = DbHelper.GetDefaultWarehouseId();
                fromClause = @"FROM (
                    SELECT i.item_id, i.item_name, i.rate, i.min_stock,
                           b.brand_name, u.uom_name, cm.category_name AS category,
                           COALESCE(s.qty_on_hand,
                               CASE WHEN s_any.item_id IS NULL AND @wh = @defwh THEN i.opening_qty ELSE 0 END) AS qty
                    FROM item_master i
                    LEFT JOIN brand_master b ON b.brand_id = i.brand_id
                    LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                    LEFT JOIN category_master cm ON cm.category_id = i.category_id
                    LEFT JOIN stock_balance s ON s.item_id = i.item_id AND s.warehouse_id = @wh
                    LEFT JOIN (SELECT DISTINCT item_id FROM stock_balance) s_any ON s_any.item_id = i.item_id
                ) x";
            }

            string sql = whId == null
                ? "SELECT item_id AS \"ID\", item_name AS \"Item Name\", brand_name AS \"Brand\",\n" +
                  "       uom_name AS \"UOM\", qty AS \"Stock Qty\", amount AS \"Stock Value\"\n" +
                  fromClause + whereSql + " ORDER BY item_name"
                : "SELECT item_id AS \"ID\", item_name AS \"Item Name\", brand_name AS \"Brand\",\n" +
                  "       uom_name AS \"UOM\", qty AS \"Stock Qty\", qty * rate AS \"Stock Value\"\n" +
                  fromClause + whereSql + " ORDER BY item_name";

            var table = DbHelper.ExecuteQuery(sql, pars);
            grid.DataSource = table;
            if (grid.Columns.Contains("ID")) grid.Columns["ID"]!.Visible = false;

            string summarySql = whId == null
                ? "SELECT COUNT(*) AS total_items, COALESCE(SUM(qty),0) AS total_qty,\n" +
                  "       COALESCE(SUM(amount),0) AS total_value,\n" +
                  "       COUNT(*) FILTER (WHERE qty < min_stock) AS low_stock_items\n" +
                  fromClause + whereSql
                : "SELECT COUNT(*) AS total_items, COALESCE(SUM(qty),0) AS total_qty,\n" +
                  "       COALESCE(SUM(qty * rate),0) AS total_value,\n" +
                  "       COUNT(*) FILTER (WHERE qty < min_stock) AS low_stock_items\n" +
                  fromClause + whereSql;

            var sumTable = DbHelper.ExecuteQuery(summarySql, pars);
            var row = sumTable.Rows[0];
            lblTotalItems.Text = row["total_items"].ToString();
            lblTotalQty.Text = Convert.ToDecimal(row["total_qty"]).ToString("N2");
            lblTotalValue.Text = Convert.ToDecimal(row["total_value"]).ToString("N2");
            lblLowStockItems.Text = row["low_stock_items"].ToString();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }
}
