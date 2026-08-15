using ErpApp.Data;

namespace ErpApp.Forms;

public class SettingsForm : AppFormBase
{
    private readonly ComboBox cboCurrency = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboFiscalStart = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox chkNotifications = new() { Text = "Enable Notifications" };
    private readonly CheckBox chkMultiCurrency = new() { Text = "Enable Multi-Currency" };
    private int companyId;

    public SettingsForm()
    {
        Text = "Settings";
        Width = 450;
        Height = 320;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        BuildLayout();
        LoadSettings();
    }

    private void BuildLayout()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(15) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        cboCurrency.Items.AddRange(new object[] { "PKR", "USD", "EUR", "GBP", "AED", "SAR" });
        cboFiscalStart.Items.AddRange(new object[] { "January", "April", "July", "October" });

        t.Controls.Add(new Label { Text = "Currency", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        cboCurrency.Dock = DockStyle.Fill;
        t.Controls.Add(cboCurrency, 1, 0);

        t.Controls.Add(new Label { Text = "Fiscal Year Start", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        cboFiscalStart.Dock = DockStyle.Fill;
        t.Controls.Add(cboFiscalStart, 1, 1);

        chkNotifications.Dock = DockStyle.Fill;
        t.Controls.Add(chkNotifications, 0, 2);
        t.SetColumnSpan(chkNotifications, 2);

        chkMultiCurrency.Dock = DockStyle.Fill;
        t.Controls.Add(chkMultiCurrency, 0, 3);
        t.SetColumnSpan(chkMultiCurrency, 2);

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

    private void LoadSettings()
    {
        try
        {
            var t = DbHelper.ExecuteQuery("SELECT * FROM company_profile ORDER BY company_id LIMIT 1");
            if (t.Rows.Count == 0) return;
            var r = t.Rows[0];
            companyId = Convert.ToInt32(r["company_id"]);
            cboCurrency.SelectedItem = r["currency"]?.ToString() ?? "PKR";
            cboFiscalStart.SelectedItem = r["fiscal_year_start"]?.ToString() ?? "January";
            chkNotifications.Checked = r["enable_notifications"] is bool b1 && b1;
            chkMultiCurrency.Checked = r["multi_currency"] is bool b2 && b2;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load settings: " + ex.Message);
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            var pars = new Dictionary<string, object?>
            {
                ["currency"] = cboCurrency.SelectedItem?.ToString() ?? "PKR",
                ["fystart"] = cboFiscalStart.SelectedItem?.ToString() ?? "January",
                ["notif"] = chkNotifications.Checked,
                ["multi"] = chkMultiCurrency.Checked,
                ["id"] = companyId
            };

            if (companyId == 0)
            {
                // No company_profile row yet — create a minimal one
                companyId = Convert.ToInt32(DbHelper.ExecuteScalar(@"
                    INSERT INTO company_profile (company_name, currency, fiscal_year_start, enable_notifications, multi_currency)
                    VALUES ('My Company', @currency, @fystart, @notif, @multi) RETURNING company_id", pars));
            }
            else
            {
                DbHelper.ExecuteNonQuery(@"
                    UPDATE company_profile SET currency=@currency, fiscal_year_start=@fystart,
                           enable_notifications=@notif, multi_currency=@multi
                    WHERE company_id=@id", pars);
            }

            MessageBox.Show("Settings saved.");
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }
}
