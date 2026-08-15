using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class UomMasterForm : SimpleMasterFormBase
{
    private readonly TextBox txtName = new();
    private readonly TextBox txtCode = new();
    private readonly TextBox txtDescription = new();
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    public UomMasterForm() : base("Unit of Measure Master")
    {
        AddRow(FieldsPanel, 0, "UOM Name", txtName, "UOM Code", txtCode);
        AddRow(FieldsPanel, 1, "Description", txtDescription);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 3, 1);

        InitializeLayout("UOM Information", "UOM List");
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT uom_id AS ""ID"", uom_name AS ""UOM Name"", uom_code AS ""UOM Code"",
               description AS ""Description"", active AS ""Active""
        FROM uom_master ORDER BY uom_name");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM uom_master WHERE uom_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtName.Text = r["uom_name"].ToString();
        txtCode.Text = r["uom_code"]?.ToString();
        txtDescription.Text = r["description"]?.ToString();
        chkActive.Checked = r["active"] is bool b && b;
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("UOM Name is required."); return false; }
        var pars = new Dictionary<string, object?>
        {
            ["name"] = txtName.Text.Trim(),
            ["code"] = txtCode.Text.Trim(),
            ["desc"] = txtDescription.Text.Trim(),
            ["active"] = chkActive.Checked
        };
        if (EditingId == null)
            DbHelper.ExecuteNonQuery("INSERT INTO uom_master (uom_name, uom_code, description, active) VALUES (@name,@code,@desc,@active)", pars);
        else
        {
            pars["id"] = EditingId;
            DbHelper.ExecuteNonQuery("UPDATE uom_master SET uom_name=@name, uom_code=@code, description=@desc, active=@active WHERE uom_id=@id", pars);
        }
        return true;
    }

    protected override void DeleteRecord(int id) =>
        DbHelper.ExecuteNonQuery("DELETE FROM uom_master WHERE uom_id=@id", new() { ["id"] = id });

    protected override void ResetFields()
    {
        txtName.Clear(); txtCode.Clear(); txtDescription.Clear(); chkActive.Checked = true;
    }
}
