using System.Data;
using Npgsql;

namespace ErpApp.Data;

/// <summary>
/// Thin wrapper around Npgsql for the whole application.
/// Every method opens its own short-lived connection (simple & safe for a desktop app).
/// </summary>
public static class DbHelper
{
    private static NpgsqlConnection NewConnection() => new(AppConfig.ConnectionString);

    /// <summary>Breaks the connection string into parts usable by pg_dump/psql command-line tools.</summary>
    public static (string Host, string Port, string Database, string Username, string Password) GetConnectionParts()
    {
        var builder = new NpgsqlConnectionStringBuilder(AppConfig.ConnectionString);
        return (builder.Host ?? "localhost", builder.Port.ToString(), builder.Database ?? "erp_db",
                builder.Username ?? "postgres", builder.Password ?? "");
    }

    public static bool TestConnection(out string message)
    {
        try
        {
            using var conn = NewConnection();
            conn.Open();
            message = "Connected successfully.";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public static DataTable ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        using var conn = NewConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(sql, conn);
        AddParams(cmd, parameters);

        var table = new DataTable();
        using var adapter = new NpgsqlDataAdapter(cmd);
        adapter.Fill(table);
        return table;
    }

    public static object? ExecuteScalar(string sql, Dictionary<string, object?>? parameters = null)
    {
        using var conn = NewConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(sql, conn);
        AddParams(cmd, parameters);
        return cmd.ExecuteScalar();
    }

    public static int ExecuteNonQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        using var conn = NewConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(sql, conn);
        AddParams(cmd, parameters);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Runs several statements in a single transaction. Throws & rolls back on error.</summary>
    public static void ExecuteTransaction(Action<NpgsqlConnection, NpgsqlTransaction> work)
    {
        using var conn = NewConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            work(conn, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void AddParams(NpgsqlCommand cmd, Dictionary<string, object?>? parameters)
    {
        if (parameters == null) return;
        foreach (var kv in parameters)
            cmd.Parameters.AddWithValue(kv.Key, kv.Value ?? DBNull.Value);
    }

    /// <summary>
    /// Gets the next document number (e.g. INV-00047) and atomically increments the counter.
    /// </summary>
    /// <summary>Looks up a well-known account (e.g. "1000" = Cash in Hand, "4000" = Sales) by its code.</summary>
    public static int GetAccountIdByCode(NpgsqlConnection conn, NpgsqlTransaction tx, string code)
    {
        using var cmd = new NpgsqlCommand("SELECT account_id FROM chart_of_accounts WHERE account_code=@c", conn, tx);
        cmd.Parameters.AddWithValue("c", code);
        var result = cmd.ExecuteScalar();
        if (result == null)
            throw new Exception($"Account code '{code}' not found in Chart of Accounts. " +
                                 "Check Master > Account Master (or Accounting > Chart of Accounts).");
        return (int)result;
    }

    /// <summary>Looks up a well-known account (e.g. "1000" = Cash in Hand, "4000" = Sales) by its code.</summary>
    public static int GetAccountIdByCode(string code)
    {
        var result = ExecuteScalar("SELECT account_id FROM chart_of_accounts WHERE account_code=@c",
            new Dictionary<string, object?> { ["c"] = code });
        if (result == null)
            throw new Exception($"Account code '{code}' not found in Chart of Accounts.");
        return Convert.ToInt32(result);
    }

    /// <summary>Returns the first active warehouse (used as the fallback warehouse for new items' opening stock).</summary>
    public static int GetDefaultWarehouseId()
    {
        var result = ExecuteScalar("SELECT warehouse_id FROM warehouse_master WHERE active ORDER BY warehouse_id LIMIT 1")
            ?? throw new Exception("No active warehouse found. Create one in Master > Warehouse Master.");
        return Convert.ToInt32(result);
    }

    /// <summary>Transaction-scoped variant of <see cref="GetDefaultWarehouseId()"/>.</summary>
    public static int GetDefaultWarehouseId(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        using var cmd = new NpgsqlCommand("SELECT warehouse_id FROM warehouse_master WHERE active ORDER BY warehouse_id LIMIT 1", conn, tx);
        var result = cmd.ExecuteScalar()
            ?? throw new Exception("No active warehouse found. Create one in Master > Warehouse Master.");
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Adjusts one (item, warehouse) balance row by a signed quantity — creates the row if
    /// it doesn't exist, otherwise updates it. Used by transaction saves and reversals.
    /// </summary>
    public static void AdjustBalance(NpgsqlConnection conn, NpgsqlTransaction tx, int itemId, int warehouseId, decimal signedQty)
    {
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO stock_balance (item_id, warehouse_id, qty_on_hand) VALUES (@item, @wh, @qty)
            ON CONFLICT (item_id, warehouse_id) DO UPDATE SET qty_on_hand = stock_balance.qty_on_hand + @qty", conn, tx);
        cmd.Parameters.AddWithValue("item", itemId);
        cmd.Parameters.AddWithValue("wh", warehouseId);
        cmd.Parameters.AddWithValue("qty", signedQty);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads the (item, qty, warehouse) rows a document's stock movements recorded, so a
    /// reversal can restore each warehouse's balance exactly. Old rows with a NULL
    /// warehouse (saved before per-warehouse balances) fall back to the default warehouse.
    /// </summary>
    private static List<(int ItemId, decimal Qty, int WarehouseId)> ReadMovementsForReversal(
        NpgsqlConnection conn, NpgsqlTransaction tx, string referenceType, int referenceId, int defaultWarehouseId)
    {
        var result = new List<(int, decimal, int)>();
        using var cmd = new NpgsqlCommand(@"
            SELECT item_id, qty, COALESCE(warehouse_id, @def) FROM stock_movement
            WHERE reference_type=@rt AND reference_id=@id", conn, tx);
        cmd.Parameters.AddWithValue("def", defaultWarehouseId);
        cmd.Parameters.AddWithValue("rt", referenceType);
        cmd.Parameters.AddWithValue("id", referenceId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add((reader.GetInt32(0), reader.GetDecimal(1), reader.GetInt32(2)));
        return result;
    }

    /// <summary>
    /// Undoes a previously-saved Sales Invoice's stock and ledger effects (adds the qty back
    /// to each warehouse's stock_balance, deletes its stock_movement/ledger_entry rows, deletes
    /// its line items) WITHOUT deleting the sales_invoice header row itself. Used by both
    /// "Delete" (which then also deletes the header) and "Edit & Save" (which then re-applies
    /// the new lines).
    /// </summary>
    public static void ReverseSalesInvoicePostings(NpgsqlConnection conn, NpgsqlTransaction tx, int invoiceId)
    {
        int defaultWh = GetDefaultWarehouseId(conn, tx);
        foreach (var (itemId, qty, wh) in ReadMovementsForReversal(conn, tx, "SALES", invoiceId, defaultWh))
            AdjustBalance(conn, tx, itemId, wh, qty); // sale removed qty → add it back

        using var delMove = new NpgsqlCommand("DELETE FROM stock_movement WHERE reference_type='SALES' AND reference_id=@id", conn, tx);
        delMove.Parameters.AddWithValue("id", invoiceId);
        delMove.ExecuteNonQuery();

        using var delLedger = new NpgsqlCommand("DELETE FROM ledger_entry WHERE voucher_type='Sales Invoice' AND reference_id=@id", conn, tx);
        delLedger.Parameters.AddWithValue("id", invoiceId);
        delLedger.ExecuteNonQuery();

        using var delItems = new NpgsqlCommand("DELETE FROM sales_invoice_item WHERE invoice_id=@id", conn, tx);
        delItems.Parameters.AddWithValue("id", invoiceId);
        delItems.ExecuteNonQuery();
    }

    /// <summary>Mirror of <see cref="ReverseSalesInvoicePostings"/> for Purchase Bills (stock was added, so reversal subtracts).</summary>
    public static void ReversePurchaseBillPostings(NpgsqlConnection conn, NpgsqlTransaction tx, int purchaseId)
    {
        int defaultWh = GetDefaultWarehouseId(conn, tx);
        foreach (var (itemId, qty, wh) in ReadMovementsForReversal(conn, tx, "PURCHASE", purchaseId, defaultWh))
            AdjustBalance(conn, tx, itemId, wh, -qty); // purchase added qty → subtract it

        using var delMove = new NpgsqlCommand("DELETE FROM stock_movement WHERE reference_type='PURCHASE' AND reference_id=@id", conn, tx);
        delMove.Parameters.AddWithValue("id", purchaseId);
        delMove.ExecuteNonQuery();

        using var delLedger = new NpgsqlCommand("DELETE FROM ledger_entry WHERE voucher_type='Purchase Bill' AND reference_id=@id", conn, tx);
        delLedger.Parameters.AddWithValue("id", purchaseId);
        delLedger.ExecuteNonQuery();

        using var delItems = new NpgsqlCommand("DELETE FROM purchase_bill_item WHERE purchase_id=@id", conn, tx);
        delItems.Parameters.AddWithValue("id", purchaseId);
        delItems.ExecuteNonQuery();
    }

    /// <summary>Mirror of <see cref="ReverseSalesInvoicePostings"/> for Sales Return (stock was added back, so reversal subtracts).</summary>
    public static void ReverseSalesReturnPostings(NpgsqlConnection conn, NpgsqlTransaction tx, int returnId)
    {
        int defaultWh = GetDefaultWarehouseId(conn, tx);
        foreach (var (itemId, qty, wh) in ReadMovementsForReversal(conn, tx, "SALES_RETURN", returnId, defaultWh))
            AdjustBalance(conn, tx, itemId, wh, -qty); // return added qty back → subtract it

        using var delMove = new NpgsqlCommand("DELETE FROM stock_movement WHERE reference_type='SALES_RETURN' AND reference_id=@id", conn, tx);
        delMove.Parameters.AddWithValue("id", returnId);
        delMove.ExecuteNonQuery();

        using var delLedger = new NpgsqlCommand("DELETE FROM ledger_entry WHERE voucher_type='Sales Return' AND reference_id=@id", conn, tx);
        delLedger.Parameters.AddWithValue("id", returnId);
        delLedger.ExecuteNonQuery();

        using var delItems = new NpgsqlCommand("DELETE FROM sales_return_item WHERE return_id=@id", conn, tx);
        delItems.Parameters.AddWithValue("id", returnId);
        delItems.ExecuteNonQuery();
    }

    /// <summary>Mirror of <see cref="ReversePurchaseBillPostings"/> for Purchase Return (stock was removed, so reversal adds back).</summary>
    public static void ReversePurchaseReturnPostings(NpgsqlConnection conn, NpgsqlTransaction tx, int returnId)
    {
        int defaultWh = GetDefaultWarehouseId(conn, tx);
        foreach (var (itemId, qty, wh) in ReadMovementsForReversal(conn, tx, "PURCHASE_RETURN", returnId, defaultWh))
            AdjustBalance(conn, tx, itemId, wh, qty); // return removed qty → add it back

        using var delMove = new NpgsqlCommand("DELETE FROM stock_movement WHERE reference_type='PURCHASE_RETURN' AND reference_id=@id", conn, tx);
        delMove.Parameters.AddWithValue("id", returnId);
        delMove.ExecuteNonQuery();

        using var delLedger = new NpgsqlCommand("DELETE FROM ledger_entry WHERE voucher_type='Purchase Return' AND reference_id=@id", conn, tx);
        delLedger.Parameters.AddWithValue("id", returnId);
        delLedger.ExecuteNonQuery();

        using var delItems = new NpgsqlCommand("DELETE FROM purchase_return_item WHERE return_id=@id", conn, tx);
        delItems.Parameters.AddWithValue("id", returnId);
        delItems.ExecuteNonQuery();
    }

    /// <summary>
    /// Undoes a Stock Transfer's balance effect (adds qty back to the From warehouse,
    /// subtracts from the To warehouse) and deletes its movement rows and line items.
    /// </summary>
    public static void ReverseStockTransferPostings(NpgsqlConnection conn, NpgsqlTransaction tx, int transferId)
    {
        int defaultWh = GetDefaultWarehouseId(conn, tx);
        var toReverse = new List<(int itemId, decimal qty, int wh, bool isOut)>();
        using (var cmd = new NpgsqlCommand(@"
            SELECT item_id, qty, COALESCE(warehouse_id, @def), movement_type FROM stock_movement
            WHERE reference_type='TRANSFER' AND reference_id=@id", conn, tx))
        {
            cmd.Parameters.AddWithValue("def", defaultWh);
            cmd.Parameters.AddWithValue("id", transferId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                toReverse.Add((reader.GetInt32(0), reader.GetDecimal(1), reader.GetInt32(2), reader.GetString(3) == "TRANSFER_OUT"));
        }
        foreach (var (itemId, qty, wh, isOut) in toReverse)
            AdjustBalance(conn, tx, itemId, wh, isOut ? qty : -qty); // OUT leg removed qty → add back; IN leg added → subtract

        using var delMove = new NpgsqlCommand("DELETE FROM stock_movement WHERE reference_type='TRANSFER' AND reference_id=@id", conn, tx);
        delMove.Parameters.AddWithValue("id", transferId);
        delMove.ExecuteNonQuery();

        using var delItems = new NpgsqlCommand("DELETE FROM stock_transfer_item WHERE transfer_id=@id", conn, tx);
        delItems.Parameters.AddWithValue("id", transferId);
        delItems.ExecuteNonQuery();
    }

    /// <summary>
    /// Undoes a Stock Adjustment's balance effect (direction from the header's
    /// Increase/Decrease type) and deletes its movement rows and line items.
    /// </summary>
    public static void ReverseStockAdjustmentPostings(NpgsqlConnection conn, NpgsqlTransaction tx, int adjustmentId)
    {
        string adjustmentType = "Increase";
        using (var tCmd = new NpgsqlCommand("SELECT adjustment_type FROM stock_adjustment WHERE adjustment_id=@id", conn, tx))
        {
            tCmd.Parameters.AddWithValue("id", adjustmentId);
            adjustmentType = (tCmd.ExecuteScalar() as string) ?? "Increase";
        }
        bool wasIncrease = adjustmentType == "Increase";

        int defaultWh = GetDefaultWarehouseId(conn, tx);
        foreach (var (itemId, qty, wh) in ReadMovementsForReversal(conn, tx, "ADJUSTMENT", adjustmentId, defaultWh))
            AdjustBalance(conn, tx, itemId, wh, wasIncrease ? -qty : qty); // increase added qty → subtract; decrease removed → add

        using var delMove = new NpgsqlCommand("DELETE FROM stock_movement WHERE reference_type='ADJUSTMENT' AND reference_id=@id", conn, tx);
        delMove.Parameters.AddWithValue("id", adjustmentId);
        delMove.ExecuteNonQuery();

        using var delItems = new NpgsqlCommand("DELETE FROM stock_adjustment_item WHERE adjustment_id=@id", conn, tx);
        delItems.Parameters.AddWithValue("id", adjustmentId);
        delItems.ExecuteNonQuery();
    }

    /// <summary>
    /// Removes a Receipt's two ledger legs (found by voucher_no, since receipt_voucher has no
    /// reference_id column) so it can be edited or deleted cleanly.
    /// </summary>
    public static void ReverseReceiptPostings(NpgsqlConnection conn, NpgsqlTransaction tx, string receiptNo)
    {
        using var delLedger = new NpgsqlCommand("DELETE FROM ledger_entry WHERE voucher_type='Receipt' AND voucher_no=@no", conn, tx);
        delLedger.Parameters.AddWithValue("no", receiptNo);
        delLedger.ExecuteNonQuery();
    }

    /// <summary>Mirror of <see cref="ReverseReceiptPostings"/> for Payment.</summary>
    public static void ReversePaymentPostings(NpgsqlConnection conn, NpgsqlTransaction tx, string paymentNo)
    {
        using var delLedger = new NpgsqlCommand("DELETE FROM ledger_entry WHERE voucher_type='Payment' AND voucher_no=@no", conn, tx);
        delLedger.Parameters.AddWithValue("no", paymentNo);
        delLedger.ExecuteNonQuery();
    }

    /// <summary>Writes one row to database_log. Never throws — logging should never block the real action.</summary>
    public static void LogAction(string action)
    {
        try
        {
            ExecuteNonQuery("INSERT INTO database_log (username, action) VALUES (@u, @a)",
                new Dictionary<string, object?> { ["u"] = AppConfig.CurrentUser, ["a"] = action });
        }
        catch { /* logging is best-effort */ }
    }

    public static string GetNextDocumentNumber(NpgsqlConnection conn, NpgsqlTransaction tx, string docType)
    {
        using var cmd = new NpgsqlCommand(
            "SELECT prefix, suffix, next_number, padding FROM document_numbering WHERE doc_type=@t FOR UPDATE",
            conn, tx);
        cmd.Parameters.AddWithValue("t", docType);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new Exception($"Document numbering not configured for '{docType}'.");

        string prefix = reader.GetString(0);
        string suffix = reader.IsDBNull(1) ? "" : reader.GetString(1);
        int next = reader.GetInt32(2);
        int padding = reader.GetInt32(3);
        reader.Close();

        using var upd = new NpgsqlCommand(
            "UPDATE document_numbering SET next_number = next_number + 1 WHERE doc_type=@t", conn, tx);
        upd.Parameters.AddWithValue("t", docType);
        upd.ExecuteNonQuery();

        return prefix + next.ToString().PadLeft(padding, '0') + suffix;
    }
}
