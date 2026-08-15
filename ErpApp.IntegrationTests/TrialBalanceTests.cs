using System.Data;
using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Trial Balance report integration test. It lives in its own collection (own
/// freshly-created scratch database) because it asserts ABSOLUTE totals across
/// every account — sibling tests posting their own vouchers would pollute them.
///
/// The scenario posts a deliberately balanced set of transactions (a sale, a
/// purchase, and a cash receipt) and then replicates exactly what
/// TrialBalanceForm.RunReport does: signed opening balance + in-period
/// debits − credits per account, zero-balance accounts skipped, and Debit
/// vs Credit totals that must be equal.
/// </summary>
[Collection("TrialBalance")]
public class TrialBalanceTests
{
    private static int AccountIdByCode(string code) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "SELECT account_id FROM chart_of_accounts WHERE account_code=@c",
        new Dictionary<string, object?> { ["c"] = code }));

    private static string NewNo(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];

    private static void PostEntry(NpgsqlConnection conn, NpgsqlTransaction tx, string no, string type, int account, decimal debit, decimal credit)
    {
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit)
            VALUES (@no, @vt, @acc, @part, @d, @c)", conn, tx);
        cmd.Parameters.AddWithValue("no", no);
        cmd.Parameters.AddWithValue("vt", type);
        cmd.Parameters.AddWithValue("acc", account);
        cmd.Parameters.AddWithValue("part", type);
        cmd.Parameters.AddWithValue("d", debit);
        cmd.Parameters.AddWithValue("c", credit);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Runs the exact TrialBalanceForm.RunReport query + computation and returns
    /// the per-account signed nets and the Debit/Credit totals the form displays.
    /// </summary>
    private static (Dictionary<int, decimal> Nets, decimal TotalDebit, decimal TotalCredit) RunReport()
    {
        var accounts = DbHelper.ExecuteQuery(@"
            SELECT a.account_id, a.account_code, a.account_name, a.account_type, a.balance_type, a.opening_balance,
                   COALESCE(SUM(CASE WHEN l.entry_date <= @asof THEN l.debit ELSE 0 END), 0) AS total_debit,
                   COALESCE(SUM(CASE WHEN l.entry_date <= @asof THEN l.credit ELSE 0 END), 0) AS total_credit
            FROM chart_of_accounts a
            LEFT JOIN ledger_entry l ON l.account_id = a.account_id
            WHERE a.active
            GROUP BY a.account_id, a.account_code, a.account_name, a.account_type, a.balance_type, a.opening_balance
            ORDER BY a.account_code NULLS LAST, a.account_name",
            new Dictionary<string, object?> { ["asof"] = DateTime.Today });

        var nets = new Dictionary<int, decimal>();
        decimal totalDebit = 0, totalCredit = 0;
        foreach (DataRow r in accounts.Rows)
        {
            int accountId = Convert.ToInt32(r["account_id"]);
            decimal opening = Convert.ToDecimal(r["opening_balance"]);
            bool isDr = (r["balance_type"].ToString() ?? "Dr") == "Dr";
            decimal signedOpening = isDr ? opening : -opening;

            decimal debitSum = Convert.ToDecimal(r["total_debit"]);
            decimal creditSum = Convert.ToDecimal(r["total_credit"]);
            decimal net = signedOpening + debitSum - creditSum;

            if (net == 0) continue; // the form skips zero-balance accounts

            nets[accountId] = net;
            if (net > 0) totalDebit += net; else totalCredit += Math.Abs(net);
        }
        return (nets, totalDebit, totalCredit);
    }

    [Fact]
    public void TrialBalance_ShowsPerAccountNets_AndBalances()
    {
        DbHelper.ExecuteNonQuery("DELETE FROM ledger_entry");
        DbHelper.ExecuteNonQuery("UPDATE chart_of_accounts SET opening_balance = 0"); // this collection owns its DB — clean slate
        int ar = AccountIdByCode("1100");      // Accounts Receivable, Dr
        int sales = AccountIdByCode("4000");   // Sales, Cr
        int purchases = AccountIdByCode("5000"); // Purchases, Dr
        int cash = AccountIdByCode("1000");    // Cash in Hand, Dr

        // an AP account + supplier (LIABILITY, Cr)
        int ap = Convert.ToInt32(DbHelper.ExecuteScalar(
            "INSERT INTO chart_of_accounts (account_code, account_name, account_type, balance_type) " +
            "VALUES (@code, 'Trial AP', 'LIABILITY', 'Cr') RETURNING account_id",
            new Dictionary<string, object?> { ["code"] = "AP" + Guid.NewGuid().ToString("N")[..8] }));

        // balanced books: sale 1000, purchase 500, cash receipt 300
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            // sale on credit: AR debit 1000 / Sales credit 1000
            PostEntry(conn, tx, NewNo("IT"), "Sales Invoice", ar, 1000m, 0m);
            PostEntry(conn, tx, NewNo("IT"), "Sales Invoice", sales, 0m, 1000m);

            // purchase on credit: Purchases debit 500 / AP credit 500
            PostEntry(conn, tx, NewNo("PUR"), "Purchase Bill", purchases, 500m, 0m);
            PostEntry(conn, tx, NewNo("PUR"), "Purchase Bill", ap, 0m, 500m);

            // cash receipt from the customer: Cash debit 300 / AR credit 300
            PostEntry(conn, tx, NewNo("RCPT"), "Receipt", cash, 300m, 0m);
            PostEntry(conn, tx, NewNo("RCPT"), "Receipt", ar, 0m, 300m);
        });

        var (nets, totalDebit, totalCredit) = RunReport();

        // per-account signed nets (Dr positive / Cr negative), as the grid shows
        Assert.Equal(700m, nets[ar]);        // 1000 − 300
        Assert.Equal(-1000m, nets[sales]);   // 1000 credit
        Assert.Equal(500m, nets[purchases]);
        Assert.Equal(300m, nets[cash]);
        Assert.Equal(-500m, nets[ap]);       // 500 credit

        // the seeded generic Bank Account (1001) has no activity → skipped, like a real TB
        Assert.False(nets.ContainsKey(AccountIdByCode("1001")));

        // books balance
        Assert.Equal(1500m, totalDebit);
        Assert.Equal(1500m, totalCredit);
        Assert.Equal(0m, Math.Round(totalDebit - totalCredit, 2));
    }

    [Fact]
    public void TrialBalance_IncludesOpeningBalancesSignedByBalanceType()
    {
        DbHelper.ExecuteNonQuery("DELETE FROM ledger_entry");
        DbHelper.ExecuteNonQuery("UPDATE chart_of_accounts SET opening_balance = 0"); // this collection owns its DB — clean slate
        int ar = AccountIdByCode("1100");
        int cash = AccountIdByCode("1000");

        // opening balances: AR owes us 2000 (Dr), cash holds 500 (Dr)
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var upd = new NpgsqlCommand("UPDATE chart_of_accounts SET opening_balance=@amt WHERE account_id=@id", conn, tx);
            upd.Parameters.AddWithValue("amt", 2000m);
            upd.Parameters.AddWithValue("id", ar);
            upd.ExecuteNonQuery();
        });
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var upd = new NpgsqlCommand("UPDATE chart_of_accounts SET opening_balance=@amt WHERE account_id=@id", conn, tx);
            upd.Parameters.AddWithValue("amt", 500m);
            upd.Parameters.AddWithValue("id", cash);
            upd.ExecuteNonQuery();
        });

        var (nets, totalDebit, totalCredit) = RunReport();

        Assert.Equal(2000m, nets[ar]);   // Dr opening → debit side
        Assert.Equal(500m, nets[cash]);  // Dr opening → debit side
        Assert.Equal(2500m, totalDebit);
        Assert.Equal(0m, totalCredit);
        // with only Dr openings there is no Cr side — the report's balance check
        // correctly reports the books out of balance by exactly the missing amount
        Assert.Equal(2500m, Math.Round(Math.Abs(totalDebit - totalCredit), 2));
    }
}

[CollectionDefinition("TrialBalance")]
public class TrialBalanceCollection : ICollectionFixture<DatabaseFixture> { }
