using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class FinancialYearForm : SimpleMasterFormBase
{
    private readonly TextBox txtName = new();
    private readonly DateTimePicker dtStart = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtEnd = new() { Format = DateTimePickerFormat.Short };
    private readonly CheckBox chkCurrent = new() { Text = "Current Year" };
    private readonly CheckBox chkActive = new() { Text = "Active", Checked = true };

    public FinancialYearForm() : base("Financial Year")
    {
        AddRow(FieldsPanel, 0, "Financial Year", txtName);
        AddRow(FieldsPanel, 1, "Start Date", dtStart, "End Date", dtEnd);
        chkCurrent.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkCurrent, 1, 2);
        chkActive.Dock = DockStyle.Fill;
        FieldsPanel.Controls.Add(chkActive, 3, 2);

        InitializeLayout("Financial Year Information", "Financial Year List");
    }

    protected override DataTable LoadListData() => DbHelper.ExecuteQuery(@"
        SELECT fy_id AS ""ID"", fy_name AS ""Financial Year"", start_date AS ""Start Date"",
               end_date AS ""End Date"", is_current AS ""Current"", is_active AS ""Active""
        FROM financial_year ORDER BY start_date DESC");

    protected override void PopulateFields(int id)
    {
        var t = DbHelper.ExecuteQuery("SELECT * FROM financial_year WHERE fy_id=@id", new() { ["id"] = id });
        if (t.Rows.Count == 0) return;
        var r = t.Rows[0];
        txtName.Text = r["fy_name"].ToString();
        dtStart.Value = Convert.ToDateTime(r["start_date"]);
        dtEnd.Value = Convert.ToDateTime(r["end_date"]);
        chkCurrent.Checked = r["is_current"] is bool b1 && b1;
        chkActive.Checked = r["is_active"] is bool b2 && b2;
    }

    protected override bool SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("Financial Year name is required."); return false; }
        if (dtEnd.Value.Date <= dtStart.Value.Date) { MessageBox.Show("End Date must be after Start Date."); return false; }

        var pars = new Dictionary<string, object?>
        {
            ["name"] = txtName.Text.Trim(),
            ["start"] = dtStart.Value.Date,
            ["end"] = dtEnd.Value.Date,
            ["current"] = chkCurrent.Checked,
            ["active"] = chkActive.Checked
        };

        // Only one financial year can be "current" at a time
        if (chkCurrent.Checked)
            DbHelper.ExecuteNonQuery("UPDATE financial_year SET is_current = FALSE");

        if (EditingId == null)
            DbHelper.ExecuteNonQuery(@"
                INSERT INTO financial_year (fy_name, start_date, end_date, is_current, is_active)
                VALUES (@name, @start, @end, @current, @active)", pars);
        else
        {
            pars["id"] = EditingId;
            DbHelper.ExecuteNonQuery(@"
                UPDATE financial_year SET fy_name=@name, start_date=@start, end_date=@end,
                       is_current=@current, is_active=@active
                WHERE fy_id=@id", pars);
        }

        if (chkCurrent.Checked)
            ErpApp.Data.AppConfig.SetFinancialYear(txtName.Text.Trim());

        return true;
    }

    protected override void DeleteRecord(int id) =>
        DbHelper.ExecuteNonQuery("DELETE FROM financial_year WHERE fy_id=@id", new() { ["id"] = id });

    protected override void ResetFields()
    {
        txtName.Clear();
        dtStart.Value = DateTime.Today;
        dtEnd.Value = DateTime.Today.AddYears(1);
        chkCurrent.Checked = false;
        chkActive.Checked = true;
    }
}
