using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Cash Flow Statement integration test. It lives in its own collection (and
/// therefore gets its own freshly-created scratch database) because it asserts
/// ABSOLUTE sums across every cash/bank account — sibling tests posting their
/// own receipts/payments would pollute the totals otherwise.
/// </summary>
[Collection("CashFlow")]
public class CashFlowTests
{
    private static int AccountIdByCode(string code) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "SELECT account_id FROM chart_of_accounts WHERE account_code=@c",
        new Dictionary<string, object?> { ["c"] = code }));

    private static int CreateBank(string name) => Convert.ToInt32(DbHelper.ExecuteScalar(
        "INSERT INTO chart_of_accounts (account_code, account_name, account_type, balance_type) " +
        "VALUES (@code, @name, 'BANK', 'Dr') RETURNING account_id",
        new Dictionary<string, object?> { ["code"] = "B" + Guid.NewGuid().ToString("N")[..8], ["name"] = name }));

    [Fact]
    public void CashFlowStatement_SeesMoneyInAnyBankAccount()
    {
        int cash = AccountIdByCode("1000");
        int bank2 = CreateBank("CF HBL " + Guid.NewGuid().ToString("N")[..8]);
        DateTime from = DateTime.Today.AddDays(-30), to = DateTime.Today;

        // opening (before From) at the new bank; in-period receipts at the new bank + cash
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var opening = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, 'Receipt', @acc, 'Opening', 1000, 0, @d)", conn, tx);
            opening.Parameters.AddWithValue("no", "CF-" + Guid.NewGuid().ToString("N")[..8]);
            opening.Parameters.AddWithValue("acc", bank2);
            opening.Parameters.AddWithValue("d", from.AddDays(-10));
            opening.ExecuteNonQuery();

            using var in1 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, 'Receipt', @acc, 'In', 500, 0, @d)", conn, tx);
            in1.Parameters.AddWithValue("no", "CF-" + Guid.NewGuid().ToString("N")[..8]);
            in1.Parameters.AddWithValue("acc", bank2);
            in1.Parameters.AddWithValue("d", from.AddDays(5));
            in1.ExecuteNonQuery();

            using var in2 = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, 'Receipt', @acc, 'In', 300, 0, @d)", conn, tx);
            in2.Parameters.AddWithValue("no", "CF-" + Guid.NewGuid().ToString("N")[..8]);
            in2.Parameters.AddWithValue("acc", cash);
            in2.Parameters.AddWithValue("d", from.AddDays(5));
            in2.ExecuteNonQuery();
        });

        var pars = new Dictionary<string, object?> { ["from"] = from, ["to"] = to };

        // opening query (CashFlowStatementForm)
        decimal opening = Convert.ToDecimal(DbHelper.ExecuteScalar(@"
            SELECT COALESCE(SUM(l.debit - l.credit), 0)
            FROM ledger_entry l
            JOIN chart_of_accounts a ON a.account_id = l.account_id
            WHERE (a.account_code = '1000' OR a.account_type = 'BANK') AND l.entry_date < @from", pars));
        Assert.Equal(1000m, opening);

        // by-type query (CashFlowStatementForm)
        decimal cashIn = Convert.ToDecimal(DbHelper.ExecuteScalar(@"
            SELECT COALESCE(SUM(l.debit),0)
            FROM ledger_entry l
            JOIN chart_of_accounts a ON a.account_id = l.account_id
            WHERE (a.account_code = '1000' OR a.account_type = 'BANK')
              AND l.entry_date BETWEEN @from AND @to AND l.voucher_type='Receipt'", pars));
        Assert.Equal(800m, cashIn);
    }
}

[CollectionDefinition("CashFlow")]
public class CashFlowCollection : ICollectionFixture<DatabaseFixture> { }
