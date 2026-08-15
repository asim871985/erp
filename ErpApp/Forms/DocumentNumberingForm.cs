using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class DocumentNumberingForm : AppFormBase
{
    private readonly ComboBox cboDocType = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox txtPrefix = new();
    private readonly TextBox txtSuffix = new();
    private readonly NumericUpDown numNextNumber = new() { Maximum = 999_999_999, Minimum = 1, Value = 1 };
    private readonly NumericUpDown numPadding = new() { Maximum = 12, Minimum = 1, Value = 5 };
    private readonly DataGridView grid = new();

    private string? editingDocType;

    public DocumentNumberingForm()
    {
        Text = "Document Numbering";
        Width = 800;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        ReloadGrid();
    }

    private void BuildLayout()
    {
        var infoGroup = new GroupBox { Text = "Document Numbering Rule", Dock = DockStyle.Top, Height = 150 };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(10) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        t.Controls.Add(new Label { Text = "Document Type", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        cboDocType.Dock = DockStyle.Fill;
        cboDocType.Items.AddRange(new object[] { "INVOICE", "RECEIPT", "PAYMENT", "PURCHASE", "JOURNAL", "CONTRA", "SALES_RETURN", "PURCHASE_RETURN" });
        t.Controls.Add(cboDocType, 1, 0);

        t.Controls.Add(new Label { Text = "Prefix", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        txtPrefix.Dock = DockStyle.Fill;
        t.Controls.Add(txtPrefix, 3, 0);

        t.Controls.Add(new Label { Text = "Suffix", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        txtSuffix.Dock = DockStyle.Fill;
        t.Controls.Add(txtSuffix, 1, 1);

        t.Controls.Add(new Label { Text = "Number Length", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 1);
        numPadding.Dock = DockStyle.Fill;
        t.Controls.Add(numPadding, 3, 1);

        infoGroup.Controls.Add(t);

        var nextNumPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 5, 10, 5) };
        nextNumPanel.Controls.Add(new Label { Text = "Next Number:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        numNextNumber.Width = 120;
        nextNumPanel.Controls.Add(numNextNumber);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10) };
        var btnSave = new Button { Text = "Save" };
        var btnClear = new Button { Text = "Clear" };
        btnSave.Click += BtnSave_Click;
        btnClear.Click += (s, e) => ClearForm();
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnClear);

        var listLabel = new Label { Text = "Configured Document Types", Dock = DockStyle.Top, Height = 25, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(5, 5, 0, 0) };

        var gridBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        var btnDelete = new Button { Text = "Delete" };
        var btnEdit = new Button { Text = "Edit" };
        btnDelete.Click += (s, e) => DeleteSelected();
        btnEdit.Click += (s, e) => EditSelected();
        gridBtnPanel.Controls.Add(btnDelete);
        gridBtnPanel.Controls.Add(btnEdit);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.CellDoubleClick += (s, e) => EditSelected();

        Controls.Add(grid);
        Controls.Add(gridBtnPanel);
        Controls.Add(listLabel);
        Controls.Add(btnPanel);
        Controls.Add(nextNumPanel);
        Controls.Add(infoGroup);
    }

    private void ReloadGrid()
    {
        try
        {
            var table = DbHelper.ExecuteQuery(@"
                SELECT doc_type AS ""Document Type"", prefix AS ""Prefix"", suffix AS ""Suffix"",
                       next_number AS ""Next Number"", padding AS ""Number Length""
                FROM document_numbering ORDER BY doc_type");
            grid.DataSource = table;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load document numbering rules: " + ex.Message);
        }
    }

    private void EditSelected()
    {
        if (grid.CurrentRow == null) { MessageBox.Show("Select a row to edit."); return; }
        editingDocType = grid.CurrentRow.Cells["Document Type"].Value.ToString();
        cboDocType.Text = editingDocType;
        txtPrefix.Text = grid.CurrentRow.Cells["Prefix"].Value?.ToString();
        txtSuffix.Text = grid.CurrentRow.Cells["Suffix"].Value?.ToString();
        numNextNumber.Value = Convert.ToDecimal(grid.CurrentRow.Cells["Next Number"].Value);
        numPadding.Value = Convert.ToDecimal(grid.CurrentRow.Cells["Number Length"].Value);
    }

    private void DeleteSelected()
    {
        if (grid.CurrentRow == null) { MessageBox.Show("Select a row to delete."); return; }
        string docType = grid.CurrentRow.Cells["Document Type"].Value.ToString()!;
        if (MessageBox.Show($"Delete numbering rule for '{docType}'? Any transaction screen using it will stop working until you add it back.",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            DbHelper.ExecuteNonQuery("DELETE FROM document_numbering WHERE doc_type=@t", new() { ["t"] = docType });
            ReloadGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message);
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        string docType = cboDocType.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(docType)) { MessageBox.Show("Select or type a Document Type."); return; }
        if (string.IsNullOrWhiteSpace(txtPrefix.Text)) { MessageBox.Show("Prefix is required."); return; }

        try
        {
            var pars = new Dictionary<string, object?>
            {
                ["type"] = docType,
                ["prefix"] = txtPrefix.Text.Trim(),
                ["suffix"] = txtSuffix.Text.Trim(),
                ["next"] = (int)numNextNumber.Value,
                ["pad"] = (int)numPadding.Value
            };

            // Upsert: works whether this is a brand-new type or an edit of an existing one
            DbHelper.ExecuteNonQuery(@"
                INSERT INTO document_numbering (doc_type, prefix, suffix, next_number, padding)
                VALUES (@type, @prefix, @suffix, @next, @pad)
                ON CONFLICT (doc_type) DO UPDATE SET
                    prefix = @prefix, suffix = @suffix, next_number = @next, padding = @pad", pars);

            ClearForm();
            ReloadGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void ClearForm()
    {
        editingDocType = null;
        cboDocType.Text = "";
        txtPrefix.Clear();
        txtSuffix.Clear();
        numNextNumber.Value = 1;
        numPadding.Value = 5;
    }
}
