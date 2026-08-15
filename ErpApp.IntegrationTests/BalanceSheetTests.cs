using System.Data;
using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Balance Sheet report integration tests. They live in their own collection
/// (own freshly-created scratch database) because the report asserts ABSOLUTE
/// totals across all accounts — sibling tests posting their own vouchers would
/// pollute them.
///
/// The scenario posts a balanced set of transactions plus opening balances and
/// replicates exactly what BalanceSheetForm.RunReport does: ASSET/BANK accounts
/// on the asset side (signed Dr balance), LIABILITY and EQUITY flipped to
/// positive, cumulative Income − Expense folded into equity as Current Earnings,
/// and the Assets = Liabilities + Equity check.
/// </summary>
[Collection("BalanceSheet")]
public class BalanceSheetTests
{
    private static int AccountIdByCode(string code) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "SELECT account_id FROM chart_of_accounts WHERE account_code=@c",
        new Dictionary<string, object?> { ["c"] = code }));

    private static int CreateAccount(string name, string type, string balanceType) =>
        Convert.ToInt32(DbHelper.ExecuteScalar(
            "INSERT INTO chart_of_accounts (account_code, account_name, account_type, balance_type) " +
            "VALUES (@code, @name, @type, @bt) RETURNING account_id",
            new Dictionary<string, object?>
            {
                ["code"] = type[..3] + Guid.NewGuid().ToString("N")[..8],
                ["name"] = name,
                ["type"] = type,
                ["bt"] = balanceType
            }));

    private static void SetOpening(int account, decimal amount)
    {
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var upd = new NpgsqlCommand("UPDATE chart_of_accounts SET opening_balance=@amt WHERE account_id=@id", conn, tx);
            upd.Parameters.AddWithValue("amt", amount);
            upd.Parameters.AddWithValue("id", account);
            upd.ExecuteNonQuery();
        });
    }

    private static void PostEntry(int account, decimal debit, decimal credit, DateTime? date = null)
    {
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, @vt, @acc, @part, @d, @c, @dt)", conn, tx);
            cmd.Parameters.AddWithValue("no", "BS-" + Guid.NewGuid().ToString("N")[..8]);
            cmd.Parameters.AddWithValue("vt", "Test");
            cmd.Parameters.AddWithValue("acc", account);
            cmd.Parameters.AddWithValue("part", "Test");
            cmd.Parameters.AddWithValue("d", debit);
            cmd.Parameters.AddWithValue("c", credit);
            cmd.Parameters.AddWithValue("dt", date ?? DateTime.Today);
            cmd.ExecuteNonQuery();
        });
    }

    /// <summary>
    /// Runs the exact BalanceSheetForm.RunReport queries + computation and returns
    /// the group totals and the balance check the form displays.
    /// </summary>
    private static (decimal Assets, decimal Liabilities, decimal Equity, decimal CurrentEarnings, bool Balanced) RunReport()
    {
        var accounts = DbHelper.ExecuteQuery(@"
            SELECT a.account_name, a.account_type, a.balance_type, a.opening_balance,
                   COALESCE(SUM(CASE WHEN l.entry_date <= @asof THEN l.debit ELSE 0 END), 0) AS total_debit,
                   COALESCE(SUM(CASE WHEN l.entry_date <= @asof THEN l.credit ELSE 0 END), 0) AS total_credit
            FROM chart_of_accounts a
            LEFT JOIN ledger_entry l ON l.account_id = a.account_id
            WHERE a.active AND a.account_type IN ('ASSET','BANK','LIABILITY','EQUITY')
            GROUP BY a.account_id, a.account_name, a.account_type, a.balance_type, a.opening_balance
            ORDER BY a.account_name",
            new Dictionary<string, object?> { ["asof"] = DateTime.Today });

        decimal SignedBalance(DataRow r)
        {
            decimal opening = Convert.ToDecimal(r["opening_balance"]);
            bool isDr = (r["balance_type"].ToString() ?? "Dr") == "Dr";
            decimal signedOpening = isDr ? opening : -opening;
            decimal debitSum = Convert.ToDecimal(r["total_debit"]);
            decimal creditSum = Convert.ToDecimal(r["total_credit"]);
            return signedOpening + debitSum - creditSum;
        }

        decimal totalAssets = 0, totalLiabilities = 0, totalEquity = 0;
        foreach (DataRow r in accounts.Rows)
        {
            string type = r["account_type"].ToString() ?? "";
            decimal bal = SignedBalance(r);
            if (type is "ASSET" or "BANK") { if (bal != 0) totalAssets += bal; }
            else if (type == "LIABILITY") { if (bal != 0) totalLiabilities += -bal; }
            else if (type == "EQUITY") { if (bal != 0) totalEquity += -bal; }
        }

        var plResult = DbHelper.ExecuteQuery(@"
            SELECT
              COALESCE(SUM(CASE WHEN a.account_type='INCOME' THEN l.credit - l.debit ELSE 0 END),0) -
              COALESCE(SUM(CASE WHEN a.account_type='EXPENSE' THEN l.debit - l.credit ELSE 0 END),0) AS net_earnings
            FROM ledger_entry l JOIN chart_of_accounts a ON a.account_id = l.account_id
            WHERE l.entry_date <= @asof AND a.account_type IN ('INCOME','EXPENSE')",
            new Dictionary<string, object?> { ["asof"] = DateTime.Today });
        decimal currentEarnings = plResult.Rows.Count > 0 && plResult.Rows[0]["net_earnings"] != DBNull.Value
            ? Convert.ToDecimal(plResult.Rows[0]["net_earnings"]) : 0;
        totalEquity += currentEarnings;

        decimal diff = totalAssets - (totalLiabilities + totalEquity);
        return (totalAssets, totalLiabilities, totalEquity, currentEarnings, Math.Round(diff, 2) == 0);
    }

    /// <summary>Wipes the ledger and opening balances — this collection owns its DB, clean slate per test.</summary>
    private static void ResetBooks()
    {
        DbHelper.ExecuteNonQuery("DELETE FROM ledger_entry");
        DbHelper.ExecuteNonQuery("UPDATE chart_of_accounts SET opening_balance = 0");
    }

    [Fact]
    public void BalanceSheet_GroupsAssetsLiabilitiesEquity_AndBalances()
    {
        ResetBooks();
        int cash = AccountIdByCode("1000");
        int ar = AccountIdByCode("1100");
        int ap = AccountIdByCode("2100");
        int sales = AccountIdByCode("4000");
        int purchases = AccountIdByCode("5000");
        int capital = CreateAccount("Opening Capital", "EQUITY", "Cr");

        // opening: 10000 cash invested as capital (Dr cash / Cr capital)
        SetOpening(cash, 10000m);
        SetOpening(capital, 10000m);

        // sale on credit 2000, purchase on credit 500, cash receipt 300
        PostEntry(ar, 2000m, 0m);
        PostEntry(sales, 0m, 2000m);
        PostEntry(purchases, 500m, 0m);
        PostEntry(ap, 0m, 500m);
        PostEntry(cash, 300m, 0m);
        PostEntry(ar, 0m, 300m);

        var (assets, liabilities, equity, earnings, balanced) = RunReport();

        // ASSETS: cash 10300 (10000 opening + 300) + AR 1700 (2000 − 300)
        Assert.Equal(12000m, assets);
        // LIABILITIES: AP 500 (Cr, flipped positive)
        Assert.Equal(500m, liabilities);
        // EQUITY: capital 10000 + Current Earnings 1500 (2000 sales − 500 purchases)
        Assert.Equal(1500m, earnings);
        Assert.Equal(11500m, equity);
        Assert.Equal(12000m, liabilities + equity);
        Assert.True(balanced); // Assets = Liabilities + Equity
    }

    [Fact]
    public void BalanceSheet_TreatsBankAccountsAsAssets()
    {
        ResetBooks();
        int cash = AccountIdByCode("1000");
        int bank = AccountIdByCode("1001");
        int capital = CreateAccount("Capital with Bank", "EQUITY", "Cr");

        // opening: cash 2000 + bank 2500, financed by capital 4500 (Cr)
        SetOpening(cash, 2000m);
        SetOpening(bank, 2500m);
        SetOpening(capital, 4500m);

        var (assets, liabilities, equity, _, balanced) = RunReport();

        // the BANK account's 2500 lands on the asset side (the IN ('ASSET','BANK') filter)
        Assert.Equal(4500m, assets);
        Assert.Equal(0m, liabilities);
        Assert.Equal(4500m, equity);
        Assert.True(balanced);
    }

    [Fact]
    public void BalanceSheet_IgnoresEntriesAfterTheAsOfDate()
    {
        ResetBooks();
        int cash = AccountIdByCode("1000");
        int ap = AccountIdByCode("2100");
        int capital = CreateAccount("Capital AsOf", "EQUITY", "Cr");

        SetOpening(cash, 1000m);
        SetOpening(capital, 1000m);

        var before = RunReport();
        Assert.Equal(1000m, before.Assets);
        Assert.True(before.Balanced);

        // a balanced transaction dated AFTER the as-of date must not move the sheet
        PostEntry(cash, 500m, 0m, DateTime.Today.AddDays(1));
        PostEntry(ap, 0m, 500m, DateTime.Today.AddDays(1));

        var after = RunReport();
        Assert.Equal(before.Assets, after.Assets);
        Assert.Equal(before.Liabilities, after.Liabilities);
        Assert.Equal(before.Equity, after.Equity);
        Assert.True(after.Balanced);
    }
}

[CollectionDefinition("BalanceSheet")]
public class BalanceSheetCollection : ICollectionFixture<DatabaseFixture> { }
