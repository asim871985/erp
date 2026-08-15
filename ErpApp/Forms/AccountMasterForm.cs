using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class AccountMasterForm : SimpleMasterFormBase
{
    private readonly TextBox txtCode = new();
    private readonly TextBox txtName = new();
    private readonly ComboBox cboType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboParent = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboBalanceType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown numOpeningBalance = new() { DecimalPlaces = 2, Maximum = 1_000_000_000, Minimum = -1_000_000_000 };
    private readonly TextBox txtDescription = new();
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    public AccountMasterForm() : base("Account Master")
    {
        AddRow(FieldsPanel, 0, "Account Code", txtCode, "Account Name", txtName);
        AddRow(FieldsPanel, 1, "Account Type", cboType, "Parent Account", cboParent);
        AddRow(FieldsPanel, 2, "Opening Balance", numOpeningBalance, "Balance Type", cboBalanceType);
        AddRow(FieldsPanel, 3, "Description", txtDescription);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 3, 3);

        cboType.Items.AddRange(new object[] { "ASSET", "BANK", "LIABILITY", "EQUITY", "INCOME", "EXPENSE" });
        cboBalanceType.Items.AddRange(new object[] { "Dr", "Cr" });

        LoadParents();
        InitializeLayout("Account Information", "Chart of Accounts");
    }

    private void LoadParents()
    {
        try
        {
            var table = DbHelper.ExecuteQuery("SELECT account_id, account_name FROM chart_of_accounts ORDER BY account_name");
            var withBlank = table.Clone();
            var blankRow = withBlank.NewRow();
            blankRow["account_id"] = DBNull.Value;
            blankRow["account_name"] = "(none)";
            withBlank.Rows.Add(blankRow);
            foreach (DataRow r in table.Rows) withBlank.ImportRow(r);

            cboParent.DisplayMember = "account_name";
            cboParent.ValueMember = "account_id";
            cboParent.DataSource = withBlank;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load parent accounts: " + ex.Message);
        }
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT a.account_id AS ""ID"", a.account_code AS ""Code"", a.account_name AS ""Account Name"",
               a.account_type AS ""Type"", p.account_name AS ""Parent"", a.opening_balance AS ""Opening Balance"",
               a.balance_type AS ""Balance Type"", a.active AS ""Active""
        FROM chart_of_accounts a
        LEFT JOIN chart_of_accounts p ON p.account_id = a.parent_id
        ORDER BY a.account_code, a.account_name");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM chart_of_accounts WHERE account_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtCode.Text = r["account_code"]?.ToString();
        txtName.Text = r["account_name"].ToString();
        cboType.SelectedItem = r["account_type"]?.ToString();
        numOpeningBalance.Value = r["opening_balance"] is DBNull ? 0 : Convert.ToDecimal(r["opening_balance"]);
        cboBalanceType.SelectedItem = r["balance_type"]?.ToString();
        txtDescription.Text = r["description"]?.ToString();
        chkActive.Checked = r["active"] is bool b && b;
        cboParent.SelectedValue = r["parent_id"] == DBNull.Value ? (object)DBNull.Value : Convert.ToInt32(r["parent_id"]);
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("Account Name is required."); return false; }
        if (cboType.SelectedItem == null) { MessageBox.Show("Select an Account Type."); return false; }

        object? parentId = (cboParent.SelectedValue is int pid) ? pid : null;
        if (parentId != null && EditingId != null && (int)parentId == EditingId)
        {
            MessageBox.Show("An account cannot be its own parent.");
            return false;
        }

        var pars = new Dictionary<string, object?>
        {
            ["code"] = string.IsNullOrWhiteSpace(txtCode.Text) ? null : txtCode.Text.Trim(),
            ["name"] = txtName.Text.Trim(),
            ["type"] = cboType.SelectedItem!.ToString(),
            ["parent"] = parentId,
            ["opening"] = numOpeningBalance.Value,
            ["balType"] = cboBalanceType.SelectedItem?.ToString() ?? "Dr",
            ["desc"] = txtDescription.Text.Trim(),
            ["active"] = chkActive.Checked
        };

        if (EditingId == null)
            DbHelper.ExecuteNonQuery(@"
                INSERT INTO chart_of_accounts (account_code, account_name, account_type, parent_id, opening_balance, balance_type, description, active)
                VALUES (@code, @name, @type, @parent, @opening, @balType, @desc, @active)", pars);
        else
        {
            pars["id"] = EditingId;
            DbHelper.ExecuteNonQuery(@"
                UPDATE chart_of_accounts SET account_code=@code, account_name=@name, account_type=@type,
                       parent_id=@parent, opening_balance=@opening, balance_type=@balType,
                       description=@desc, active=@active
                WHERE account_id=@id", pars);
        }
        LoadParents();
        return true;
    }

    protected override void DeleteRecord(int id) =>
        DbHelper.ExecuteNonQuery("DELETE FROM chart_of_accounts WHERE account_id=@id", new() { ["id"] = id });

    protected override void ResetFields()
    {
        txtCode.Clear(); txtName.Clear(); txtDescription.Clear();
        numOpeningBalance.Value = 0; chkActive.Checked = true;
        cboType.SelectedIndex = -1;
        cboBalanceType.SelectedIndex = -1;
        if (cboParent.Items.Count > 0) cboParent.SelectedIndex = -1;
    }
}
