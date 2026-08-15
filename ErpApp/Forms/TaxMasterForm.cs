using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class TaxMasterForm : SimpleMasterFormBase
{
    private readonly TextBox txtName = new();
    private readonly NumericUpDown numRate = new() { DecimalPlaces = 2, Maximum = 100 };
    private readonly TextBox txtDescription = new();
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    public TaxMasterForm() : base("Tax Master")
    {
        AddRow(FieldsPanel, 0, "Tax Name", txtName, "Rate (%)", numRate);
        AddRow(FieldsPanel, 1, "Description", txtDescription);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 3, 1);

        InitializeLayout("Tax Information", "Tax List");
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT tax_id AS ""ID"", tax_name AS ""Tax Name"", tax_percent AS ""Rate (%)"",
               description AS ""Description"", active AS ""Active""
        FROM tax_master ORDER BY tax_name");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM tax_master WHERE tax_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtName.Text = r["tax_name"].ToString();
        numRate.Value = r["tax_percent"] is DBNull ? 0 : Convert.ToDecimal(r["tax_percent"]);
        txtDescription.Text = r["description"]?.ToString();
        chkActive.Checked = r["active"] is bool b && b;
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("Tax Name is required."); return false; }
        var pars = new Dictionary<string, object?>
        {
            ["name"] = txtName.Text.Trim(),
            ["rate"] = numRate.Value,
            ["desc"] = txtDescription.Text.Trim(),
            ["active"] = chkActive.Checked
        };
        if (EditingId == null)
            DbHelper.ExecuteNonQuery("INSERT INTO tax_master (tax_name, tax_percent, description, active) VALUES (@name,@rate,@desc,@active)", pars);
        else
        {
            pars["id"] = EditingId;
            DbHelper.ExecuteNonQuery("UPDATE tax_master SET tax_name=@name, tax_percent=@rate, description=@desc, active=@active WHERE tax_id=@id", pars);
        }
        return true;
    }

    protected override void DeleteRecord(int id) =>
        DbHelper.ExecuteNonQuery("DELETE FROM tax_master WHERE tax_id=@id", new() { ["id"] = id });

    protected override void ResetFields()
    {
        txtName.Clear(); numRate.Value = 0; txtDescription.Clear(); chkActive.Checked = true;
    }
}
