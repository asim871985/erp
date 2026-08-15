using ErpApp.Data;

namespace ErpApp.Forms;

public class CompanyProfileForm : AppFormBase
{
    private readonly TextBox txtName = new();
    private readonly TextBox txtAddress = new();
    private readonly TextBox txtPhone = new();
    private readonly TextBox txtEmail = new();
    private readonly TextBox txtNtn = new();
    private readonly TextBox txtStrn = new();
    private readonly PictureBox picLogo = new() { BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.WhiteSmoke };
    private string? logoPath;
    private int companyId;

    public CompanyProfileForm()
    {
        Text = "Company Profile";
        Width = 650;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        BuildLayout();
        LoadProfile();
    }

    private void BuildLayout()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, Padding = new Padding(15) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));

        void Row(int r, string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, r);
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c, 1, r);
        }

        Row(0, "Company Name", txtName);
        Row(1, "Address", txtAddress);
        Row(2, "Phone", txtPhone);
        Row(3, "Email", txtEmail);
        Row(4, "NTN", txtNtn);
        Row(5, "STRN", txtStrn);

        picLogo.Width = 140;
        picLogo.Height = 140;
        t.Controls.Add(picLogo, 2, 0);
        t.SetRowSpan(picLogo, 3);
        var btnUploadLogo = new Button { Text = "Upload Logo", Width = 140 };
        btnUploadLogo.Click += BtnUploadLogo_Click;
        t.Controls.Add(btnUploadLogo, 2, 3);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(15) };
        var btnSave = new Button { Text = "Save", Width = 90 };
        var btnCancel = new Button { Text = "Cancel", Width = 90 };
        btnSave.Click += BtnSave_Click;
        btnCancel.Click += (s, e) => Close();
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnCancel);

        Controls.Add(t);
        Controls.Add(btnPanel);
    }

    private void BtnUploadLogo_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        logoPath = ofd.FileName;
        picLogo.Image = Image.FromFile(logoPath);
    }

    private void LoadProfile()
    {
        try
        {
            var t = DbHelper.ExecuteQuery("SELECT * FROM company_profile ORDER BY company_id LIMIT 1");
            if (t.Rows.Count == 0) return;
            var r = t.Rows[0];
            companyId = Convert.ToInt32(r["company_id"]);
            txtName.Text = r["company_name"]?.ToString();
            txtAddress.Text = r["address"]?.ToString();
            txtPhone.Text = r["phone"]?.ToString();
            txtEmail.Text = r["email"]?.ToString();
            txtNtn.Text = r["ntn"]?.ToString();
            txtStrn.Text = r["strn"]?.ToString();
            logoPath = r["logo_path"] as string;
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                picLogo.Image = Image.FromFile(logoPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load company profile: " + ex.Message);
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("Company Name is required."); return; }
        try
        {
            var pars = new Dictionary<string, object?>
            {
                ["name"] = txtName.Text.Trim(),
                ["addr"] = txtAddress.Text.Trim(),
                ["phone"] = txtPhone.Text.Trim(),
                ["email"] = txtEmail.Text.Trim(),
                ["ntn"] = txtNtn.Text.Trim(),
                ["strn"] = txtStrn.Text.Trim(),
                ["logo"] = (object?)logoPath ?? DBNull.Value,
                ["id"] = companyId
            };

            if (companyId == 0)
            {
                companyId = Convert.ToInt32(DbHelper.ExecuteScalar(@"
                    INSERT INTO company_profile (company_name, address, phone, email, ntn, strn, logo_path)
                    VALUES (@name, @addr, @phone, @email, @ntn, @strn, @logo) RETURNING company_id", pars));
            }
            else
            {
                DbHelper.ExecuteNonQuery(@"
                    UPDATE company_profile SET company_name=@name, address=@addr, phone=@phone,
                           email=@email, ntn=@ntn, strn=@strn, logo_path=@logo
                    WHERE company_id=@id", pars);
            }

            AppConfig.SetCompanyName(txtName.Text.Trim());
            MessageBox.Show("Company profile saved.");
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }
}
