using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Users > Change Password. Verifies the current password before setting a new one.</summary>
public class ChangePasswordForm : AppFormBase
{
    private readonly TextBox txtCurrentPassword = new() { PasswordChar = '*' };
    private readonly TextBox txtNewPassword = new() { PasswordChar = '*' };
    private readonly TextBox txtConfirmPassword = new() { PasswordChar = '*' };

    public ChangePasswordForm()
    {
        Text = "Change Password";
        Width = 420;
        Height = 280;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        BuildLayout();
    }

    private void BuildLayout()
    {
        var title = new Label
        {
            Text = $"Change Password — {AppConfig.CurrentUser}",
            Dock = DockStyle.Top,
            Height = 35,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Padding = new Padding(15, 8, 0, 0)
        };

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(15) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c);
        }

        Row("Current Password", txtCurrentPassword);
        Row("New Password", txtNewPassword);
        Row("Confirm New Password", txtConfirmPassword);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(15) };
        var btnSave = new Button { Text = "Save", Width = 90 };
        var btnCancel = new Button { Text = "Cancel", Width = 90 };
        btnSave.Click += BtnSave_Click;
        btnCancel.Click += (s, e) => Close();
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnCancel);

        Controls.Add(t);
        Controls.Add(btnPanel);
        Controls.Add(title);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtNewPassword.Text) || txtNewPassword.Text.Length < 4)
        {
            MessageBox.Show("New password must be at least 4 characters.");
            return;
        }
        if (txtNewPassword.Text != txtConfirmPassword.Text)
        {
            MessageBox.Show("New Password and Confirm New Password don't match.");
            return;
        }

        try
        {
            var t = DbHelper.ExecuteQuery("SELECT user_id, password_hash FROM users_master WHERE username=@u",
                new Dictionary<string, object?> { ["u"] = AppConfig.CurrentUser });
            if (t.Rows.Count == 0)
            {
                MessageBox.Show("Current user account not found in Users table.");
                return;
            }

            var row = t.Rows[0];
            string storedHash = row["password_hash"].ToString() ?? "";

            // PasswordHelper.Verify accepts the current BCrypt hash, a legacy SHA-256
            // hash, or the plain-text "admin" seed, so the very first login after
            // setup still works before anyone has set a real password.
            bool currentOk = PasswordHelper.Verify(txtCurrentPassword.Text, storedHash);
            if (!currentOk)
            {
                MessageBox.Show("Current password is incorrect.");
                return;
            }

            DbHelper.ExecuteNonQuery("UPDATE users_master SET password_hash=@hash WHERE user_id=@id",
                new Dictionary<string, object?> { ["hash"] = PasswordHelper.Hash(txtNewPassword.Text), ["id"] = row["user_id"] });

            DbHelper.LogAction("Changed own password");
            MessageBox.Show("Password changed successfully.");
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not change password: " + ex.Message);
        }
    }
}
