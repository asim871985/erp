using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class ManageUsersForm : SimpleMasterFormBase
{
    private readonly TextBox txtUsername = new();
    private readonly TextBox txtFullName = new();
    private readonly ComboBox cboRole = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtPassword = new() { PasswordChar = '*' };
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };
    private readonly Label lblPasswordHint = new() { ForeColor = Color.Gray, AutoSize = true };

    public ManageUsersForm() : base("Manage Users")
    {
        cboRole.Items.AddRange(new object[] { "Admin", "User" });
        cboRole.SelectedIndex = 1;

        AddRow(FieldsPanel, 0, "Username", txtUsername, "Full Name", txtFullName);
        AddRow(FieldsPanel, 1, "Role", cboRole, "Password", txtPassword);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 1, 2);
        lblPasswordHint.Text = "Leave Password blank when editing to keep the current password unchanged.";
        lblPasswordHint.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(lblPasswordHint, 2, 2);
        FieldsPanel.SetColumnSpan(lblPasswordHint, 2);

        InitializeLayout("User Information", "User List");
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT user_id AS ""ID"", username AS ""Username"", full_name AS ""Full Name"",
               role AS ""Role"", active AS ""Active""
        FROM users_master ORDER BY username");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM users_master WHERE user_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtUsername.Text = r["username"].ToString();
        txtFullName.Text = r["full_name"]?.ToString();
        cboRole.SelectedItem = r["role"]?.ToString() ?? "User";
        chkActive.Checked = r["active"] is bool b && b;
        txtPassword.Clear(); // never show/prefill a password
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtUsername.Text)) { MessageBox.Show("Username is required."); return false; }
        if (EditingId == null && string.IsNullOrWhiteSpace(txtPassword.Text))
        {
            MessageBox.Show("Password is required for a new user.");
            return false;
        }

        var pars = new Dictionary<string, object?>
        {
            ["username"] = txtUsername.Text.Trim(),
            ["fullname"] = txtFullName.Text.Trim(),
            ["role"] = cboRole.SelectedItem?.ToString() ?? "User",
            ["active"] = chkActive.Checked
        };

        if (EditingId == null)
        {
            pars["hash"] = PasswordHelper.Hash(txtPassword.Text);
            DbHelper.ExecuteNonQuery(@"
                INSERT INTO users_master (username, password_hash, full_name, role, active)
                VALUES (@username, @hash, @fullname, @role, @active)", pars);
        }
        else
        {
            pars["id"] = EditingId;
            if (!string.IsNullOrWhiteSpace(txtPassword.Text))
            {
                pars["hash"] = PasswordHelper.Hash(txtPassword.Text);
                DbHelper.ExecuteNonQuery(@"
                    UPDATE users_master SET username=@username, password_hash=@hash, full_name=@fullname,
                           role=@role, active=@active
                    WHERE user_id=@id", pars);
            }
            else
            {
                DbHelper.ExecuteNonQuery(@"
                    UPDATE users_master SET username=@username, full_name=@fullname, role=@role, active=@active
                    WHERE user_id=@id", pars);
            }
        }
        return true;
    }

    protected override void DeleteRecord(int id)
    {
        if (id == 1)
            throw new Exception("The default admin account can't be deleted from here — deactivate it instead if needed.");
        DbHelper.ExecuteNonQuery("DELETE FROM users_master WHERE user_id=@id", new() { ["id"] = id });
    }

    protected override void ResetFields()
    {
        txtUsername.Clear(); txtFullName.Clear(); txtPassword.Clear();
        cboRole.SelectedIndex = 1;
        chkActive.Checked = true;
    }
}
