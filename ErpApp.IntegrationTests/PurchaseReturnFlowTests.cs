using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Purchase / Sales Return / Purchase Return flows against the scratch
/// database. Each test mirrors the exact statements the forms run
/// (PurchaseForm, SalesReturnForm, PurchaseReturnForm): header + line items +
/// a per-warehouse stock movement + the balance adjustment, plus the two
/// balanced ledger legs (party account from the customer/supplier master,
/// Purchases 5000 / Sales 4000 on the other side). Reversal tests drive the
/// real DbHelper reversal methods and assert each warehouse's balance is
/// restored exactly and all posting rows are cleaned up.
///
/// All assertions are per-document (filtered by voucher no / reference id), so
/// they are immune to sibling tests sharing the collection's database.
/// </summary>
[Collection("Database")]
public class PurchaseReturnFlowTests
{
    private static int AccountIdByCode(string code) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "SELECT account_id FROM chart_of_accounts WHERE account_code=@c",
        new Dictionary<string, object?> { ["c"] = code }));

    private static int CreateWarehouse(string name) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO warehouse_master (warehouse_name, location, active) VALUES (@n, 'Test location', TRUE) RETURNING warehouse_id",
        new Dictionary<string, object?> { ["n"] = name }));

    private static int CreateItem(string name, decimal rate = 10) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO item_master (item_name, rate, active) VALUES (@n, @r, TRUE) RETURNING item_id",
        new Dictionary<string, object?> { ["n"] = name, ["r"] = rate }));

    private static int CreateSupplier(string name)
    {
        // like the Account Master + Supplier forms: an AP account, then a supplier tied to it
        int acc = Convert.ToInt32(DbHelper.ExecuteScalar(
            "INSERT INTO chart_of_accounts (account_code, account_name, account_type, balance_type) " +
            "VALUES (@code, @name, 'LIABILITY', 'Cr') RETURNING account_id",
            new Dictionary<string, object?> { ["code"] = "AP" + Guid.NewGuid().ToString("N")[..8], ["name"] = name + " A/C" }));
        return Convert.ToInt32(DbHelper.ExecuteScalar(
            "INSERT INTO supplier_master (supplier_name, account_id, address) VALUES (@n, @acc, 'Test address') RETURNING supplier_id",
            new Dictionary<string, object?> { ["n"] = name, ["acc"] = acc }));
    }

    private static int SeededCustomerId() => Convert.ToInt32(DbHelper.ExecuteScalar(
        "SELECT customer_id FROM customer_master WHERE customer_name='Walk In Customer'"));

    private static decimal Balance(int itemId, int warehouseId)
    {
        var v = DbHelper.ExecuteScalar("SELECT qty_on_hand FROM stock_balance WHERE item_id=@i AND warehouse_id=@w",
            new Dictionary<string, object?> { ["i"] = itemId, ["w"] = warehouseId });
        return v == null || v == DBNull.Value ? 0 : Convert.ToDecimal(v);
    }

    private static int Count(string sql, Dictionary<string, object?> pars) => Convert.ToInt32(DbHelper.ExecuteScalar(sql, pars));

    private static decimal SumOf(string column, string voucherNo) => Convert.ToDecimal(DbHelper.ExecuteScalar(
        $"SELECT COALESCE(SUM({column}),0) FROM ledger_entry WHERE voucher_no=@no",
        new Dictionary<string, object?> { ["no"] = voucherNo }));

    private static string NewNo(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];

    // ---- Purchase: stock IN + supplier credit / Purchases debit --------------

    [Fact]
    public void Purchase_SaveFlow_AddsStockAtPickedWarehouse_AndPostsLedger()
    {
        int item = CreateItem("PurFlow " + Guid.NewGuid().ToString("N")[..8]);
        int wh1 = CreateWarehouse("PurFlowMain " + Guid.NewGuid().ToString("N")[..8]);
        int wh2 = CreateWarehouse("PurFlowBranch " + Guid.NewGuid().ToString("N")[..8]);
        int supplier = CreateSupplier("PurFlowSup " + Guid.NewGuid().ToString("N")[..8]);
        string no = NewNo("PUR");
        decimal grand = 1200m;
        int purchaseId = 0;

        // what PurchaseForm BtnSave writes (picked warehouse = wh2)
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO purchase_bill (bill_no, bill_date, supplier_id, ref_no, credit_days, due_date, remarks, sub_total, discount, tax, grand_total)
                VALUES (@no, CURRENT_DATE, @sup, '', 0, CURRENT_DATE, '', @grand, 0, 0, @grand) RETURNING purchase_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            cmd.Parameters.AddWithValue("sup", supplier);
            cmd.Parameters.AddWithValue("grand", grand);
            purchaseId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(@"
                INSERT INTO purchase_bill_item (purchase_id, item_id, qty, rate, disc_percent, amount)
                VALUES (@p, @item, 120, 10, 0, @grand)", conn, tx);
            line.Parameters.AddWithValue("p", purchaseId);
            line.Parameters.AddWithValue("item", item);
            line.Parameters.AddWithValue("grand", grand);
            line.ExecuteNonQuery();

            using var mov = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @wh, 'IN', 120, 'PURCHASE', @p)", conn, tx);
            mov.Parameters.AddWithValue("item", item);
            mov.Parameters.AddWithValue("wh", wh2);
            mov.Parameters.AddWithValue("p", purchaseId);
            mov.ExecuteNonQuery();

            DbHelper.AdjustBalance(conn, tx, item, wh2, 120m);

            // ledger: credit the supplier's account (payable up)…
            using var supLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                SELECT @no, 'Purchase Bill', account_id, 'By Purchase Bill', 0, @grand, @p
                FROM supplier_master WHERE supplier_id=@sup", conn, tx);
            supLeg.Parameters.AddWithValue("no", no);
            supLeg.Parameters.AddWithValue("grand", grand);
            supLeg.Parameters.AddWithValue("p", purchaseId);
            supLeg.Parameters.AddWithValue("sup", supplier);
            supLeg.ExecuteNonQuery();

            // …and debit Purchases (5000)
            using var expLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                VALUES (@no, 'Purchase Bill', @acc, 'To Purchase Bill', @grand, 0, @p)", conn, tx);
            expLeg.Parameters.AddWithValue("no", no);
            expLeg.Parameters.AddWithValue("acc", AccountIdByCode("5000"));
            expLeg.Parameters.AddWithValue("grand", grand);
            expLeg.Parameters.AddWithValue("p", purchaseId);
            expLeg.ExecuteNonQuery();
        });

        // stock landed at the picked warehouse only
        Assert.Equal(120m, Balance(item, wh2));
        Assert.Equal(0m, Balance(item, wh1));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='PURCHASE' AND reference_id=@id", new() { ["id"] = purchaseId }));

        // ledger legs are balanced and hit the right accounts
        Assert.Equal(grand, SumOf("debit", no));
        Assert.Equal(grand, SumOf("credit", no));
        int supAcc = Convert.ToInt32(DbHelper.ExecuteScalar(
            "SELECT account_id FROM supplier_master WHERE supplier_id=@id", new Dictionary<string, object?> { ["id"] = supplier }));
        Assert.Equal(grand, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(credit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = supAcc })));
        Assert.Equal(grand, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(debit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = AccountIdByCode("5000") })));
    }

    [Fact]
    public void ReversePurchaseBillPostings_RestoresBalanceAndRemovesPostings()
    {
        int item = CreateItem("PurRev " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("PurRevWh " + Guid.NewGuid().ToString("N")[..8]);
        int supplier = CreateSupplier("PurRevSup " + Guid.NewGuid().ToString("N")[..8]);
        string no = NewNo("PUR");
        int purchaseId = 0;

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 500m));

        // a purchase that added 100
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, wh, 100m);
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO purchase_bill (bill_no, bill_date, supplier_id, ref_no, credit_days, due_date, remarks, sub_total, discount, tax, grand_total)
                VALUES (@no, CURRENT_DATE, @sup, '', 0, CURRENT_DATE, '', 1000, 0, 0, 1000) RETURNING purchase_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            cmd.Parameters.AddWithValue("sup", supplier);
            purchaseId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(
                "INSERT INTO purchase_bill_item (purchase_id, item_id, qty, rate, disc_percent, amount) VALUES (@p, @item, 100, 10, 0, 1000)", conn, tx);
            line.Parameters.AddWithValue("p", purchaseId);
            line.Parameters.AddWithValue("item", item);
            line.ExecuteNonQuery();

            using var mov = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @wh, 'IN', 100, 'PURCHASE', @p)", conn, tx);
            mov.Parameters.AddWithValue("item", item);
            mov.Parameters.AddWithValue("wh", wh);
            mov.Parameters.AddWithValue("p", purchaseId);
            mov.ExecuteNonQuery();

            using var leg1 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                SELECT @no, 'Purchase Bill', account_id, 'By Purchase Bill', 0, 1000, @p
                FROM supplier_master WHERE supplier_id=@sup", conn, tx);
            leg1.Parameters.AddWithValue("no", no);
            leg1.Parameters.AddWithValue("p", purchaseId);
            leg1.Parameters.AddWithValue("sup", supplier);
            leg1.ExecuteNonQuery();

            using var leg2 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                VALUES (@no, 'Purchase Bill', @acc, 'To Purchase Bill', 1000, 0, @p)", conn, tx);
            leg2.Parameters.AddWithValue("no", no);
            leg2.Parameters.AddWithValue("acc", AccountIdByCode("5000"));
            leg2.Parameters.AddWithValue("p", purchaseId);
            leg2.ExecuteNonQuery();
        });

        Assert.Equal(600m, Balance(item, wh));

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.ReversePurchaseBillPostings(conn, tx, purchaseId));

        Assert.Equal(500m, Balance(item, wh));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='PURCHASE' AND reference_id=@id", new() { ["id"] = purchaseId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM purchase_bill_item WHERE purchase_id=@id", new() { ["id"] = purchaseId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM ledger_entry WHERE voucher_type='Purchase Bill' AND reference_id=@id", new() { ["id"] = purchaseId }));
    }

    // ---- Sales Return: stock back IN + customer credit / Sales debit ---------

    [Fact]
    public void SalesReturn_SaveFlow_AddsStockAtPickedWarehouse_AndPostsLedger()
    {
        int item = CreateItem("SRetFlow " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("SRetFlowWh " + Guid.NewGuid().ToString("N")[..8]);
        int customer = SeededCustomerId();
        string no = NewNo("SRET");
        decimal total = 600m;
        int returnId = 0;

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 50m));

        // what SalesReturnForm BtnSave writes
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO sales_return (return_no, return_date, invoice_id, customer_id, remarks, total_amount)
                VALUES (@no, CURRENT_DATE, NULL, @cust, '', @total) RETURNING return_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            cmd.Parameters.AddWithValue("cust", customer);
            cmd.Parameters.AddWithValue("total", total);
            returnId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(@"
                INSERT INTO sales_return_item (return_id, item_id, qty, rate, disc_percent, amount)
                VALUES (@r, @item, 60, 10, 0, @total)", conn, tx);
            line.Parameters.AddWithValue("r", returnId);
            line.Parameters.AddWithValue("item", item);
            line.Parameters.AddWithValue("total", total);
            line.ExecuteNonQuery();

            using var mov = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @wh, 'IN', 60, 'SALES_RETURN', @r)", conn, tx);
            mov.Parameters.AddWithValue("item", item);
            mov.Parameters.AddWithValue("wh", wh);
            mov.Parameters.AddWithValue("r", returnId);
            mov.ExecuteNonQuery();

            DbHelper.AdjustBalance(conn, tx, item, wh, 60m);

            // ledger: credit the customer (receivable down) / debit Sales (4000)
            using var custLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                SELECT @no, 'Sales Return', account_id, 'By Sales Return', 0, @total, @r
                FROM customer_master WHERE customer_id=@cust", conn, tx);
            custLeg.Parameters.AddWithValue("no", no);
            custLeg.Parameters.AddWithValue("total", total);
            custLeg.Parameters.AddWithValue("r", returnId);
            custLeg.Parameters.AddWithValue("cust", customer);
            custLeg.ExecuteNonQuery();

            using var salesLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                VALUES (@no, 'Sales Return', @acc, 'To Sales Return', @total, 0, @r)", conn, tx);
            salesLeg.Parameters.AddWithValue("no", no);
            salesLeg.Parameters.AddWithValue("acc", AccountIdByCode("4000"));
            salesLeg.Parameters.AddWithValue("total", total);
            salesLeg.Parameters.AddWithValue("r", returnId);
            salesLeg.ExecuteNonQuery();
        });

        Assert.Equal(110m, Balance(item, wh)); // 50 + 60 back in
        Assert.Equal(1, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='SALES_RETURN' AND reference_id=@id", new() { ["id"] = returnId }));
        Assert.Equal(total, SumOf("debit", no));
        Assert.Equal(total, SumOf("credit", no));
        Assert.Equal(total, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(credit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = AccountIdByCode("1100") })));
        Assert.Equal(total, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(debit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = AccountIdByCode("4000") })));
    }

    [Fact]
    public void ReverseSalesReturnPostings_RestoresBalanceAndRemovesPostings()
    {
        int item = CreateItem("SRetRev " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("SRetRevWh " + Guid.NewGuid().ToString("N")[..8]);
        int customer = SeededCustomerId();
        string no = NewNo("SRET");
        int returnId = 0;

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 100m));

        // a sales return that added 40 back
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, wh, 40m);
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO sales_return (return_no, return_date, invoice_id, customer_id, remarks, total_amount)
                VALUES (@no, CURRENT_DATE, NULL, @cust, '', 400) RETURNING return_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            cmd.Parameters.AddWithValue("cust", customer);
            returnId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(
                "INSERT INTO sales_return_item (return_id, item_id, qty, rate, disc_percent, amount) VALUES (@r, @item, 40, 10, 0, 400)", conn, tx);
            line.Parameters.AddWithValue("r", returnId);
            line.Parameters.AddWithValue("item", item);
            line.ExecuteNonQuery();

            using var mov = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @wh, 'IN', 40, 'SALES_RETURN', @r)", conn, tx);
            mov.Parameters.AddWithValue("item", item);
            mov.Parameters.AddWithValue("wh", wh);
            mov.Parameters.AddWithValue("r", returnId);
            mov.ExecuteNonQuery();

            using var leg1 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                SELECT @no, 'Sales Return', account_id, 'By Sales Return', 0, 400, @r
                FROM customer_master WHERE customer_id=@cust", conn, tx);
            leg1.Parameters.AddWithValue("no", no);
            leg1.Parameters.AddWithValue("r", returnId);
            leg1.Parameters.AddWithValue("cust", customer);
            leg1.ExecuteNonQuery();

            using var leg2 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                VALUES (@no, 'Sales Return', @acc, 'To Sales Return', 400, 0, @r)", conn, tx);
            leg2.Parameters.AddWithValue("no", no);
            leg2.Parameters.AddWithValue("acc", AccountIdByCode("4000"));
            leg2.Parameters.AddWithValue("r", returnId);
            leg2.ExecuteNonQuery();
        });

        Assert.Equal(140m, Balance(item, wh));

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.ReverseSalesReturnPostings(conn, tx, returnId));

        Assert.Equal(100m, Balance(item, wh));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='SALES_RETURN' AND reference_id=@id", new() { ["id"] = returnId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM sales_return_item WHERE return_id=@id", new() { ["id"] = returnId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM ledger_entry WHERE voucher_type='Sales Return' AND reference_id=@id", new() { ["id"] = returnId }));
    }

    // ---- Purchase Return: stock OUT + supplier debit / Purchases credit ------

    [Fact]
    public void PurchaseReturn_SaveFlow_RemovesStockAtPickedWarehouse_AndPostsLedger()
    {
        int item = CreateItem("PRetFlow " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("PRetFlowWh " + Guid.NewGuid().ToString("N")[..8]);
        int supplier = CreateSupplier("PRetFlowSup " + Guid.NewGuid().ToString("N")[..8]);
        string no = NewNo("PRET");
        decimal total = 400m;
        int returnId = 0;

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 100m));

        // what PurchaseReturnForm BtnSave writes
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO purchase_return (return_no, return_date, purchase_id, supplier_id, remarks, total_amount)
                VALUES (@no, CURRENT_DATE, NULL, @sup, '', @total) RETURNING return_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            cmd.Parameters.AddWithValue("sup", supplier);
            cmd.Parameters.AddWithValue("total", total);
            returnId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(@"
                INSERT INTO purchase_return_item (return_id, item_id, qty, rate, disc_percent, amount)
                VALUES (@r, @item, 40, 10, 0, @total)", conn, tx);
            line.Parameters.AddWithValue("r", returnId);
            line.Parameters.AddWithValue("item", item);
            line.Parameters.AddWithValue("total", total);
            line.ExecuteNonQuery();

            using var mov = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @wh, 'OUT', 40, 'PURCHASE_RETURN', @r)", conn, tx);
            mov.Parameters.AddWithValue("item", item);
            mov.Parameters.AddWithValue("wh", wh);
            mov.Parameters.AddWithValue("r", returnId);
            mov.ExecuteNonQuery();

            DbHelper.AdjustBalance(conn, tx, item, wh, -40m);

            // ledger: debit the supplier (payable down) / credit Purchases (5000)
            using var supLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                SELECT @no, 'Purchase Return', account_id, 'To Purchase Return', @total, 0, @r
                FROM supplier_master WHERE supplier_id=@sup", conn, tx);
            supLeg.Parameters.AddWithValue("no", no);
            supLeg.Parameters.AddWithValue("total", total);
            supLeg.Parameters.AddWithValue("r", returnId);
            supLeg.Parameters.AddWithValue("sup", supplier);
            supLeg.ExecuteNonQuery();

            using var purchLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                VALUES (@no, 'Purchase Return', @acc, 'By Purchase Return', 0, @total, @r)", conn, tx);
            purchLeg.Parameters.AddWithValue("no", no);
            purchLeg.Parameters.AddWithValue("acc", AccountIdByCode("5000"));
            purchLeg.Parameters.AddWithValue("total", total);
            purchLeg.Parameters.AddWithValue("r", returnId);
            purchLeg.ExecuteNonQuery();
        });

        Assert.Equal(60m, Balance(item, wh)); // 100 − 40 returned
        Assert.Equal(1, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='PURCHASE_RETURN' AND reference_id=@id", new() { ["id"] = returnId }));
        Assert.Equal(total, SumOf("debit", no));
        Assert.Equal(total, SumOf("credit", no));
        int supAcc = Convert.ToInt32(DbHelper.ExecuteScalar(
            "SELECT account_id FROM supplier_master WHERE supplier_id=@id", new Dictionary<string, object?> { ["id"] = supplier }));
        Assert.Equal(total, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(debit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = supAcc })));
        Assert.Equal(total, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(credit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = AccountIdByCode("5000") })));
    }

    [Fact]
    public void ReversePurchaseReturnPostings_RestoresBalanceAndRemovesPostings()
    {
        int item = CreateItem("PRetRev " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("PRetRevWh " + Guid.NewGuid().ToString("N")[..8]);
        int supplier = CreateSupplier("PRetRevSup " + Guid.NewGuid().ToString("N")[..8]);
        string no = NewNo("PRET");
        int returnId = 0;

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 100m));

        // a purchase return that removed 30
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, wh, -30m);
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO purchase_return (return_no, return_date, purchase_id, supplier_id, remarks, total_amount)
                VALUES (@no, CURRENT_DATE, NULL, @sup, '', 300) RETURNING return_id", conn, tx);
            cmd.Parameters.AddWithValue("no", no);
            cmd.Parameters.AddWithValue("sup", supplier);
            returnId = (int)cmd.ExecuteScalar()!;

            using var line = new NpgsqlCommand(
                "INSERT INTO purchase_return_item (return_id, item_id, qty, rate, disc_percent, amount) VALUES (@r, @item, 30, 10, 0, 300)", conn, tx);
            line.Parameters.AddWithValue("r", returnId);
            line.Parameters.AddWithValue("item", item);
            line.ExecuteNonQuery();

            using var mov = new NpgsqlCommand(@"
                INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                VALUES (@item, @wh, 'OUT', 30, 'PURCHASE_RETURN', @r)", conn, tx);
            mov.Parameters.AddWithValue("item", item);
            mov.Parameters.AddWithValue("wh", wh);
            mov.Parameters.AddWithValue("r", returnId);
            mov.ExecuteNonQuery();

            using var leg1 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                SELECT @no, 'Purchase Return', account_id, 'To Purchase Return', 300, 0, @r
                FROM supplier_master WHERE supplier_id=@sup", conn, tx);
            leg1.Parameters.AddWithValue("no", no);
            leg1.Parameters.AddWithValue("r", returnId);
            leg1.Parameters.AddWithValue("sup", supplier);
            leg1.ExecuteNonQuery();

            using var leg2 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                VALUES (@no, 'Purchase Return', @acc, 'By Purchase Return', 0, 300, @r)", conn, tx);
            leg2.Parameters.AddWithValue("no", no);
            leg2.Parameters.AddWithValue("acc", AccountIdByCode("5000"));
            leg2.Parameters.AddWithValue("r", returnId);
            leg2.ExecuteNonQuery();
        });

        Assert.Equal(70m, Balance(item, wh));

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.ReversePurchaseReturnPostings(conn, tx, returnId));

        Assert.Equal(100m, Balance(item, wh));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='PURCHASE_RETURN' AND reference_id=@id", new() { ["id"] = returnId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM purchase_return_item WHERE return_id=@id", new() { ["id"] = returnId }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM ledger_entry WHERE voucher_type='Purchase Return' AND reference_id=@id", new() { ["id"] = returnId }));
    }
}
