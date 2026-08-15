using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class WarehouseMasterForm : SimpleMasterFormBase
{
    private readonly TextBox txtName = new();
    private readonly TextBox txtLocation = new();
    private readonly TextBox txtDescription = new();
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    public WarehouseMasterForm() : base("Warehouse Master")
    {
        AddRow(FieldsPanel, 0, "Warehouse Name", txtName);
        AddRow(FieldsPanel, 1, "Location", txtLocation);
        AddRow(FieldsPanel, 2, "Description", txtDescription);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 3, 2);

        InitializeLayout("Warehouse Information", "Warehouse List");
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT warehouse_id AS ""ID"", warehouse_name AS ""Warehouse Name"", location AS ""Location"",
               description AS ""Description"", active AS ""Active""
        FROM warehouse_master ORDER BY warehouse_name");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM warehouse_master WHERE warehouse_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtName.Text = r["warehouse_name"].ToString();
        txtLocation.Text = r["location"]?.ToString();
        txtDescription.Text = r["description"]?.ToString();
        chkActive.Checked = r["active"] is bool b && b;
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("Warehouse Name is required."); return false; }
        var pars = new Dictionary<string, object?>
        {
            ["name"] = txtName.Text.Trim(),
            ["loc"] = txtLocation.Text.Trim(),
            ["desc"] = txtDescription.Text.Trim(),
            ["active"] = chkActive.Checked
        };
        if (EditingId == null)
            DbHelper.ExecuteNonQuery("INSERT INTO warehouse_master (warehouse_name, location, description, active) VALUES (@name,@loc,@desc,@active)", pars);
        else
        {
            pars["id"] = EditingId;
            DbHelper.ExecuteNonQuery("UPDATE warehouse_master SET warehouse_name=@name, location=@loc, description=@desc, active=@active WHERE warehouse_id=@id", pars);
        }
        return true;
    }

    protected override void DeleteRecord(int id) =>
        DbHelper.ExecuteNonQuery("DELETE FROM warehouse_master WHERE warehouse_id=@id", new() { ["id"] = id });

    protected override void ResetFields()
    {
        txtName.Clear(); txtLocation.Clear(); txtDescription.Clear(); chkActive.Checked = true;
    }
}
