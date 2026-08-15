using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class SupplierMasterForm : SimpleMasterFormBase
{
    private readonly TextBox txtName = new();
    private readonly TextBox txtAddress = new();
    private readonly TextBox txtMobile = new();
    private readonly TextBox txtEmail = new();
    private readonly NumericUpDown numCreditLimit = new() { DecimalPlaces = 2, Maximum = 1_000_000_000 };
    private readonly NumericUpDown numOpeningBalance = new() { DecimalPlaces = 2, Maximum = 1_000_000_000, Minimum = -1_000_000_000 };
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    public SupplierMasterForm() : base("Supplier Master")
    {
        AddRow(FieldsPanel, 0, "Supplier Name", txtName, "Mobile", txtMobile);
        AddRow(FieldsPanel, 1, "Address", txtAddress, "Email", txtEmail);
        AddRow(FieldsPanel, 2, "Credit Limit", numCreditLimit, "Opening Balance", numOpeningBalance);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 3, 3);

        InitializeLayout("Supplier Information", "Supplier List");
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT supplier_id AS ""ID"", supplier_name AS ""Supplier Name"", mobile AS ""Mobile"",
               email AS ""Email"", address AS ""Address"", credit_limit AS ""Credit Limit"",
               opening_balance AS ""Opening Balance"", active AS ""Active""
        FROM supplier_master ORDER BY supplier_name");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM supplier_master WHERE supplier_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtName.Text = r["supplier_name"].ToString();
        txtAddress.Text = r["address"]?.ToString();
        txtMobile.Text = r["mobile"]?.ToString();
        txtEmail.Text = r["email"]?.ToString();
        numCreditLimit.Value = r["credit_limit"] is DBNull ? 0 : Convert.ToDecimal(r["credit_limit"]);
        numOpeningBalance.Value = r["opening_balance"] is DBNull ? 0 : Convert.ToDecimal(r["opening_balance"]);
        chkActive.Checked = r["active"] is bool b && b;
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("Supplier Name is required."); return false; }
        var pars = new Dictionary<string, object?>
        {
            ["name"] = txtName.Text.Trim(),
            ["addr"] = txtAddress.Text.Trim(),
            ["mobile"] = txtMobile.Text.Trim(),
            ["email"] = txtEmail.Text.Trim(),
            ["credit"] = numCreditLimit.Value,
            ["opening"] = numOpeningBalance.Value,
            ["active"] = chkActive.Checked
        };

        if (EditingId == null)
        {
            // Auto-create a payable sub-account so it shows up in the Ledger too
            int accountId = Convert.ToInt32(DbHelper.ExecuteScalar(@"
                INSERT INTO chart_of_accounts (account_name, account_type, balance_type, opening_balance)
                VALUES (@name, 'LIABILITY', 'Cr', @opening) RETURNING account_id",
                new Dictionary<string, object?> { ["name"] = txtName.Text.Trim(), ["opening"] = numOpeningBalance.Value }));
            pars["acc"] = accountId;

            DbHelper.ExecuteNonQuery(@"
                INSERT INTO supplier_master (supplier_name, address, mobile, email, credit_limit, opening_balance, account_id, active)
                VALUES (@name, @addr, @mobile, @email, @credit, @opening, @acc, @active)", pars);
        }
        else
        {
            pars["id"] = EditingId;
            DbHelper.ExecuteNonQuery(@"
                UPDATE supplier_master SET supplier_name=@name, address=@addr, mobile=@mobile, email=@email,
                       credit_limit=@credit, opening_balance=@opening, active=@active
                WHERE supplier_id=@id", pars);
            DbHelper.ExecuteNonQuery(@"
                UPDATE chart_of_accounts SET account_name=@name, opening_balance=@opening
                WHERE account_id = (SELECT account_id FROM supplier_master WHERE supplier_id=@id)", pars);
        }
        return true;
    }

    protected override void DeleteRecord(int id) =>
        DbHelper.ExecuteNonQuery("DELETE FROM supplier_master WHERE supplier_id=@id", new() { ["id"] = id });

    protected override void ResetFields()
    {
        txtName.Clear(); txtAddress.Clear(); txtMobile.Clear(); txtEmail.Clear();
        numCreditLimit.Value = 0; numOpeningBalance.Value = 0; chkActive.Checked = true;
    }
}
