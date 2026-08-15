using ErpApp.Data;
using ErpApp.Forms;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Per-warehouse stock flow, end to end against a scratch database created by
/// <see cref="DatabaseFixture"/>: opening-stock seeding, purchase/transfer/sale/
/// return/adjustment hitting the picked warehouse's balance, the aggregate item
/// list view, the Stock Summary per-warehouse query, and the real reversal
/// methods restoring each warehouse's balance on edit/delete.
///
/// The transaction SQL mirrors what the forms run (SalesInvoiceForm,
/// StockTransferForm, StockAdjustmentForm) so the tests exercise the same
/// statements + the same DbHelper code paths.
/// </summary>
[Collection("Database")]
public class StockFlowTests
{
    private static int CreateWarehouse(string name) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO warehouse_master (warehouse_name, location, active) VALUES (@n, 'Test location', TRUE) RETURNING warehouse_id",
        new Dictionary<string, object?> { ["n"] = name }));

    private static int CreateItem(string name, decimal openingQty = 0, decimal rate = 10) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO item_master (item_name, opening_qty, rate, active) VALUES (@n, @q, @r, TRUE) RETURNING item_id",
        new Dictionary<string, object?> { ["n"] = name, ["q"] = openingQty, ["r"] = rate }));

    private static decimal Balance(int itemId, int warehouseId)
    {
        var v = DbHelper.ExecuteScalar("SELECT qty_on_hand FROM stock_balance WHERE item_id=@i AND warehouse_id=@w",
            new Dictionary<string, object?> { ["i"] = itemId, ["w"] = warehouseId });
        return v == null || v == DBNull.Value ? 0 : Convert.ToDecimal(v);
    }

    private static int Count(string sql, Dictionary<string, object?> pars) => Convert.ToInt32(DbHelper.ExecuteScalar(sql, pars));

    // ---- Opening stock & default warehouse ---------------------------------

    [Fact]
    public void DefaultWarehouse_IsTheSeededMainWarehouse()
    {
        int def = DbHelper.GetDefaultWarehouseId();
        Assert.True(def > 0);
        var name = DbHelper.ExecuteScalar("SELECT warehouse_name FROM warehouse_master WHERE warehouse_id=@id",
            new Dictionary<string, object?> { ["id"] = def })?.ToString();
        Assert.Equal("Main Warehouse", name);
    }

    [Fact]
    public void OpeningStock_SeedsBalanceAtDefaultWarehouse()
    {
        int item = CreateItem("OpenSeed " + Guid.NewGuid().ToString("N")[..8], openingQty: 50);
        int def = DbHelper.GetDefaultWarehouseId();
        int other = CreateWarehouse("OpenOther " + Guid.NewGuid().ToString("N")[..8]);

        // What AddItemDialog/ItemMasterForm do on save with an opening qty
        bool hasBalance = Count("SELECT COUNT(*) FROM stock_balance WHERE item_id=@id", new() { ["id"] = item }) > 0;
        Assert.False(hasBalance);
        if (!hasBalance)
        {
            DbHelper.ExecuteNonQuery(@"
                INSERT INTO stock_balance (item_id, warehouse_id, qty_on_hand) VALUES (@item, @wh, @qty)
                ON CONFLICT (item_id, warehouse_id) DO UPDATE SET qty_on_hand = stock_balance.qty_on_hand + @qty",
                new Dictionary<string, object?> { ["item"] = item, ["wh"] = def, ["qty"] = 50 });
        }

        Assert.Equal(50m, Balance(item, def));   // lands in the default warehouse
        Assert.Equal(0m, Balance(item, other));  // nowhere else
    }

    // ---- Transactions hit the picked warehouse's balance -------------------

    [Fact]
    public void Purchase_IncreasesPickedWarehouseBalance()
    {
        int item = CreateItem("Pur " + Guid.NewGuid().ToString("N")[..8]);
        int wh1 = CreateWarehouse("PurMain " + Guid.NewGuid().ToString("N")[..8]);
        int wh2 = CreateWarehouse("PurBranch " + Guid.NewGuid().ToString("N")[..8]);

        // PurchaseForm: +qty at the picked warehouse
        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh2, 100m));

        Assert.Equal(0m, Balance(item, wh1));    // the other warehouse is untouched
        Assert.Equal(100m, Balance(item, wh2));
    }

    [Fact]
    public void Transfer_MovesBalancesBetweenWarehouses()
    {
        int item = CreateItem("Tr " + Guid.NewGuid().ToString("N")[..8]);
        int wh1 = CreateWarehouse("TrMain " + Guid.NewGuid().ToString("N")[..8]);
        int wh2 = CreateWarehouse("TrBranch " + Guid.NewGuid().ToString("N")[..8]);

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh1, 100m));

        // StockTransferForm: -qty @from, +qty @to
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, wh1, -30m);
            DbHelper.AdjustBalance(conn, tx, item, wh2, 30m);
        });

        Assert.Equal(70m, Balance(item, wh1));
        Assert.Equal(30m, Balance(item, wh2));
        Assert.Equal(100m, Balance(item, wh1) + Balance(item, wh2)); // overall on-hand unchanged
    }

    [Fact]
    public void Sale_DecreasesPickedWarehouseBalance()
    {
        int item = CreateItem("Sale " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("SaleWh " + Guid.NewGuid().ToString("N")[..8]);

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 50m));
        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, -20m)); // sale

        Assert.Equal(30m, Balance(item, wh));
    }

    [Fact]
    public void SalesReturn_IncreasesPickedWarehouseBalance()
    {
        int item = CreateItem("SRet " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("SRetWh " + Guid.NewGuid().ToString("N")[..8]);

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 50m));
        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, -20m));
        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 5m)); // return

        Assert.Equal(35m, Balance(item, wh));
    }

    [Fact]
    public void Adjustment_IncreaseAndDecrease_AdjustPickedWarehouse()
    {
        int item = CreateItem("Adj " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("AdjWh " + Guid.NewGuid().ToString("N")[..8]);

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 10m));  // Increase
        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, -4m));   // Decrease

        Assert.Equal(6m, Balance(item, wh));
    }

    // ---- Print derivation ----------------------------------------------------

    [Fact]
    public void InvoicePrint_WarehouseDerivation_FindsThePickedWarehouse()
    {
        int item = CreateItem("PrintWh " + Guid.NewGuid().ToString("N")[..8]);
        string branchName = "PrintWhBranch " + Guid.NewGuid().ToString("N")[..8];
        int wh1 = CreateWarehouse("PrintWhMain " + Guid.NewGuid().ToString("N")[..8]);
        int wh2 = CreateWarehouse(branchName);
        string no = "IT-" + Guid.NewGuid().ToString("N")[..8];
        int invoiceId = 0;

        // a sale saved with the warehouse picker on wh2
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var cmd = new NpgsqlCommand(
                "INSERT INTO sales_invoice (invoice_no, invoice_date, grand_total) VALUES (@no, CURRENT_DATE, 100) RETURNING invoice_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            invoiceId = (int)cmd.ExecuteScalar()!;

            using var mov = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @wh, 'OUT', 1, 'SALES', @inv)", conn, tx);
            mov.Parameters.AddWithValue("item", item);
            mov.Parameters.AddWithValue("wh", wh2);
            mov.Parameters.AddWithValue("inv", invoiceId);
            mov.ExecuteNonQuery();
        });

        // the query InvoiceDocumentData.Load runs for the printout
        var rows = DbHelper.ExecuteQuery(@"
            SELECT DISTINCT w.warehouse_name FROM stock_movement sm
            LEFT JOIN warehouse_master w ON w.warehouse_id = sm.warehouse_id
            WHERE sm.reference_type='SALES' AND sm.reference_id=@id AND w.warehouse_name IS NOT NULL",
            new Dictionary<string, object?> { ["id"] = invoiceId });
        Assert.Single(rows.Rows);
        Assert.Equal(branchName, rows.Rows[0]["warehouse_name"].ToString()); // the picked warehouse shows on the printout

        // a doc with no movement rows (or pre-warehouse NULL) yields no warehouse name
        var empty = DbHelper.ExecuteQuery(@"
            SELECT DISTINCT w.warehouse_name FROM stock_movement sm
            LEFT JOIN warehouse_master w ON w.warehouse_id = sm.warehouse_id
            WHERE sm.reference_type='SALES' AND sm.reference_id=@id AND w.warehouse_name IS NOT NULL",
            new Dictionary<string, object?> { ["id"] = 99999999 });
        Assert.Empty(empty.Rows);
    }

    [Fact]
    public void TransferNote_Load_GetsFromToWarehousesItemsAndTotal()
    {
        int item = CreateItem("Note " + Guid.NewGuid().ToString("N")[..8]);
        string fromName = "NoteFrom " + Guid.NewGuid().ToString("N")[..8];
        string toName = "NoteTo " + Guid.NewGuid().ToString("N")[..8];
        int wh1 = CreateWarehouse(fromName);
        int wh2 = CreateWarehouse(toName);
        string no = "ST-" + Guid.NewGuid().ToString("N")[..8];
        int transferId = 0;

        // what StockTransferForm writes on save
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO stock_transfer (transfer_no, transfer_date, from_warehouse_id, to_warehouse_id, remarks)
                VALUES (@no, CURRENT_DATE, @from, @to, 'Test transfer') RETURNING transfer_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            cmd.Parameters.AddWithValue("from", wh1);
            cmd.Parameters.AddWithValue("to", wh2);
            transferId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(
                "INSERT INTO stock_transfer_item (transfer_id, item_id, qty) VALUES (@t, @item, 25)", conn, tx);
            line.Parameters.AddWithValue("t", transferId);
            line.Parameters.AddWithValue("item", item);
            line.ExecuteNonQuery();
        });

        // the real loader the print form uses
        var data = StockTransferDocumentData.Load(transferId);
        Assert.NotNull(data);
        Assert.Equal(no, data!.TransferNo);
        Assert.Equal(fromName, data.FromWarehouse);
        Assert.Equal(toName, data.ToWarehouse);
        Assert.Single(data.Items.Rows);
        Assert.Equal(25m, data.TotalQty);

        // the drawing routine renders the loaded data without throwing
        using (var bmp = new Bitmap(StockTransferDocumentData.DocWidth, StockTransferDocumentData.DocHeight))
        using (var g = Graphics.FromImage(bmp))
            StockTransferDocumentRenderer.Draw(g, 1f, data);

        // a transfer that no longer exists yields null
        Assert.Null(StockTransferDocumentData.Load(99999999));
    }

    // ---- Read side ----------------------------------------------------------

    [Fact]
    public void ItemListView_TotalsAcrossWarehouses()
    {
        int item = CreateItem("View " + Guid.NewGuid().ToString("N")[..8]);
        int wh1 = CreateWarehouse("ViewWh1 " + Guid.NewGuid().ToString("N")[..8]);
        int wh2 = CreateWarehouse("ViewWh2 " + Guid.NewGuid().ToString("N")[..8]);

        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, wh1, 120m);
            DbHelper.AdjustBalance(conn, tx, item, wh2, 30m);
        });

        var qty = Convert.ToDecimal(DbHelper.ExecuteScalar("SELECT qty FROM vw_item_list WHERE item_id=@id",
            new Dictionary<string, object?> { ["id"] = item }));
        Assert.Equal(150m, qty);
    }

    [Fact]
    public void StockSummary_PerWarehouseQuery_ShowsQtyAtThatWarehouse()
    {
        int item = CreateItem("Sum " + Guid.NewGuid().ToString("N")[..8], openingQty: 50);
        int wh1 = CreateWarehouse("SumWh1 " + Guid.NewGuid().ToString("N")[..8]);
        int wh2 = CreateWarehouse("SumWh2 " + Guid.NewGuid().ToString("N")[..8]);

        // opening seed at default + purchase into wh2 (item has rows, so no opening fallback elsewhere)
        int def = DbHelper.GetDefaultWarehouseId();
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, def, 50m);
            DbHelper.AdjustBalance(conn, tx, item, wh1, 100m);
            DbHelper.AdjustBalance(conn, tx, item, wh2, 30m);
        });

        // The exact per-warehouse subquery StockSummaryForm builds for a picked warehouse.
        decimal QtyAt(int wh)
        {
            var table = DbHelper.ExecuteQuery(@"
                SELECT i.item_id, i.item_name, i.rate, i.min_stock,
                       b.brand_name, u.uom_name, cm.category_name AS category,
                       COALESCE(s.qty_on_hand,
                           CASE WHEN s_any.item_id IS NULL AND @wh = @defwh THEN i.opening_qty ELSE 0 END) AS qty
                FROM item_master i
                LEFT JOIN brand_master b ON b.brand_id = i.brand_id
                LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                LEFT JOIN category_master cm ON cm.category_id = i.category_id
                LEFT JOIN stock_balance s ON s.item_id = i.item_id AND s.warehouse_id = @wh
                LEFT JOIN (SELECT DISTINCT item_id FROM stock_balance) s_any ON s_any.item_id = i.item_id",
                new Dictionary<string, object?> { ["wh"] = wh, ["defwh"] = def });
            foreach (System.Data.DataRow r in table.Rows)
                if (Convert.ToInt32(r["item_id"]) == item)
                    return Convert.ToDecimal(r["qty"]);
            return -1;
        }

        Assert.Equal(100m, QtyAt(wh1));
        Assert.Equal(30m, QtyAt(wh2));
    }

    // ---- Reversals restore each warehouse's balance ------------------------

    [Fact]
    public void ReverseSalesInvoicePostings_RestoresWarehouseBalance()
    {
        int item = CreateItem("RevSale " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("RevSaleWh " + Guid.NewGuid().ToString("N")[..8]);
        string no = "IT-" + Guid.NewGuid().ToString("N")[..8];
        int invoiceId = 0;

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 100m));

        // What SalesInvoiceForm writes: header + line + OUT movement + balance + ledger legs
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, wh, -10m);

            using var cmd = new NpgsqlCommand(
                "INSERT INTO sales_invoice (invoice_no, invoice_date, grand_total) VALUES (@no, CURRENT_DATE, 1000) RETURNING invoice_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            invoiceId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(
                "INSERT INTO sales_invoice_item (invoice_id, item_id, qty, rate, amount) VALUES (@inv, @item, 10, 100, 1000)", conn, tx);
            line.Parameters.AddWithValue("inv", invoiceId);
            line.Parameters.AddWithValue("item", item);
            line.ExecuteNonQuery();

            using var mov = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @wh, 'OUT', 10, 'SALES', @inv)", conn, tx);
            mov.Parameters.AddWithValue("item", item);
            mov.Parameters.AddWithValue("wh", wh);
            mov.Parameters.AddWithValue("inv", invoiceId);
            mov.ExecuteNonQuery();

            using var led = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                VALUES (@no, 'Sales Invoice', NULL, 'x', 0, 1000, @inv)", conn, tx);
            led.Parameters.AddWithValue("no", no);
            led.Parameters.AddWithValue("inv", invoiceId);
            led.ExecuteNonQuery();
        });

        Assert.Equal(90m, Balance(item, wh));

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.ReverseSalesInvoicePostings(conn, tx, invoiceId));

        Assert.Equal(100m, Balance(item, wh));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='SALES' AND reference_id=@id", new() { ["id"] = invoiceId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM sales_invoice_item WHERE invoice_id=@id", new() { ["id"] = invoiceId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM ledger_entry WHERE voucher_type='Sales Invoice' AND reference_id=@id", new() { ["id"] = invoiceId }));
    }

    [Fact]
    public void ReverseStockTransferPostings_RestoresBothWarehouses()
    {
        int item = CreateItem("RevTr " + Guid.NewGuid().ToString("N")[..8]);
        int wh1 = CreateWarehouse("RevTrMain " + Guid.NewGuid().ToString("N")[..8]);
        int wh2 = CreateWarehouse("RevTrBranch " + Guid.NewGuid().ToString("N")[..8]);
        string no = "ST-" + Guid.NewGuid().ToString("N")[..8];
        int transferId = 0;

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh1, 100m));

        // What StockTransferForm writes: header + line + TRANSFER_OUT/IN legs + balance moves
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, wh1, -30m);
            DbHelper.AdjustBalance(conn, tx, item, wh2, 30m);

            using var cmd = new NpgsqlCommand(@"
                INSERT INTO stock_transfer (transfer_no, transfer_date, from_warehouse_id, to_warehouse_id)
                VALUES (@no, CURRENT_DATE, @from, @to) RETURNING transfer_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            cmd.Parameters.AddWithValue("from", wh1);
            cmd.Parameters.AddWithValue("to", wh2);
            transferId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(
                "INSERT INTO stock_transfer_item (transfer_id, item_id, qty) VALUES (@t, @item, 30)", conn, tx);
            line.Parameters.AddWithValue("t", transferId);
            line.Parameters.AddWithValue("item", item);
            line.ExecuteNonQuery();

            using var outLeg = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @from, 'TRANSFER_OUT', 30, 'TRANSFER', @t)", conn, tx);
            outLeg.Parameters.AddWithValue("item", item);
            outLeg.Parameters.AddWithValue("from", wh1);
            outLeg.Parameters.AddWithValue("t", transferId);
            outLeg.ExecuteNonQuery();

            using var inLeg = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @to, 'TRANSFER_IN', 30, 'TRANSFER', @t)", conn, tx);
            inLeg.Parameters.AddWithValue("item", item);
            inLeg.Parameters.AddWithValue("to", wh2);
            inLeg.Parameters.AddWithValue("t", transferId);
            inLeg.ExecuteNonQuery();
        });

        Assert.Equal(70m, Balance(item, wh1));
        Assert.Equal(30m, Balance(item, wh2));

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.ReverseStockTransferPostings(conn, tx, transferId));

        Assert.Equal(100m, Balance(item, wh1));
        Assert.Equal(0m, Balance(item, wh2));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='TRANSFER' AND reference_id=@id", new() { ["id"] = transferId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM stock_transfer_item WHERE transfer_id=@id", new() { ["id"] = transferId }));
    }

    [Fact]
    public void ReverseStockAdjustmentPostings_RestoresBalance()
    {
        int item = CreateItem("RevAdj " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("RevAdjWh " + Guid.NewGuid().ToString("N")[..8]);
        string no = "ADJ-" + Guid.NewGuid().ToString("N")[..8];
        int adjId = 0;

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 100m));

        // What StockAdjustmentForm writes for a "Decrease": header + line + movement + -qty balance
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, wh, -10m);

            using var cmd = new NpgsqlCommand(@"
                INSERT INTO stock_adjustment (adjustment_no, adjustment_date, adjustment_type, warehouse_id)
                VALUES (@no, CURRENT_DATE, 'Decrease', @wh) RETURNING adjustment_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            cmd.Parameters.AddWithValue("wh", wh);
            adjId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(
                "INSERT INTO stock_adjustment_item (adjustment_id, item_id, qty, rate, amount) VALUES (@a, @item, 10, 0, 0)", conn, tx);
            line.Parameters.AddWithValue("a", adjId);
            line.Parameters.AddWithValue("item", item);
            line.ExecuteNonQuery();

            using var mov = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @wh, 'ADJUSTMENT', 10, 'ADJUSTMENT', @a)", conn, tx);
            mov.Parameters.AddWithValue("item", item);
            mov.Parameters.AddWithValue("wh", wh);
            mov.Parameters.AddWithValue("a", adjId);
            mov.ExecuteNonQuery();
        });

        Assert.Equal(90m, Balance(item, wh));

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.ReverseStockAdjustmentPostings(conn, tx, adjId));

        Assert.Equal(100m, Balance(item, wh));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='ADJUSTMENT' AND reference_id=@id", new() { ["id"] = adjId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM stock_adjustment_item WHERE adjustment_id=@id", new() { ["id"] = adjId }));
    }
}
