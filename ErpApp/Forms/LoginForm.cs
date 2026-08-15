using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Shown before MainForm. Authenticates against users_master via
/// PasswordHelper (BCrypt hashes, plus legacy SHA-256 / plain-text "admin"
/// fallbacks so existing accounts and brand-new installs still work; legacy
/// hashes are re-hashed with BCrypt on a successful login). Program.cs only
/// proceeds to Application.Run(new MainForm()) if this returns DialogResult.OK.
/// </summary>
public class LoginForm : AppFormBase
{
    private readonly TextBox txtUsername = new();
    private readonly TextBox txtPassword = new() { PasswordChar = '*' };
    private readonly Label lblError = new() { ForeColor = Color.Firebrick, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button btnLogin = new() { Text = "Login" };

    private int failedAttempts;
    private const int MaxAttempts = 5;

    public string LoggedInUsername { get; private set; } = "";

    public LoginForm()
    {
        Text = "Login — ERP Software";
        Width = 420;
        Height = 340;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AcceptButton = btnLogin;

        BuildLayout();
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.SteelBlue };
        var lblTitle = new Label
        {
            Text = "ERP Software",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(0, 12, 0, 0)
        };
        var lblSubtitle = new Label
        {
            Text = "Inventory & Accounting System",
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 9),
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter
        };
        header.Controls.Add(lblSubtitle);
        header.Controls.Add(lblTitle);

        // Explicit row heights (all equal) so labels and their textboxes stay aligned —
        // TableLayoutPanel rows left to auto-size can end up uneven, causing the gaps/
        // overlap you'd see if this were built the same loose way as some other forms.
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(30, 25, 30, 10)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        for (int i = 0; i < form.RowCount; i++)
            form.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / form.RowCount));

        void Row(int row, string label, Control c)
        {
            form.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(3, 6, 3, 6);
            form.Controls.Add(c, 1, row);
        }

        Row(0, "Username", txtUsername);
        Row(1, "Password", txtPassword);

        lblError.Dock = DockStyle.Fill;
        form.Controls.Add(lblError, 0, 2);
        form.SetColumnSpan(lblError, 2);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(30, 0, 30, 15)
        };
        var btnExit = new Button { Text = "Exit", Width = 90, Height = 32 };
        btnLogin.Width = 90;
        btnLogin.Height = 32;
        btnLogin.Click += BtnLogin_Click;
        btnExit.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.Add(btnLogin);
        btnPanel.Controls.Add(btnExit);

        Controls.Add(form);
        Controls.Add(btnPanel);
        Controls.Add(header);

        txtUsername.Text = "admin";
        txtUsername.Select();
    }

    private void BtnLogin_Click(object? sender, EventArgs e)
    {
        lblError.Text = "";

        if (string.IsNullOrWhiteSpace(txtUsername.Text))
        {
            lblError.Text = "Enter a username.";
            return;
        }

        try
        {
            var t = DbHelper.ExecuteQuery(
                "SELECT user_id, password_hash, active FROM users_master WHERE username=@u",
                new Dictionary<string, object?> { ["u"] = txtUsername.Text.Trim() });

            if (t.Rows.Count == 0)
            {
                RegisterFailedAttempt("Invalid username or password.");
                return;
            }

            var row = t.Rows[0];
            bool active = row["active"] is bool b && b;
            string storedHash = row["password_hash"].ToString() ?? "";

            bool ok = PasswordHelper.Verify(txtPassword.Text, storedHash);

            if (!ok)
            {
                RegisterFailedAttempt("Invalid username or password.");
                return;
            }
            if (!active)
            {
                lblError.Text = "This account has been deactivated.";
                return;
            }

            // Migrate legacy hashes (old SHA-256 / plain-text seed) to BCrypt on the
            // spot so the account is properly protected after its next login.
            if (PasswordHelper.NeedsRehash(storedHash))
            {
                DbHelper.ExecuteNonQuery("UPDATE users_master SET password_hash=@hash WHERE user_id=@id",
                    new Dictionary<string, object?> { ["hash"] = PasswordHelper.Hash(txtPassword.Text), ["id"] = row["user_id"] });
            }

            LoggedInUsername = txtUsername.Text.Trim();
            AppConfig.CurrentUser = LoggedInUsername;
            DbHelper.LogAction("Logged in");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            lblError.Text = "Could not reach the database.";
            MessageBox.Show(
                "Could not verify credentials — check the connection string in appsettings.json " +
                "and make sure PostgreSQL is running.\n\n" + ex.Message,
                "Database Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RegisterFailedAttempt(string message)
    {
        failedAttempts++;
        lblError.Text = message;
        txtPassword.Clear();
        txtPassword.Focus();

        if (failedAttempts >= MaxAttempts)
        {
            MessageBox.Show("Too many failed attempts. The application will now close.", "Login Failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
