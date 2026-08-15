using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class BrandMasterForm : SimpleMasterFormBase
{
    private readonly TextBox txtName = new();
    private readonly TextBox txtDescription = new();
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    public BrandMasterForm() : base("Brand Master")
    {
        AddRow(FieldsPanel, 0, "Brand Name", txtName);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 3, 0);
        AddRow(FieldsPanel, 1, "Description", txtDescription);

        InitializeLayout("Brand Information", "Brand List");
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT brand_id AS ""ID"", brand_name AS ""Brand Name"", description AS ""Description"", active AS ""Active""
        FROM brand_master ORDER BY brand_name");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM brand_master WHERE brand_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtName.Text = r["brand_name"].ToString();
        txtDescription.Text = r["description"]?.ToString();
        chkActive.Checked = r["active"] is bool b && b;
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("Brand Name is required."); return false; }
        var pars = new Dictionary<string, object?>
        {
            ["name"] = txtName.Text.Trim(),
            ["desc"] = txtDescription.Text.Trim(),
            ["active"] = chkActive.Checked
        };
        if (EditingId == null)
            DbHelper.ExecuteNonQuery("INSERT INTO brand_master (brand_name, description, active) VALUES (@name,@desc,@active)", pars);
        else
        {
            pars["id"] = EditingId;
            DbHelper.ExecuteNonQuery("UPDATE brand_master SET brand_name=@name, description=@desc, active=@active WHERE brand_id=@id", pars);
        }
        return true;
    }

    protected override void DeleteRecord(int id) =>
        DbHelper.ExecuteNonQuery("DELETE FROM brand_master WHERE brand_id=@id", new() { ["id"] = id });

    protected override void ResetFields()
    {
        txtName.Clear(); txtDescription.Clear(); chkActive.Checked = true;
    }
}
