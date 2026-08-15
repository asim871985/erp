using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Shared scaffold used by the simple Master screens (UOM, Brand, Model, Warehouse,
/// Tax, Customer, Supplier, Account). Subclasses only need to describe their fields,
/// their list query, and their insert/update/delete calls.
/// </summary>
public abstract class SimpleMasterFormBase : AppFormBase
{
    protected readonly TableLayoutPanel FieldsPanel = new() { Dock = DockStyle.Fill, ColumnCount = 4 };
    protected readonly DataGridView Grid = new();
    private readonly Button btnNew = new() { Text = "+ New" };
    private readonly Button btnEdit = new() { Text = "Edit" };
    private readonly Button btnDelete = new() { Text = "Delete" };
    private readonly Button btnSave = new() { Text = "Save" };
    private readonly Button btnClear = new() { Text = "Clear" };

    protected int? EditingId;

    protected SimpleMasterFormBase(string title)
    {
        Text = title;
        Width = 900;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
    }

    /// <summary>Call this from the derived constructor after fields are built.</summary>
    protected void InitializeLayout(string groupBoxTitle, string listTitle)
    {
        // FieldsPanel's rows were added implicitly by AddRow() calls in the subclass
        // constructor, with no RowStyles set — left alone, TableLayoutPanel auto-sizes
        // each row to fit its content, which produces uneven row heights (visible as
        // gaps between some label/textbox pairs and cramped spacing for others). Since
        // all AddRow() calls have already run by the time we get here, work out how many
        // rows were actually used and give every one an equal, explicit share of the height.
        int rowCount = FieldsPanel.Controls.Count > 0
            ? FieldsPanel.Controls.Cast<Control>().Max(c => FieldsPanel.GetRow(c)) + 1
            : 1;
        for (int i = 0; i < rowCount; i++)
            FieldsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowCount));

        const int rowHeight = 42;
        var infoGroup = new GroupBox { Text = groupBoxTitle, Dock = DockStyle.Top, Height = rowCount * rowHeight + 35 };
        for (int i = 0; i < 4; i++) FieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        FieldsPanel.Padding = new Padding(10);
        infoGroup.Controls.Add(FieldsPanel);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(5) };
        btnSave.Click += (s, e) => SaveClicked();
        btnClear.Click += (s, e) => ClearForm();
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnClear);

        var listLabel = new Label { Text = listTitle, Dock = DockStyle.Top, Height = 25, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(5, 5, 0, 0) };

        var gridBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        btnDelete.Click += (s, e) => DeleteClicked();
        btnEdit.Click += (s, e) => EditClicked();
        btnNew.Click += (s, e) => ClearForm();
        gridBtnPanel.Controls.Add(btnDelete);
        gridBtnPanel.Controls.Add(btnEdit);
        gridBtnPanel.Controls.Add(btnNew);

        Grid.Dock = DockStyle.Fill;
        Grid.ReadOnly = true;
        Grid.AllowUserToAddRows = false;
        Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Grid.MultiSelect = false;
        Grid.CellDoubleClick += (s, e) => EditClicked();

        Controls.Add(Grid);
        Controls.Add(gridBtnPanel);
        Controls.Add(listLabel);
        Controls.Add(btnPanel);
        Controls.Add(infoGroup);

        ReloadGrid();
    }

    protected static void AddRow(TableLayoutPanel t, int row, string l1, Control c1, string? l2 = null, Control? c2 = null)
    {
        t.Controls.Add(new Label { Text = l1, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        c1.Dock = DockStyle.Fill;
        t.Controls.Add(c1, 1, row);
        if (l2 != null)
        {
            t.Controls.Add(new Label { Text = l2, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 2, row);
            if (c2 != null)
            {
                c2.Dock = DockStyle.Fill;
                t.Controls.Add(c2, 3, row);
            }
        }
    }

    /// <summary>Load the DataTable to show in the grid. "ID" column (hidden) must be first.</summary>
    protected abstract DataTable LoadListData();

    /// <summary>Populate the entry fields from the row with this primary key.</summary>
    protected abstract void PopulateFields(int id);

    /// <summary>Insert or update depending on EditingId; return true on success.</summary>
    protected abstract bool SaveRecord();

    /// <summary>Delete the record with this primary key.</summary>
    protected abstract void DeleteRecord(int id);

    /// <summary>Reset all input fields to their defaults.</summary>
    protected abstract void ResetFields();

    protected void ReloadGrid()
    {
        try
        {
            var table = LoadListData();
            Grid.DataSource = table;
            if (Grid.Columns.Contains("ID")) Grid.Columns["ID"]!.Visible = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load list: " + ex.Message);
        }
    }

    private void SaveClicked()
    {
        try
        {
            bool wasEditing = EditingId != null;
            if (SaveRecord())
            {
                Data.DbHelper.LogAction($"{Text}: {(wasEditing ? "Updated" : "Created")} record");
                ClearForm();
                ReloadGrid();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void EditClicked()
    {
        if (Grid.CurrentRow == null) { MessageBox.Show("Select a row to edit."); return; }
        int id = Convert.ToInt32(Grid.CurrentRow.Cells["ID"].Value);
        EditingId = id;
        PopulateFields(id);
    }

    private void DeleteClicked()
    {
        if (Grid.CurrentRow == null) { MessageBox.Show("Select a row to delete."); return; }
        if (MessageBox.Show("Delete this record?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        int id = Convert.ToInt32(Grid.CurrentRow.Cells["ID"].Value);
        try
        {
            DeleteRecord(id);
            Data.DbHelper.LogAction($"{Text}: Deleted record #{id}");
            ReloadGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed (record may be referenced elsewhere): " + ex.Message);
        }
    }

    private void ClearForm()
    {
        EditingId = null;
        ResetFields();
    }
}
