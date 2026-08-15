using System.Data;
using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// General Ledger (Accounting &gt; Ledger) integration tests against the scratch
/// database. The ledger's queries are all account-scoped (filtered by
/// account_id), so the tests are immune to sibling tests sharing the
/// collection's database — each test uses its own dedicated account.
///
/// Replicates exactly what LedgerForm.LoadLedger does: the opening balance as
/// the account's cumulative balance before the From date (signed COA opening
/// balance + all pre-period entries — the same signed-opening logic Trial
/// Balance / Balance Sheet use), the in-period movement grid, and the running
/// balance with its Dr/Cr suffix. Also covers the account-picker union of
/// chart accounts + customer/supplier sub-ledgers.
/// </summary>
[Collection("Database")]
public class LedgerTests
{
    private static int CreateAccount(string name, string type, string balanceType, decimal opening) =>
        Convert.ToInt32(DbHelper.ExecuteScalar(@"
            INSERT INTO chart_of_accounts (account_code, account_name, account_type, balance_type, opening_balance)
            VALUES (@code, @name, @type, @bt, @opening) RETURNING account_id",
            new Dictionary<string, object?>
            {
                ["code"] = "L" + Guid.NewGuid().ToString("N")[..8],
                ["name"] = name,
                ["type"] = type,
                ["bt"] = balanceType,
                ["opening"] = opening
            }));

    /// <summary>What CustomerMasterForm does: a dedicated receivable account + the customer row.</summary>
    private static int CreateCustomer(string name, decimal opening)
    {
        int accountId = CreateAccount(name, "ASSET", "Dr", opening);
        DbHelper.ExecuteNonQuery(@"
            INSERT INTO customer_master (customer_name, address, mobile, opening_balance, account_id, active)
            VALUES (@n, 'Test address', '123', @opening, @acc, TRUE)",
            new Dictionary<string, object?> { ["n"] = name, ["opening"] = opening, ["acc"] = accountId });
        return accountId;
    }

    /// <summary>What SupplierMasterForm does: a dedicated payable account + the supplier row.</summary>
    private static int CreateSupplier(string name, decimal opening)
    {
        int accountId = CreateAccount(name, "LIABILITY", "Cr", opening);
        DbHelper.ExecuteNonQuery(@"
            INSERT INTO supplier_master (supplier_name, address, mobile, opening_balance, account_id, active)
            VALUES (@n, 'Test address', '123', @opening, @acc, TRUE)",
            new Dictionary<string, object?> { ["n"] = name, ["opening"] = opening, ["acc"] = accountId });
        return accountId;
    }

    private static void PostEntry(int account, decimal debit, decimal credit, DateTime date)
    {
        DbHelper.ExecuteTransaction((conn, tx) =>
        {
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, entry_date)
                VALUES (@no, @vt, @acc, @part, @d, @c, @dt)", conn, tx);
            cmd.Parameters.AddWithValue("no", "LDG-" + Guid.NewGuid().ToString("N")[..8]);
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
    /// Runs the exact LedgerForm.LoadLedger queries + computation and returns the
    /// opening balance, the per-row running balances (formatted Dr/Cr), and the
    /// final current balance.
    /// </summary>
    private static (decimal Opening, List<string> RowBalances, string Current) RunLedger(int accountId, DateTime from, DateTime to)
    {
        decimal opening = Convert.ToDecimal(DbHelper.ExecuteScalar(@"
            SELECT CASE WHEN balance_type='Dr' THEN opening_balance ELSE -opening_balance END
                   + COALESCE((SELECT SUM(debit - credit) FROM ledger_entry
                               WHERE account_id=@id AND entry_date < @from), 0) AS opening
            FROM chart_of_accounts WHERE account_id=@id",
            new Dictionary<string, object?> { ["id"] = accountId, ["from"] = from }));

        var table = DbHelper.ExecuteQuery(@"
            SELECT entry_date, voucher_no, voucher_type, particulars, debit, credit
            FROM ledger_entry
            WHERE account_id=@id AND entry_date BETWEEN @from AND @to
            ORDER BY entry_date, entry_id",
            new Dictionary<string, object?> { ["id"] = accountId, ["from"] = from, ["to"] = to });

        decimal running = opening;
        var balances = new List<string>();
        foreach (DataRow row in table.Rows)
        {
            decimal debit = Convert.ToDecimal(row["debit"]);
            decimal credit = Convert.ToDecimal(row["credit"]);
            running += debit - credit;
            balances.Add(Math.Abs(running).ToString("N2") + (running >= 0 ? " Dr" : " Cr"));
        }

        return (opening, balances, Math.Abs(running).ToString("N2") + (running >= 0 ? " Dr" : " Cr"));
    }

    [Fact]
    public void Ledger_Opening_IsSignedCoaOpeningPlusPrePeriodEntries()
    {
        string name = "LedgerCust " + Guid.NewGuid().ToString("N")[..8];
        int account = CreateCustomer(name, opening: 500m);
        DateTime from = DateTime.Today.AddDays(-30), to = DateTime.Today;

        // 200 debited before the period → folds into opening; 100 in / 50 out in period
        PostEntry(account, 200m, 0m, from.AddDays(-10));
        PostEntry(account, 100m, 0m, DateTime.Today);
        PostEntry(account, 0m, 50m, DateTime.Today);

        var (opening, balances, current) = RunLedger(account, from, to);

        Assert.Equal(700m, opening);       // 500 customer opening + 200 pre-period
        Assert.Equal(new[] { "800.00 Dr", "750.00 Dr" }, balances);
        Assert.Equal("750.00 Dr", current);
    }

    [Fact]
    public void Ledger_CreditAccount_ShowsCrOpeningAndCurrent()
    {
        string name = "LedgerSup " + Guid.NewGuid().ToString("N")[..8];
        int account = CreateSupplier(name, opening: 300m);
        DateTime from = DateTime.Today.AddDays(-30), to = DateTime.Today;

        PostEntry(account, 0m, 100m, DateTime.Today); // we owe more

        var (opening, balances, current) = RunLedger(account, from, to);

        Assert.Equal(-300m, opening);      // Cr opening is negative internally
        Assert.Equal(new[] { "400.00 Cr" }, balances);
        Assert.Equal("400.00 Cr", current);
    }

    [Fact]
    public void Ledger_PlainCoaAccount_UsesItsOwnOpeningBalance()
    {
        // a non-customer account's COA opening must show up (previously it always showed 0.00)
        int account = CreateAccount("LedgerPlain " + Guid.NewGuid().ToString("N")[..8], "ASSET", "Dr", opening: 500m);
        DateTime from = DateTime.Today.AddDays(-30), to = DateTime.Today;

        var (opening, balances, current) = RunLedger(account, from, to);

        Assert.Equal(500m, opening);
        Assert.Empty(balances);
        Assert.Equal("500.00 Dr", current);
    }

    [Fact]
    public void Ledger_AccountUnion_IncludesCustomerAndSupplierSubLedgers()
    {
        string custName = "LedgerUnionCust " + Guid.NewGuid().ToString("N")[..8];
        string supName = "LedgerUnionSup " + Guid.NewGuid().ToString("N")[..8];
        CreateCustomer(custName, 0m);
        CreateSupplier(supName, 0m);

        // the exact account-picker union query
        var rows = DbHelper.ExecuteQuery(@"
            SELECT account_id, account_name FROM chart_of_accounts
            UNION
            SELECT c.account_id, c.customer_name FROM customer_master c WHERE c.account_id IS NOT NULL
            UNION
            SELECT s.account_id, s.supplier_name FROM supplier_master s WHERE s.account_id IS NOT NULL
            ORDER BY account_name");

        var names = rows.Rows.Cast<DataRow>().Select(r => r["account_name"].ToString()).ToHashSet();
        Assert.Contains(custName, names); // customer sub-ledger in the picker
        Assert.Contains(supName, names);  // supplier sub-ledger in the picker
        Assert.Contains("Cash in Hand", names); // plain chart accounts too
    }
}
