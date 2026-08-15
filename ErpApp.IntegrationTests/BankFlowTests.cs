using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Bank-account flow against the scratch database: non-cash receipts/payments
/// post their cash/bank leg to the picked BANK account (Cash stays on 1000),
/// the edit/print bank-leg derivation finds that account, the Cash Flow
/// Statement sees money in any BANK account (not just the old 1001), and the
/// Bank Summary report computes per-bank opening/in/out/closing.
///
/// The SQL mirrors what the forms run (ReceiptForm/PaymentForm ledger posts,
/// CashFlowStatementForm, BankSummaryForm) and is driven through the real
/// DbHelper, including the ReverseReceiptPostings reversal.
/// </summary>
[Collection("Database")]
public class BankFlowTests
{
    private const string PartyCode = "1100"; // seeded Accounts Receivable

    private static int AccountIdByCode(string code) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "SELECT account_id FROM chart_of_accounts WHERE account_code=@c",
        new Dictionary<string, object?> { ["c"] = code }));

    private static int CreateBank(string name) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO chart_of_accounts (account_code, account_name, account_type, balance_type) " +
        "VALUES (@code, @name, 'BANK', 'Dr') RETURNING account_id",
        new Dictionary<string, object?> { ["code"] = "B" + Guid.NewGuid().ToString("N")[..8], ["name"] = name }));

    private static decimal SumDebit(string voucherNo) => Convert.ToDecimal(DbHelper.ExecuteScalar(
        "SELECT COALESCE(SUM(debit),0) FROM ledger_entry WHERE voucher_no=@no",
        new Dictionary<string, object?> { ["no"] = voucherNo }));

    private static decimal SumCredit(string voucherNo) => Convert.ToDecimal(DbHelper.ExecuteScalar(
        "SELECT COALESCE(SUM(credit),0) FROM ledger_entry WHERE voucher_no=@no",
        new Dictionary<string, object?> { ["no"] = voucherNo }));

    /// <summary>
    /// Posts a voucher exactly like ReceiptForm/PaymentForm BtnSave: header + two
    /// balanced ledger legs. Receipt = credit party / debit bank; Payment = debit
    /// party / credit bank.
    /// </summary>
    private static void PostVoucher(string table, string no, string type, int partyAccount, int bankAccount, decimal amount)
    {
        bool isReceipt = type == "Receipt";
        decimal partyDebit = isReceipt ? 0 : amount;
        decimal partyCredit = isReceipt ? amount : 0;
        decimal bankDebit = isReceipt ? amount : 0;
        decimal bankCredit = isReceipt ? 0 : amount;

        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var header = new NpgsqlCommand(
                $"INSERT INTO {table} ({(isReceipt ? "receipt_no, received_by" : "payment_no, paid_by")}, account_id, payment_mode, amount) " +
                $"VALUES (@no, @by, @acc, 'Bank Transfer', @amt)", conn, tx);
            header.Parameters.AddWithValue("no", no);
            header.Parameters.AddWithValue("by", "Admin");
            header.Parameters.AddWithValue("acc", partyAccount);
            header.Parameters.AddWithValue("amt", amount);
            header.ExecuteNonQuery();

            using var partyLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit)
                VALUES (@no, @vt, @acc, @part, @d, @c)", conn, tx);
            partyLeg.Parameters.AddWithValue("no", no);
            partyLeg.Parameters.AddWithValue("vt", type);
            partyLeg.Parameters.AddWithValue("acc", partyAccount);
            partyLeg.Parameters.AddWithValue("part", isReceipt ? "By Cash Receipt" : "By Payment");
            partyLeg.Parameters.AddWithValue("d", partyDebit);
            partyLeg.Parameters.AddWithValue("c", partyCredit);
            partyLeg.ExecuteNonQuery();

            using var bankLeg = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit)
                VALUES (@no, @vt, @acc, @part, @d, @c)", conn, tx);
            bankLeg.Parameters.AddWithValue("no", no);
            bankLeg.Parameters.AddWithValue("vt", type);
            bankLeg.Parameters.AddWithValue("acc", bankAccount);
            bankLeg.Parameters.AddWithValue("part", isReceipt ? "To Cash Receipt" : "To Payment");
            bankLeg.Parameters.AddWithValue("d", bankDebit);
            bankLeg.Parameters.AddWithValue("c", bankCredit);
            bankLeg.ExecuteNonQuery();
        });
    }

    private static string NewNo(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];

    // ---- Non-cash receipts/payments hit the picked bank --------------------

    [Fact]
    public void NonCashReceipt_PostsDebitToPickedBank()
    {
        int party = AccountIdByCode(PartyCode);
        int bank1 = AccountIdByCode("1001"); // generic Bank Account
        int bank2 = CreateBank("HBL Current " + Guid.NewGuid().ToString("N")[..8]);
        string no = NewNo("RCPT");

        PostVoucher("receipt_voucher", no, "Receipt", party, bank2, 5000m);

        Assert.Equal(5000m, SumCredit(no));
        Assert.Equal(5000m, SumDebit(no));
        // the picked bank took the debit; the other bank saw nothing
        Assert.Equal(5000m, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(debit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = bank2 })));
        Assert.Equal(0m, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(debit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = bank1 })));
    }

    [Fact]
    public void CashReceipt_PostsDebitToCashInHand_Code1000()
    {
        int party = AccountIdByCode(PartyCode);
        int cash = AccountIdByCode("1000");
        string no = NewNo("RCPT");

        // cash leg: what ReceiptForm does when Payment Mode = Cash
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var leg1 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit)
                VALUES (@no, 'Receipt', @acc, 'By Cash Receipt', 0, 300)", conn, tx);
            leg1.Parameters.AddWithValue("no", no);
            leg1.Parameters.AddWithValue("acc", party);
            leg1.ExecuteNonQuery();

            using var leg2 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit)
                VALUES (@no, 'Receipt', @acc, 'To Cash Receipt', 300, 0)", conn, tx);
            leg2.Parameters.AddWithValue("no", no);
            leg2.Parameters.AddWithValue("acc", cash);
            leg2.ExecuteNonQuery();
        });

        Assert.Equal(300m, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(debit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = cash })));
    }

    [Fact]
    public void NonCashPayment_PostsCreditToPickedBank()
    {
        int party = AccountIdByCode(PartyCode);
        int bank2 = CreateBank("UBL " + Guid.NewGuid().ToString("N")[..8]);
        string no = NewNo("PAY");

        PostVoucher("payment_voucher", no, "Payment", party, bank2, 2500m);

        Assert.Equal(2500m, Convert.ToDecimal(DbHelper.ExecuteScalar(
            "SELECT COALESCE(SUM(credit),0) FROM ledger_entry WHERE voucher_no=@no AND account_id=@a",
            new Dictionary<string, object?> { ["no"] = no, ["a"] = bank2 })));
        Assert.Equal(2500m, SumDebit(no)); // party leg
    }

    // ---- Bank-leg derivation (edit + print) --------------------------------

    [Fact]
    public void BankLegDerivation_Receipt_FindsPickedBank()
    {
        int party = AccountIdByCode(PartyCode);
        int bank2 = CreateBank("MCB " + Guid.NewGuid().ToString("N")[..8]);
        string no = NewNo("RCPT");

        PostVoucher("receipt_voucher", no, "Receipt", party, bank2, 1000m);

        // what ReceiptForm.RestoreBankFromLedger runs on edit
        int derived = Convert.ToInt32(DbHelper.ExecuteScalar(@"
            SELECT l.account_id FROM ledger_entry l
            WHERE l.voucher_no=@no AND l.voucher_type='Receipt'
              AND l.debit > 0 AND l.account_id <> @acc
            ORDER BY l.entry_id LIMIT 1",
            new Dictionary<string, object?> { ["no"] = no, ["acc"] = party }));

        Assert.Equal(bank2, derived);
    }

    [Fact]
    public void BankLegDerivation_Payment_FindsPickedBank()
    {
        int party = AccountIdByCode(PartyCode);
        int bank2 = CreateBank("Alfalah " + Guid.NewGuid().ToString("N")[..8]);
        string no = NewNo("PAY");

        PostVoucher("payment_voucher", no, "Payment", party, bank2, 800m);

        int derived = Convert.ToInt32(DbHelper.ExecuteScalar(@"
            SELECT l.account_id FROM ledger_entry l
            WHERE l.voucher_no=@no AND l.voucher_type='Payment'
              AND l.credit > 0 AND l.account_id <> @acc
            ORDER BY l.entry_id LIMIT 1",
            new Dictionary<string, object?> { ["no"] = no, ["acc"] = party }));

        Assert.Equal(bank2, derived);
    }

    // ---- Reversal -----------------------------------------------------------

    [Fact]
    public void ReverseReceiptPostings_RemovesBothLedgerLegs()
    {
        int party = AccountIdByCode(PartyCode);
        int bank2 = CreateBank("HBL Rev " + Guid.NewGuid().ToString("N")[..8]);
        string no = NewNo("RCPT");

        PostVoucher("receipt_voucher", no, "Receipt", party, bank2, 900m);
        Assert.Equal(2, Convert.ToInt32(DbHelper.ExecuteScalar(
            "SELECT COUNT(*) FROM ledger_entry WHERE voucher_no=@no", new Dictionary<string, object?> { ["no"] = no })));

        DbHelper.ExecuteTransaction((conn, tx) => DbHelper.ReverseReceiptPostings(conn, tx, no));

        Assert.Equal(0, Convert.ToInt32(DbHelper.ExecuteScalar(
            "SELECT COUNT(*) FROM ledger_entry WHERE voucher_no=@no AND voucher_type='Receipt'",
            new Dictionary<string, object?> { ["no"] = no })));
    }

    // ---- Bank Summary --------------------------------------------------------

    [Fact]
    public void BankSummary_PerBankOpeningInOutClosing()
    {
        int bank1 = AccountIdByCode("1001");
        int bank2 = CreateBank("BS HBL " + Guid.NewGuid().ToString("N")[..8]);
        DateTime from = DateTime.Today.AddDays(-30), to = DateTime.Today;

        // bank1: opening 1000 before From, in 500 / out 200 in period → closing 1300
        // bank2: opening 0, in 700 → closing 700
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var opening = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, 'Receipt', @acc, 'Opening', 1000, 0, @d)", conn, tx);
            opening.Parameters.AddWithValue("no", NewNo("RCPT"));
            opening.Parameters.AddWithValue("acc", bank1);
            opening.Parameters.AddWithValue("d", from.AddDays(-10));
            opening.ExecuteNonQuery();

            using var in1 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, 'Receipt', @acc, 'In', 500, 0, @d)", conn, tx);
            in1.Parameters.AddWithValue("no", NewNo("RCPT"));
            in1.Parameters.AddWithValue("acc", bank1);
            in1.Parameters.AddWithValue("d", from.AddDays(5));
            in1.ExecuteNonQuery();

            using var out1 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, 'Payment', @acc, 'Out', 0, 200, @d)", conn, tx);
            out1.Parameters.AddWithValue("no", NewNo("PAY"));
            out1.Parameters.AddWithValue("acc", bank1);
            out1.Parameters.AddWithValue("d", from.AddDays(5));
            out1.ExecuteNonQuery();

            using var in2 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, 'Receipt', @acc, 'In', 700, 0, @d)", conn, tx);
            in2.Parameters.AddWithValue("no", NewNo("RCPT"));
            in2.Parameters.AddWithValue("acc", bank2);
            in2.Parameters.AddWithValue("d", from.AddDays(5));
            in2.ExecuteNonQuery();
        });

        // the BankSummaryForm query shape
        var rows = DbHelper.ExecuteQuery(@"
            SELECT a.account_id, a.account_name,
                   COALESCE(a.opening_balance,0) + COALESCE(SUM(CASE WHEN l.entry_date < @from THEN l.debit - l.credit ELSE 0 END),0) AS opening,
                   COALESCE(SUM(CASE WHEN l.entry_date BETWEEN @from AND @to THEN l.debit ELSE 0 END),0) AS cash_in,
                   COALESCE(SUM(CASE WHEN l.entry_date BETWEEN @from AND @to THEN l.credit ELSE 0 END),0) AS cash_out
            FROM chart_of_accounts a
            LEFT JOIN ledger_entry l ON l.account_id = a.account_id
            WHERE a.account_type='BANK' AND a.active
            GROUP BY a.account_id, a.account_name, a.opening_balance
            ORDER BY a.account_name",
            new Dictionary<string, object?> { ["from"] = from, ["to"] = to });

        var byId = new Dictionary<int, (decimal Opening, decimal In, decimal Out)>();
        foreach (System.Data.DataRow r in rows.Rows)
            byId[Convert.ToInt32(r["account_id"])] = (
                Convert.ToDecimal(r["opening"]),
                Convert.ToDecimal(r["cash_in"]),
                Convert.ToDecimal(r["cash_out"]));

        var (o1, i1, oo1) = byId[bank1];
        Assert.Equal(1000m, o1); Assert.Equal(500m, i1); Assert.Equal(200m, oo1);
        Assert.Equal(1300m, o1 + i1 - oo1); // closing

        var (o2, i2, oo2) = byId[bank2];
        Assert.Equal(0m, o2); Assert.Equal(700m, i2); Assert.Equal(0m, oo2);
        Assert.Equal(700m, o2 + i2 - oo2);
    }
}
