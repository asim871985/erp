using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class CategoryMasterForm : SimpleMasterFormBase
{
    private readonly TextBox txtName = new();
    private readonly TextBox txtDescription = new();
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    public CategoryMasterForm() : base("Category Master")
    {
        AddRow(FieldsPanel, 0, "Category Name", txtName);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 3, 0);
        AddRow(FieldsPanel, 1, "Description", txtDescription);

        InitializeLayout("Category Information", "Category List");
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT category_id AS ""ID"", category_name AS ""Category Name"", description AS ""Description"", active AS ""Active""
        FROM category_master ORDER BY category_name");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM category_master WHERE category_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtName.Text = r["category_name"].ToString();
        txtDescription.Text = r["description"]?.ToString();
        chkActive.Checked = r["active"] is bool b && b;
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("Category Name is required."); return false; }
        var pars = new Dictionary<string, object?>
        {
            ["name"] = txtName.Text.Trim(),
            ["desc"] = txtDescription.Text.Trim(),
            ["active"] = chkActive.Checked
        };
        if (EditingId == null)
            DbHelper.ExecuteNonQuery("INSERT INTO category_master (category_name, description, active) VALUES (@name,@desc,@active)", pars);
        else
        {
            pars["id"] = EditingId;
            DbHelper.ExecuteNonQuery("UPDATE category_master SET category_name=@name, description=@desc, active=@active WHERE category_id=@id", pars);
        }
        return true;
    }

    protected override void DeleteRecord(int id) =>
        DbHelper.ExecuteNonQuery("DELETE FROM category_master WHERE category_id=@id", new() { ["id"] = id });

    protected override void ResetFields()
    {
        txtName.Clear(); txtDescription.Clear(); chkActive.Checked = true;
    }
}
