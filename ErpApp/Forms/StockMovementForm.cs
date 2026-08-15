using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches Inventory mockup form 4 "Stock Movement" — browsable movement log with In/Out/Net summary.</summary>
public class StockMovementForm : AppFormBase
{
    private readonly ComboBox cboItem = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboWarehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnClear = new() { Text = "Clear" };

    private readonly Label lblTotalIn = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label lblTotalOut = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label lblNetMovement = new() { Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

    private readonly DataGridView grid = new();

    public StockMovementForm()
    {
        Text = "Stock Movement";
        Width = 1050;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadItems();
        LoadWarehouses();
        RunReport();
    }

    private void BuildLayout()
    {
        var filterGroup = new GroupBox { Text = "Filters", Dock = DockStyle.Top, Height = 90 };
        var f = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Padding = new Padding(10) };
        for (int i = 0; i < 5; i++) f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        f.Controls.Add(new Label { Text = "Item", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        cboItem.Dock = DockStyle.Fill;
        f.Controls.Add(cboItem, 0, 1);

        f.Controls.Add(new Label { Text = "From Date", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        dtFrom.Dock = DockStyle.Fill;
        dtFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        f.Controls.Add(dtFrom, 1, 1);

        f.Controls.Add(new Label { Text = "To Date", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        dtTo.Dock = DockStyle.Fill;
        dtTo.Value = DateTime.Today;
        f.Controls.Add(dtTo, 2, 1);

        f.Controls.Add(new Label { Text = "Warehouse", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 3, 0);
        cboWarehouse.Dock = DockStyle.Fill;
        f.Controls.Add(cboWarehouse, 3, 1);

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        btnSearch.Click += (s, e) => RunReport();
        btnClear.Click += (s, e) => { cboItem.SelectedIndex = -1; cboWarehouse.SelectedIndex = 0; RunReport(); };
        btnFlow.Controls.Add(btnSearch);
        btnFlow.Controls.Add(btnClear);
        f.Controls.Add(btnFlow, 4, 1);

        filterGroup.Controls.Add(f);

        var summaryGroup = new GroupBox { Text = "Summary", Dock = DockStyle.Top, Height = 90 };
        var s = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        for (int i = 0; i < 3; i++) s.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));

        Panel Kpi(string caption, Label valueLabel)
        {
            var p = new Panel { Dock = DockStyle.Fill };
            var cap = new Label { Text = caption, Dock = DockStyle.Top, Height = 18, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gray };
            valueLabel.Dock = DockStyle.Fill;
            p.Controls.Add(valueLabel);
            p.Controls.Add(cap);
            return p;
        }

        s.Controls.Add(Kpi("Total In Qty", lblTotalIn), 0, 0);
        s.Controls.Add(Kpi("Total Out Qty", lblTotalOut), 1, 0);
        s.Controls.Add(Kpi("Net Movement", lblNetMovement), 2, 0);
        summaryGroup.Controls.Add(s);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        Controls.Add(grid);
        Controls.Add(summaryGroup);
        Controls.Add(filterGroup);
    }

    private void LoadItems()
    {
        try
        {
            var items = DbHelper.ExecuteQuery("SELECT item_id, item_name FROM item_master WHERE active ORDER BY item_name");
            var withAll = items.Clone();
            var blank = withAll.NewRow();
            blank["item_id"] = DBNull.Value;
            blank["item_name"] = "(All Items)";
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

    private void LoadWarehouses()
    {
        try
        {
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
            MessageBox.Show("Could not load warehouses: " + ex.Message);
        }
    }

    private void RunReport()
    {
        try
        {
            string sql = @"
                SELECT sm.movement_date AS ""Date"", sm.reference_type AS ""Ref Type"",
                       sm.reference_id AS ""Ref No"", i.item_name AS ""Item"",
                       COALESCE(w.warehouse_name, '') AS ""Warehouse"",
                       sm.movement_type AS ""Type"", sm.qty AS ""Qty"", sm.remarks AS ""Remarks""
                FROM stock_movement sm
                LEFT JOIN item_master i ON i.item_id = sm.item_id
                LEFT JOIN warehouse_master w ON w.warehouse_id = sm.warehouse_id
                WHERE sm.movement_date BETWEEN @from AND @to";
            var pars = new Dictionary<string, object?> { ["from"] = dtFrom.Value.Date, ["to"] = dtTo.Value.Date };

            if (cboItem.SelectedValue is int itemId)
            {
                sql += " AND sm.item_id=@item";
                pars["item"] = itemId;
            }
            if (cboWarehouse.SelectedValue is int whId)
            {
                sql += " AND sm.warehouse_id=@wh";
                pars["wh"] = whId;
            }
            sql += " ORDER BY sm.movement_date, sm.movement_id";

            var table = DbHelper.ExecuteQuery(sql, pars);
            grid.DataSource = table;

            // IN = purchases, sales returns, transfer-in legs, and positive adjustments
            decimal totalIn = 0, totalOut = 0;
            foreach (DataRow row in table.Rows)
            {
                string type = row["Type"].ToString()!;
                decimal qty = Convert.ToDecimal(row["Qty"]);
                bool isIn = type == "IN" || type == "TRANSFER_IN" || (type == "ADJUSTMENT" && qty >= 0);
                if (isIn) totalIn += Math.Abs(qty);
                else totalOut += Math.Abs(qty);
            }
            lblTotalIn.Text = totalIn.ToString("N2");
            lblTotalOut.Text = totalOut.ToString("N2");
            lblNetMovement.Text = (totalIn - totalOut).ToString("N2");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }
}
