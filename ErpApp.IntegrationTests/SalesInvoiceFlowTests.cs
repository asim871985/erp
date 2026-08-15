using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Sales Invoice save flow against the scratch database, mirroring exactly what
/// SalesInvoiceForm.BtnSave runs: header + line items + an OUT stock movement at
/// the picked warehouse + the balance adjustment, then the two balanced ledger
/// legs — customer debit (account from customer_master) / Sales (4000) credit,
/// both posted at the NET grand total. Also covers the edit path (reverse old
/// postings → update header → re-post) proving there's no double posting.
///
/// All assertions are per-document (filtered by voucher no / reference id), so
/// they are immune to sibling tests sharing the collection's database.
/// </summary>
[Collection("Database")]
public class SalesInvoiceFlowTests
{
    private static int AccountIdByCode(string code) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "SELECT account_id FROM chart_of_accounts WHERE account_code=@c",
        new Dictionary<string, object?> { ["c"] = code }));

    private static int CreateWarehouse(string name) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO warehouse_master (warehouse_name, location, active) VALUES (@n, 'Test location', TRUE) RETURNING warehouse_id",
        new Dictionary<string, object?> { ["n"] = name }));

    private static int CreateItem(string name, decimal rate = 100) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO item_master (item_name, rate, active) VALUES (@n, @r, TRUE) RETURNING item_id",
        new Dictionary<string, object?> { ["n"] = name, ["r"] = rate }));

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

    private static string NewNo() => "IT-" + Guid.NewGuid().ToString("N")[..8];

    private sealed record SaleLine(int ItemId, decimal Qty, decimal Rate, decimal DiscPercent);

    /// <summary>
    /// Posts a sale exactly like SalesInvoiceForm.BtnSave: header (computing
    /// sub/discount/grand like the form), line items, OUT movement + balance at
    /// the picked warehouse, and the customer-debit / Sales-credit ledger legs at
    /// the net grand. When <paramref name="existingInvoiceId"/> is given, writes
    /// into that invoice (the edit path after the reversal), otherwise inserts a
    /// new header.
    /// </summary>
    private static int PostSale(string no, int customer, int warehouse, IReadOnlyList<SaleLine> lines,
        int? existingInvoiceId = null)
    {
        decimal subTotal = lines.Sum(l => l.Qty * l.Rate);
        decimal discount = lines.Sum(l => l.Qty * l.Rate * l.DiscPercent / 100m);
        decimal grand = subTotal - discount;
        int invoiceId = 0;

        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            if (existingInvoiceId is int id)
            {
                invoiceId = id;
                using var upd = new NpgsqlCommand(@"
                    UPDATE sales_invoice SET invoice_date=CURRENT_DATE, customer_id=@cust, address='', mobile='',
                           payment_terms='', salesman='', sub_total=@sub, discount=@disc,
                           tax=0, grand_total=@grand, amount_in_words=''
                    WHERE invoice_id=@id", conn, tx);
                upd.Parameters.AddWithValue("cust", customer);
                upd.Parameters.AddWithValue("sub", subTotal);
                upd.Parameters.AddWithValue("disc", discount);
                upd.Parameters.AddWithValue("grand", grand);
                upd.Parameters.AddWithValue("id", id);
                upd.ExecuteNonQuery();
            }
            else
            {
                using var cmd = new NpgsqlCommand(@"
                    INSERT INTO sales_invoice (invoice_no, invoice_date, customer_id, address, mobile,
                        payment_terms, salesman, sub_total, discount, tax, grand_total, amount_in_words)
                    VALUES (@no, CURRENT_DATE, @cust, '', '', '', '', @sub, @disc, 0, @grand, '')
                    RETURNING invoice_id", conn, tx);
                cmd.Parameters.AddWithValue("no", no);
                cmd.Parameters.AddWithValue("cust", customer);
                cmd.Parameters.AddWithValue("sub", subTotal);
                cmd.Parameters.AddWithValue("disc", discount);
                cmd.Parameters.AddWithValue("grand", grand);
                invoiceId = (int)cmd.ExecuteScalar()!;
            }

            foreach (var line in lines)
            {
                using var lineCmd = new NpgsqlCommand(@"
                    INSERT INTO sales_invoice_item (invoice_id, item_id, qty, rate, disc_percent, amount)
                    VALUES (@inv, @item, @qty, @rate, @disc, @amt)", conn, tx);
                lineCmd.Parameters.AddWithValue("inv", invoiceId);
                lineCmd.Parameters.AddWithValue("item", line.ItemId);
                lineCmd.Parameters.AddWithValue("qty", line.Qty);
                lineCmd.Parameters.AddWithValue("rate", line.Rate);
                lineCmd.Parameters.AddWithValue("disc", line.DiscPercent);
                lineCmd.Parameters.AddWithValue("amt", line.Qty * line.Rate);
                lineCmd.ExecuteNonQuery();

                using var mov = new NpgsqlCommand(@"
                    INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                    VALUES (@item, @wh, 'OUT', @qty, 'SALES', @inv)", conn, tx);
                mov.Parameters.AddWithValue("item", line.ItemId);
                mov.Parameters.AddWithValue("wh", warehouse);
                mov.Parameters.AddWithValue("qty", line.Qty);
                mov.Parameters.AddWithValue("inv", invoiceId);
                mov.ExecuteNonQuery();

                DbHelper.AdjustBalance(conn, tx, line.ItemId, warehouse, -line.Qty);
            }

            // ledger: debit the customer (receivable up) / credit Sales (4000), both at the net grand
            using var custLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                SELECT @no, 'Sales Invoice', account_id, 'To Sales Invoice', @grand, 0, @inv
                FROM customer_master WHERE customer_id=@cust", conn, tx);
            custLeg.Parameters.AddWithValue("no", no);
            custLeg.Parameters.AddWithValue("grand", grand);
            custLeg.Parameters.AddWithValue("inv", invoiceId);
            custLeg.Parameters.AddWithValue("cust", customer);
            custLeg.ExecuteNonQuery();

            using var salesLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                VALUES (@no, 'Sales Invoice', @acc, 'By Sales Invoice', 0, @grand, @inv)", conn, tx);
            salesLeg.Parameters.AddWithValue("no", no);
            salesLeg.Parameters.AddWithValue("acc", AccountIdByCode("4000"));
            salesLeg.Parameters.AddWithValue("grand", grand);
            salesLeg.Parameters.AddWithValue("inv", invoiceId);
            salesLeg.ExecuteNonQuery();
        });

        return invoiceId;
    }

    // ---- Save: stock OUT at the picked warehouse + balanced ledger -----------

    [Fact]
    public void SalesInvoice_SaveFlow_RemovesStockAtPickedWarehouse_AndPostsLedger()
    {
        int item1 = CreateItem("InvFlowA " + Guid.NewGuid().ToString("N")[..8]);
        int item2 = CreateItem("InvFlowB " + Guid.NewGuid().ToString("N")[..8]);
        int wh1 = CreateWarehouse("InvFlowMain " + Guid.NewGuid().ToString("N")[..8]);
        int wh2 = CreateWarehouse("InvFlowBranch " + Guid.NewGuid().ToString("N")[..8]);
        int customer = SeededCustomerId();
        string no = NewNo();

        // stock before the sale: 100 of each at the picked warehouse (wh2)
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item1, wh2, 100m);
            DbHelper.AdjustBalance(conn, tx, item2, wh2, 100m);
        });

        int invoiceId = PostSale(no, customer, wh2,
            new[] { new SaleLine(item1, 30, 100, 0), new SaleLine(item2, 20, 50, 0) });

        // stock came out of the picked warehouse only
        Assert.Equal(70m, Balance(item1, wh2));
        Assert.Equal(80m, Balance(item2, wh2));
        Assert.Equal(0m, Balance(item1, wh1)); // the non-picked warehouse is untouched
        Assert.Equal(2, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='SALES' AND reference_id=@id", new() { ["id"] = invoiceId }));

        // ledger: customer debit 4000 (30×100 + 20×50) / Sales credit 4000, balanced
        Assert.Equal(4000m, SumOf("debit", no));
        Assert.Equal(4000m, SumOf("credit", no));
        Assert.Equal(4000m, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(debit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = AccountIdByCode("1100") })));
        Assert.Equal(4000m, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(credit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = AccountIdByCode("4000") })));
    }

    // ---- Discount: header stores gross, ledger posts net ---------------------

    [Fact]
    public void SalesInvoice_Discount_StoresGrossInHeader_ButPostsLedgerAtNet()
    {
        int item = CreateItem("InvDisc " + Guid.NewGuid().ToString("N")[..8]);
        int wh = CreateWarehouse("InvDiscWh " + Guid.NewGuid().ToString("N")[..8]);
        int customer = SeededCustomerId();
        string no = NewNo();

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.AdjustBalance(conn, tx, item, wh, 100m));

        // 10 × 100 with 10% line discount → sub 1000, discount 100, grand 900
        int invoiceId = PostSale(no, customer, wh, new[] { new SaleLine(item, 10, 100, 10m) });

        Assert.Equal(90m, Balance(item, wh));

        // header keeps the gross figures…
        var header = DbHelper.ExecuteQuery("SELECT sub_total, discount, grand_total FROM sales_invoice WHERE invoice_id=@id",
            new Dictionary<string, object?> { ["id"] = invoiceId });
        Assert.Equal(1000m, Convert.ToDecimal(header.Rows[0]["sub_total"]));
        Assert.Equal(100m, Convert.ToDecimal(header.Rows[0]["discount"]));
        Assert.Equal(900m, Convert.ToDecimal(header.Rows[0]["grand_total"]));

        // …but both ledger legs post the NET grand (the form uses @grand for both)
        Assert.Equal(900m, SumOf("debit", no));
        Assert.Equal(900m, SumOf("credit", no));
        Assert.Equal(2, Count("SELECT COUNT(*) FROM ledger_entry WHERE voucher_no=@no AND voucher_type='Sales Invoice'", new() { ["no"] = no })); // exactly the two legs
    }

    // ---- Edit: reverse old postings, then re-post without doubles -------------

    [Fact]
    public void SalesInvoice_EditSave_ReversesOldPostings_AndRepostsCleanly()
    {
        int item = CreateItem("InvEdit " + Guid.NewGuid().ToString("N")[..8]);
        int wh1 = CreateWarehouse("InvEditMain " + Guid.NewGuid().ToString("N")[..8]);
        int wh2 = CreateWarehouse("InvEditBranch " + Guid.NewGuid().ToString("N")[..8]);
        int customer = SeededCustomerId();
        string no = NewNo();

        // opening stock at both warehouses
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            DbHelper.AdjustBalance(conn, tx, item, wh1, 50m);
            DbHelper.AdjustBalance(conn, tx, item, wh2, 50m);
        });

        // first save: 10 out of wh1
        int invoiceId = PostSale(no, customer, wh1, new[] { new SaleLine(item, 10, 100, 0) });
        Assert.Equal(40m, Balance(item, wh1));
        Assert.Equal(50m, Balance(item, wh2));

        // edit save: exactly what the form does — reverse the old postings, then
        // re-post against the same invoice (now 5 out of wh2 instead)
        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.ReverseSalesInvoicePostings(conn, tx, invoiceId));
        PostSale(no, customer, wh2, new[] { new SaleLine(item, 5, 100, 0) }, existingInvoiceId: invoiceId);

        // balances reflect only the new lines: wh1 fully restored, wh2 −5
        Assert.Equal(50m, Balance(item, wh1));
        Assert.Equal(45m, Balance(item, wh2));

        // exactly one movement + two ledger legs remain — no double posting
        Assert.Equal(1, Count("SELECT COUNT(*) FROM stock_movement WHERE reference_type='SALES' AND reference_id=@id", new() { ["id"] = invoiceId }));
        Assert.Equal(2, Count("SELECT COUNT(*) FROM ledger_entry WHERE voucher_type='Sales Invoice' AND reference_id=@id", new() { ["id"] = invoiceId }));
        Assert.Equal(500m, SumOf("debit", no));  // 5 × 100
        Assert.Equal(500m, SumOf("credit", no));
    }
}
