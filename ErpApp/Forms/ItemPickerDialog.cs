using System.Data;

namespace ErpApp.Forms;

/// <summary>Small popup to search and pick one item row from a preloaded DataTable.</summary>
public class ItemPickerDialog : AppFormBase
{
    public DataRow? SelectedRow { get; private set; }

    private readonly DataGridView grid = new();
    private readonly TextBox txtSearch = new() { Dock = DockStyle.Top };
    private readonly DataTable source;

    public ItemPickerDialog(DataTable items)
    {
        source = items;
        Text = "Select Item";
        Width = 700;
        Height = 450;
        StartPosition = FormStartPosition.CenterParent;

        txtSearch.TextChanged += (s, e) => Filter();
        var placeholder = new Label { Text = "Type to search item name / model / brand...", Dock = DockStyle.Top, Height = 20, ForeColor = Color.Gray };

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.DataSource = source;
        grid.CellDoubleClick += (s, e) => Choose();

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8) };
        var btnOk = new Button { Text = "Select" };
        var btnCancel = new Button { Text = "Cancel" };
        btnOk.Click += (s, e) => Choose();
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.Add(btnOk);
        btnPanel.Controls.Add(btnCancel);

        Controls.Add(grid);
        Controls.Add(btnPanel);
        Controls.Add(txtSearch);
        Controls.Add(placeholder);
    }

    private void Filter()
    {
        string term = txtSearch.Text.Trim();
        if (string.IsNullOrEmpty(term)) { grid.DataSource = source; return; }

        bool HasColumn(string name) => source.Columns.Contains(name);
        string Val(DataRow r, string col) => HasColumn(col) ? (r[col]?.ToString() ?? "") : "";

        var view = source.Copy();
        var rowsToRemove = view.AsEnumerable()
            .Where(r => !r["item_name"].ToString()!.Contains(term, StringComparison.OrdinalIgnoreCase)
                     && !Val(r, "model").Contains(term, StringComparison.OrdinalIgnoreCase)
                     && !Val(r, "brand_name").Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var r in rowsToRemove) view.Rows.Remove(r);
        grid.DataSource = view;
    }

    private void Choose()
    {
        if (grid.CurrentRow?.DataBoundItem is DataRowView rv)
        {
            SelectedRow = rv.Row;
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            MessageBox.Show("Select a row first.");
        }
    }
}
