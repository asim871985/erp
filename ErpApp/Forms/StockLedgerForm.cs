using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Matches Inventory mockup form 3 "Stock Ledger" — per-item movement history with a
/// running balance, built from the <c>stock_movement</c> log. This is item-based, unlike
/// the account-based Ledger form under Accounting.
/// </summary>
public class StockLedgerForm : AppFormBase
{
    private readonly ComboBox cboItem = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboWarehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly Button btnSearch = new() { Text = "Search" };
    private readonly Button btnClear = new() { Text = "Clear" };

    private readonly Label lblOpeningQty = new() { TextAlign = ContentAlignment.MiddleRight };
    private readonly Label lblOpeningValue = new() { TextAlign = ContentAlignment.MiddleRight };

    private readonly DataGridView grid = new();
    private readonly Label lblTotalIn = new() { Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    private readonly Label lblTotalOut = new() { Font = new Font("Segoe UI", 9, FontStyle.Bold) };

    public StockLedgerForm()
    {
        Text = "Stock Ledger";
        Width = 1000;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadItems();
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
        btnClear.Click += (s, e) => { cboItem.SelectedIndex = -1; cboWarehouse.SelectedIndex = 0; grid.DataSource = null; lblOpeningQty.Text = ""; lblOpeningValue.Text = ""; lblTotalIn.Text = ""; lblTotalOut.Text = ""; };
        btnFlow.Controls.Add(btnSearch);
        btnFlow.Controls.Add(btnClear);
        f.Controls.Add(btnFlow, 4, 1);

        filterGroup.Controls.Add(f);

        var openingGroup = new GroupBox { Text = "Opening Balance", Dock = DockStyle.Top, Height = 70 };
        var o = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        o.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        o.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        o.Controls.Add(new Label { Text = "Opening Qty", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        lblOpeningQty.Dock = DockStyle.Fill;
        o.Controls.Add(lblOpeningQty, 1, 0);
        o.Controls.Add(new Label { Text = "Value", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        lblOpeningValue.Dock = DockStyle.Fill;
        o.Controls.Add(lblOpeningValue, 1, 1);
        openingGroup.Controls.Add(o);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bottomPanel.Controls.Add(lblTotalOut);
        bottomPanel.Controls.Add(lblTotalIn);

        Controls.Add(grid);
        Controls.Add(bottomPanel);
        Controls.Add(openingGroup);
        Controls.Add(filterGroup);
    }    private void LoadItems()
    {
        try
        {
            var items = DbHelper.ExecuteQuery("SELECT item_id, item_name FROM item_master WHERE active ORDER BY item_name");
            cboItem.DisplayMember = "item_name";
            cboItem.ValueMember = "item_id";
            cboItem.DataSource = items;

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

            cboItem.SelectedIndex = -1;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load items: " + ex.Message);
        }
    }

    private void RunReport()
    {
        if (cboItem.SelectedValue is not int itemId) { MessageBox.Show("Select an item."); return; }

        try
        {
            var itemInfo = DbHelper.ExecuteQuery("SELECT rate, opening_qty FROM item_master WHERE item_id=@id", new() { ["id"] = itemId });
            decimal rate = itemInfo.Rows.Count > 0 ? Convert.ToDecimal(itemInfo.Rows[0]["rate"]) : 0;

            int? whId = cboWarehouse.SelectedValue is int w ? w : (int?)null;
            string whFilter = whId == null ? "" : " AND warehouse_id=@wh";
            var pars = new Dictionary<string, object?> { ["id"] = itemId, ["from"] = dtFrom.Value.Date };
            if (whId != null) pars["wh"] = whId;

            // Opening balance = movements strictly before the From Date, at this warehouse
            // (or all warehouses when "All" is picked). The item's opening_qty only counts
            // when viewing everything, or when the picked warehouse IS the default one —
            // that's where opening stock is seeded.
            var openingResult = DbHelper.ExecuteQuery(@"
                SELECT COALESCE(SUM(CASE WHEN movement_type IN ('IN','TRANSFER_IN') OR (movement_type='ADJUSTMENT' AND qty >= 0) THEN qty ELSE 0 END)
                       - SUM(CASE WHEN movement_type IN ('OUT','TRANSFER_OUT') THEN qty ELSE 0 END), 0) AS net
                FROM stock_movement WHERE item_id=@id AND movement_date < @from" + whFilter, pars);

            decimal openingQty = 0;
            if (whId == null || whId == DbHelper.GetDefaultWarehouseId())
                openingQty = itemInfo.Rows.Count > 0 ? Convert.ToDecimal(itemInfo.Rows[0]["opening_qty"]) : 0;
            if (openingResult.Rows.Count > 0 && openingResult.Rows[0]["net"] != DBNull.Value)
                openingQty += Convert.ToDecimal(openingResult.Rows[0]["net"]);

            lblOpeningQty.Text = openingQty.ToString("N2");
            lblOpeningValue.Text = (openingQty * rate).ToString("N2");

            pars["to"] = dtTo.Value.Date;
            var movements = DbHelper.ExecuteQuery(@"
                SELECT sm.movement_date, sm.movement_type, sm.reference_type, sm.reference_id, sm.qty, sm.remarks,
                       COALESCE(w.warehouse_name, '') AS warehouse_name
                FROM stock_movement sm
                LEFT JOIN warehouse_master w ON w.warehouse_id = sm.warehouse_id
                WHERE sm.item_id=@id AND sm.movement_date BETWEEN @from AND @to" + whFilter + @"
                ORDER BY sm.movement_date, sm.movement_id", pars);

            var display = new DataTable();
            display.Columns.Add("S.No", typeof(int));
            display.Columns.Add("Date", typeof(string));
            display.Columns.Add("Ref Type", typeof(string));
            display.Columns.Add("Ref No.", typeof(string));
            display.Columns.Add("Warehouse", typeof(string));
            display.Columns.Add("IN Qty", typeof(string));
            display.Columns.Add("OUT Qty", typeof(string));
            display.Columns.Add("Balance Qty", typeof(string));
            display.Columns.Add("Value", typeof(string));

            decimal balance = openingQty;
            decimal totalIn = 0, totalOut = 0;
            int sno = 1;
            foreach (DataRow r in movements.Rows)
            {
                string type = r["movement_type"].ToString()!;
                decimal qty = Convert.ToDecimal(r["qty"]);
                bool isIn = type == "IN" || type == "TRANSFER_IN" || (type == "ADJUSTMENT" && qty >= 0);
                decimal inQty = isIn ? Math.Abs(qty) : 0;
                decimal outQty = !isIn ? Math.Abs(qty) : 0;
                balance += inQty - outQty;
                totalIn += inQty;
                totalOut += outQty;

                display.Rows.Add(
                    sno++,
                    Convert.ToDateTime(r["movement_date"]).ToString("dd/MM/yyyy"),
                    r["reference_type"]?.ToString() ?? type,
                    r["reference_id"]?.ToString() ?? "-",
                    r["warehouse_name"]?.ToString() ?? "",
                    inQty > 0 ? inQty.ToString("N2") : "",
                    outQty > 0 ? outQty.ToString("N2") : "",
                    balance.ToString("N2"),
                    (balance * rate).ToString("N2"));
            }

            grid.DataSource = display;
            lblTotalIn.Text = "Total IN: " + totalIn.ToString("N2");
            lblTotalOut.Text = "Total OUT: " + totalOut.ToString("N2");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not run report: " + ex.Message);
        }
    }
}
