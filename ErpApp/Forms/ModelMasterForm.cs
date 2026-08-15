using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class ModelMasterForm : SimpleMasterFormBase
{
    private readonly TextBox txtName = new();
    private readonly ComboBox cboBrand = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtDescription = new();
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    public ModelMasterForm() : base("Model Master")
    {
        AddRow(FieldsPanel, 0, "Model Name", txtName, "Brand", cboBrand);
        AddRow(FieldsPanel, 1, "Description", txtDescription);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 3, 1);

        LoadBrands();
        InitializeLayout("Model Information", "Model List");
    }

    private void LoadBrands()
    {
        try
        {
            var brands = DbHelper.ExecuteQuery("SELECT brand_id, brand_name FROM brand_master ORDER BY brand_name");
            cboBrand.DisplayMember = "brand_name";
            cboBrand.ValueMember = "brand_id";
            cboBrand.DataSource = brands;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load brands: " + ex.Message);
        }
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT m.model_id AS ""ID"", m.model_name AS ""Model Name"", b.brand_name AS ""Brand"",
               m.description AS ""Description"", m.active AS ""Active""
        FROM model_master m LEFT JOIN brand_master b ON b.brand_id = m.brand_id
        ORDER BY m.model_name");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM model_master WHERE model_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtName.Text = r["model_name"].ToString();
        txtDescription.Text = r["description"]?.ToString();
        chkActive.Checked = r["active"] is bool b && b;
        cboBrand.SelectedValue = r["brand_id"] == DBNull.Value ? null : Convert.ToInt32(r["brand_id"]);
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("Model Name is required."); return false; }
        var pars = new Dictionary<string, object?>
        {
            ["name"] = txtName.Text.Trim(),
            ["brand"] = cboBrand.SelectedValue as int?,
            ["desc"] = txtDescription.Text.Trim(),
            ["active"] = chkActive.Checked
        };
        if (EditingId == null)
            DbHelper.ExecuteNonQuery("INSERT INTO model_master (model_name, brand_id, description, active) VALUES (@name,@brand,@desc,@active)", pars);
        else
        {
            pars["id"] = EditingId;
            DbHelper.ExecuteNonQuery("UPDATE model_master SET model_name=@name, brand_id=@brand, description=@desc, active=@active WHERE model_id=@id", pars);
        }
        return true;
    }

    protected override void DeleteRecord(int id) =>
        DbHelper.ExecuteNonQuery("DELETE FROM model_master WHERE model_id=@id", new() { ["id"] = id });

    protected override void ResetFields()
    {
        txtName.Clear(); txtDescription.Clear(); chkActive.Checked = true;
        if (cboBrand.Items.Count > 0) cboBrand.SelectedIndex = -1;
    }
}
