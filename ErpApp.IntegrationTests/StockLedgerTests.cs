using System.Data;
using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Stock Ledger report integration tests against the scratch database. The
/// ledger's queries are all item-scoped (filtered by item_id, optionally by
/// warehouse), so the tests are immune to sibling tests sharing the collection's
/// database.
///
/// Replicates exactly what StockLedgerForm.RunReport does: the warehouse-aware
/// opening balance (movements before the From date, plus the item's opening_qty
/// only when viewing all warehouses or the default one), the movement grid with
/// IN/OUT classification (IN, TRANSFER_IN and non-negative ADJUSTMENT are
/// inbound; OUT and TRANSFER_OUT outbound), the running balance, and the
/// Total IN / Total OUT footers.
/// </summary>
[Collection("Database")]
public class StockLedgerTests
{
    private static int CreateWarehouse(string name) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO warehouse_master (warehouse_name, location, active) VALUES (@n, 'Test location', TRUE) RETURNING warehouse_id",
        new Dictionary<string, object?> { ["n"] = name }));

    private static int CreateItem(string name, decimal openingQty = 0, decimal rate = 10) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO item_master (item_name, opening_qty, rate, active) VALUES (@n, @q, @r, TRUE) RETURNING item_id",
        new Dictionary<string, object?> { ["n"] = name, ["q"] = openingQty, ["r"] = rate }));

    private static void InsertMovement(int item, int warehouse, string type, decimal qty, string refType, DateTime date)
    {
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id, movement_date)
                VALUES (@item, @wh, @type, @qty, @ref, @id, @dt)", conn, tx);
            cmd.Parameters.AddWithValue("item", item);
            cmd.Parameters.AddWithValue("wh", warehouse);
            cmd.Parameters.AddWithValue("type", type);
            cmd.Parameters.AddWithValue("qty", qty);
            cmd.Parameters.AddWithValue("ref", refType);
            cmd.Parameters.AddWithValue("id", 1);
            cmd.Parameters.AddWithValue("dt", date);
            cmd.ExecuteNonQuery();
        });
    }

    /// <summary>
    /// Runs the exact StockLedgerForm.RunReport queries + classification and
    /// returns the opening balance, totals, running balance, and each movement's
    /// (type, qty, isIn) classification.
    /// </summary>
    private static (decimal Opening, decimal TotalIn, decimal TotalOut, decimal Final, List<(string Type, decimal Qty, bool IsIn)> Rows)
        RunLedger(int itemId, DateTime from, DateTime to, int? whId)
    {
        decimal openingQty = Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT opening_qty FROM item_master WHERE item_id=@id", new Dictionary<string, object?> { ["id"] = itemId }));

        string whFilter = whId == null ? "" : " AND warehouse_id=@wh";
        var pars = new Dictionary<string, object?> { ["id"] = itemId, ["from"] = from };
        if (whId != null) pars["wh"] = whId;

        decimal opening = 0;
        if (whId == null || whId == DbHelper.GetDefaultWarehouseId())
            opening = openingQty; // opening stock is seeded at the default warehouse
        var net = DbHelper.ExecuteScalar(@"
            SELECT COALESCE(SUM(CASE WHEN movement_type IN ('IN','TRANSFER_IN') OR (movement_type='ADJUSTMENT' AND qty >= 0) THEN qty ELSE 0 END)
                   - SUM(CASE WHEN movement_type IN ('OUT','TRANSFER_OUT') THEN qty ELSE 0 END), 0) AS net
            FROM stock_movement WHERE item_id=@id AND movement_date < @from" + whFilter, pars);
        if (net != null && net != DBNull.Value)
            opening += Convert.ToDecimal(net);

        pars["to"] = to;
        var movements = DbHelper.ExecuteQuery(@"
            SELECT sm.movement_type, sm.qty
            FROM stock_movement sm
            WHERE sm.item_id=@id AND sm.movement_date BETWEEN @from AND @to" + whFilter + @"
            ORDER BY sm.movement_date, sm.movement_id", pars);

        decimal balance = opening, totalIn = 0, totalOut = 0;
        var rows = new List<(string, decimal, bool)>();
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
            rows.Add((type, qty, isIn));
        }

        return (opening, totalIn, totalOut, balance, rows);
    }

    [Fact]
    public void StockLedger_AllWarehouses_ClassifiesTransferLegsCorrectly()
    {
        int item = CreateItem("LedgerAll " + Guid.NewGuid().ToString("N")[..8], openingQty: 50, rate: 10);
        int main = DbHelper.GetDefaultWarehouseId();
        int branch = CreateWarehouse("LedgerAllBranch " + Guid.NewGuid().ToString("N")[..8]);
        DateTime from = DateTime.Today.AddDays(-30), to = DateTime.Today;

        // the classic flow: purchase 100 → transfer 30 to branch → sale 20 from branch
        InsertMovement(item, main, "IN", 100, "PURCHASE", DateTime.Today);
        InsertMovement(item, main, "TRANSFER_OUT", 30, "TRANSFER", DateTime.Today);
        InsertMovement(item, branch, "TRANSFER_IN", 30, "TRANSFER", DateTime.Today);
        InsertMovement(item, branch, "OUT", 20, "SALES", DateTime.Today);

        var (opening, totalIn, totalOut, final, rows) = RunLedger(item, from, to, whId: null);

        Assert.Equal(50m, opening);        // opening_qty + no movements before From
        Assert.Equal(130m, totalIn);       // 100 purchase + 30 transfer-in
        Assert.Equal(50m, totalOut);       // 30 transfer-out + 20 sale
        Assert.Equal(130m, final);         // 50 + 130 − 50

        // the TRANSFER_IN leg is classified IN (not OUT) — the fixed classification
        var transferIn = rows.Single(r => r.Type == "TRANSFER_IN");
        Assert.True(transferIn.IsIn);
        Assert.Equal(2, rows.Count(r => r.IsIn));   // PURCHASE + TRANSFER_IN
        Assert.Equal(2, rows.Count(r => !r.IsIn));  // TRANSFER_OUT + SALES
    }

    [Fact]
    public void StockLedger_PickedWarehouse_ShowsOnlyThatWarehousesMovements()
    {
        int item = CreateItem("LedgerWh " + Guid.NewGuid().ToString("N")[..8], openingQty: 50, rate: 10);
        int main = DbHelper.GetDefaultWarehouseId();
        int branch = CreateWarehouse("LedgerWhBranch " + Guid.NewGuid().ToString("N")[..8]);
        DateTime from = DateTime.Today.AddDays(-30), to = DateTime.Today;

        InsertMovement(item, main, "IN", 100, "PURCHASE", DateTime.Today);
        InsertMovement(item, main, "TRANSFER_OUT", 30, "TRANSFER", DateTime.Today);
        InsertMovement(item, branch, "TRANSFER_IN", 30, "TRANSFER", DateTime.Today);
        InsertMovement(item, branch, "OUT", 20, "SALES", DateTime.Today);

        // Branch view: opening 0 (opening_qty lives at the default warehouse), in 30, out 20
        var (branchOpening, branchIn, branchOut, branchFinal, branchRows) = RunLedger(item, from, to, branch);
        Assert.Equal(0m, branchOpening);
        Assert.Equal(30m, branchIn);
        Assert.Equal(20m, branchOut);
        Assert.Equal(10m, branchFinal);
        Assert.All(branchRows, r => Assert.True(r.IsIn || r.Type == "OUT")); // only this warehouse's legs

        // Main (default) view: opening 50, in 100, out 30
        var (mainOpening, mainIn, mainOut, mainFinal, _) = RunLedger(item, from, to, main);
        Assert.Equal(50m, mainOpening);
        Assert.Equal(100m, mainIn);
        Assert.Equal(30m, mainOut);
        Assert.Equal(120m, mainFinal);
    }

    [Fact]
    public void StockLedger_OpeningIncludesMovementsBeforeTheFromDate()
    {
        int item = CreateItem("LedgerOpen " + Guid.NewGuid().ToString("N")[..8], openingQty: 0, rate: 10);
        int main = DbHelper.GetDefaultWarehouseId();
        DateTime from = DateTime.Today.AddDays(-30), to = DateTime.Today;

        // 25 arrived before the period (becomes opening), 10 inside the period
        InsertMovement(item, main, "IN", 25, "PURCHASE", from.AddDays(-5));
        InsertMovement(item, main, "IN", 10, "PURCHASE", DateTime.Today);

        var (opening, totalIn, totalOut, final, _) = RunLedger(item, from, to, whId: null);

        Assert.Equal(25m, opening);   // the pre-period movement is folded into opening
        Assert.Equal(10m, totalIn);   // only in-period movements count as IN
        Assert.Equal(0m, totalOut);
        Assert.Equal(35m, final);     // 25 + 10
    }
}
