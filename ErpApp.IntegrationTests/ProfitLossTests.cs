using System.Data;
using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Profit &amp; Loss (Income Statement) integration tests. They live in their own
/// collection (own freshly-created scratch database) because the report asserts
/// ABSOLUTE totals across every INCOME/EXPENSE account — sibling tests posting
/// their own vouchers would pollute them.
///
/// Replicates exactly what ProfitLossForm.RunReport does: INCOME accounts
/// aggregated as SUM(credit) − SUM(debit), EXPENSE accounts as SUM(debit) −
/// SUM(credit), both only for ledger entries in the chosen date range, and the
/// Net Profit / Net Loss result.
/// </summary>
[Collection("ProfitLoss")]
public class ProfitLossTests
{
    private static int AccountIdByCode(string code) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "SELECT account_id FROM chart_of_accounts WHERE account_code=@c",
        new Dictionary<string, object?> { ["c"] = code }));

    private static void PostEntry(int account, decimal debit, decimal credit, DateTime date)
    {
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, @vt, @acc, @part, @d, @c, @dt)", conn, tx);
            cmd.Parameters.AddWithValue("no", "PL-" + Guid.NewGuid().ToString("N")[..8]);
            cmd.Parameters.AddWithValue("vt", "Test");
            cmd.Parameters.AddWithValue("acc", account);
            cmd.Parameters.AddWithValue("part", "Test");
            cmd.Parameters.AddWithValue("d", debit);
            cmd.Parameters.AddWithValue("c", credit);
            cmd.Parameters.AddWithValue("dt", date);
            cmd.ExecuteNonQuery();
        });
    }

    /// <summary>
    /// Runs the exact ProfitLossForm.RunReport queries and returns the per-account
    /// amounts plus the net result and its sign (positive = profit).
    /// </summary>
    private static (Dictionary<string, decimal> Income, Dictionary<string, decimal> Expenses, decimal Net, bool IsProfit) RunReport(DateTime from, DateTime to)
    {
        var pars = new Dictionary<string, object?> { ["from"] = from, ["to"] = to };

        var incomeRows = DbHelper.ExecuteQuery(@"
            SELECT a.account_name,
                   COALESCE(SUM(l.credit),0) - COALESCE(SUM(l.debit),0) AS amount
            FROM chart_of_accounts a
            JOIN ledger_entry l ON l.account_id = a.account_id AND l.entry_date BETWEEN @from AND @to
            WHERE a.account_type='INCOME'
            GROUP BY a.account_name HAVING COALESCE(SUM(l.credit),0) - COALESCE(SUM(l.debit),0) <> 0
            ORDER BY a.account_name", pars);

        var expenseRows = DbHelper.ExecuteQuery(@"
            SELECT a.account_name,
                   COALESCE(SUM(l.debit),0) - COALESCE(SUM(l.credit),0) AS amount
            FROM chart_of_accounts a
            JOIN ledger_entry l ON l.account_id = a.account_id AND l.entry_date BETWEEN @from AND @to
            WHERE a.account_type='EXPENSE'
            GROUP BY a.account_name HAVING COALESCE(SUM(l.debit),0) - COALESCE(SUM(l.credit),0) <> 0
            ORDER BY a.account_name", pars);

        var income = new Dictionary<string, decimal>();
        decimal totalIncome = 0;
        foreach (DataRow r in incomeRows.Rows)
        {
            decimal amt = Convert.ToDecimal(r["amount"]);
            income[r["account_name"].ToString()!] = amt;
            totalIncome += amt;
        }

        var expenses = new Dictionary<string, decimal>();
        decimal totalExpense = 0;
        foreach (DataRow r in expenseRows.Rows)
        {
            decimal amt = Convert.ToDecimal(r["amount"]);
            expenses[r["account_name"].ToString()!] = amt;
            totalExpense += amt;
        }

        decimal net = totalIncome - totalExpense;
        return (income, expenses, net, net >= 0);
    }

    [Fact]
    public void ProfitLoss_ComputesIncomeExpense_AndNetProfit()
    {
        DbHelper.ExecuteNonQuery("DELETE FROM ledger_entry"); // this collection owns its DB — clean slate
        int sales = AccountIdByCode("4000");
        int purchases = AccountIdByCode("5000");
        DateTime from = DateTime.Today.AddDays(-10), to = DateTime.Today;

        // income: sales 2000 − a sales return 200 → 1800
        PostEntry(sales, 0m, 2000m, DateTime.Today);
        PostEntry(sales, 200m, 0m, DateTime.Today);
        // expense: purchases 500 − a purchase return 100 → 400
        PostEntry(purchases, 500m, 0m, DateTime.Today);
        PostEntry(purchases, 0m, 100m, DateTime.Today);

        var (income, expenses, net, isProfit) = RunReport(from, to);

        Assert.Equal(1800m, income["Sales"]);
        Assert.Equal(400m, expenses["Purchases"]);
        Assert.Equal(1400m, net);   // 1800 − 400
        Assert.True(isProfit);      // "Net Profit: 1,400.00"
    }

    [Fact]
    public void ProfitLoss_HonorsTheDateRange()
    {
        DbHelper.ExecuteNonQuery("DELETE FROM ledger_entry");
        int sales = AccountIdByCode("4000");
        int purchases = AccountIdByCode("5000");
        DateTime from = DateTime.Today.AddDays(-10), to = DateTime.Today;

        // entries OUTSIDE the range must be ignored: sales 1000 before From, purchases 300 after To
        PostEntry(sales, 0m, 1000m, from.AddDays(-5));
        PostEntry(purchases, 300m, 0m, to.AddDays(1));

        var (income, expenses, net, isProfit) = RunReport(from, to);

        Assert.Empty(income);   // no in-period income rows
        Assert.Empty(expenses); // no in-period expense rows
        Assert.Equal(0m, net);  // report shows 0 income / 0 expenses
        Assert.True(isProfit);  // net 0 renders as "Net Profit: 0.00" (net >= 0)
    }

    [Fact]
    public void ProfitLoss_ReportsNetLossWhenExpensesExceedIncome()
    {
        DbHelper.ExecuteNonQuery("DELETE FROM ledger_entry");
        int sales = AccountIdByCode("4000");
        int purchases = AccountIdByCode("5000");
        DateTime from = DateTime.Today.AddDays(-10), to = DateTime.Today;

        PostEntry(sales, 0m, 100m, DateTime.Today);
        PostEntry(purchases, 500m, 0m, DateTime.Today);

        var (income, expenses, net, isProfit) = RunReport(from, to);

        Assert.Equal(100m, income["Sales"]);
        Assert.Equal(500m, expenses["Purchases"]);
        Assert.Equal(-400m, net);
        Assert.False(isProfit); // "Net Loss: 400.00"
    }
}

[CollectionDefinition("ProfitLoss")]
public class ProfitLossCollection : ICollectionFixture<DatabaseFixture> { }
