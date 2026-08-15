using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Mirror of <see cref="SalesInvoiceListForm"/> for Purchase Bills.</summary>
public class PurchaseInvoiceListForm : AppFormBase
{
    private readonly TextBox txtSearch = new() { Width = 200 };
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly DataGridView grid = new();
    private readonly Label lblHint = new() { AutoSize = true, ForeColor = Color.Gray };

    public PurchaseInvoiceListForm()
    {
        Text = "Purchase Invoice List";
        Width = 1000;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadGrid();
    }

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        var btnNew = new Button { Text = "+ New Purchase" };
        var btnEdit = new Button { Text = "Edit" };
        var btnDelete = new Button { Text = "Delete Selected" };
        var btnPrint = new Button { Text = "Print Selected" };
        var btnRefresh = new Button { Text = "Refresh" };
        btnNew.Click += (s, e) => OpenChild(new PurchaseForm());
        btnEdit.Click += (s, e) => EditSelected();
        btnDelete.Click += (s, e) => DeleteSelected();
        btnPrint.Click += (s, e) => PrintSelected();
        btnRefresh.Click += (s, e) => LoadGrid();
        top.Controls.Add(btnNew);
        top.Controls.Add(btnEdit);
        top.Controls.Add(btnDelete);
        top.Controls.Add(btnPrint);
        top.Controls.Add(btnRefresh);

        var filterPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8) };
        filterPanel.Controls.Add(new Label { Text = "Search", AutoSize = true, Padding = new Padding(0, 6, 5, 0) });
        filterPanel.Controls.Add(txtSearch);
        filterPanel.Controls.Add(new Label { Text = "From", AutoSize = true, Padding = new Padding(10, 6, 5, 0) });
        dtFrom.Value = new DateTime(DateTime.Today.Year, 1, 1);
        filterPanel.Controls.Add(dtFrom);
        filterPanel.Controls.Add(new Label { Text = "To", AutoSize = true, Padding = new Padding(10, 6, 5, 0) });
        dtTo.Value = DateTime.Today;
        filterPanel.Controls.Add(dtTo);
        var btnSearch = new Button { Text = "Search" };
        btnSearch.Click += (s, e) => LoadGrid();
        filterPanel.Controls.Add(btnSearch);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = true;
        grid.CellDoubleClick += (s, e) => EditSelected();

        lblHint.Text = "Tip: Ctrl+Click or Shift+Click to select multiple rows for bulk Delete/Print.";
        lblHint.Dock = DockStyle.Bottom;
        lblHint.Padding = new Padding(8, 4, 0, 4);

        Controls.Add(grid);
        Controls.Add(lblHint);
        Controls.Add(filterPanel);
        Controls.Add(top);
    }

    private void OpenChild(Form child)
    {
        MdiHelper.ShowCentered(MdiParent, child);
    }

    private void LoadGrid()
    {
        try
        {
            string sql = @"
                SELECT pb.purchase_id AS ""ID"", pb.bill_no AS ""Bill No."", pb.bill_date AS ""Date"",
                       s.supplier_name AS ""Supplier"", pb.due_date AS ""Due Date"",
                       pb.grand_total AS ""Grand Total""
                FROM purchase_bill pb
                LEFT JOIN supplier_master s ON s.supplier_id = pb.supplier_id
                WHERE pb.bill_date BETWEEN @from AND @to";
            var pars = new Dictionary<string, object?> { ["from"] = dtFrom.Value.Date, ["to"] = dtTo.Value.Date };

            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                sql += " AND (pb.bill_no ILIKE @s OR s.supplier_name ILIKE @s)";
                pars["s"] = $"%{txtSearch.Text.Trim()}%";
            }
            sql += " ORDER BY pb.bill_date DESC, pb.purchase_id DESC";

            grid.DataSource = DbHelper.ExecuteQuery(sql, pars);
            if (grid.Columns.Contains("ID")) grid.Columns["ID"]!.Visible = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load purchase bills: " + ex.Message);
        }
    }

    private List<int> SelectedIds() =>
        grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => Convert.ToInt32(r.Cells["ID"].Value))
            .Distinct().ToList();

    private void EditSelected()
    {
        var ids = SelectedIds();
        if (ids.Count == 0) { MessageBox.Show("Select a bill to edit."); return; }
        if (ids.Count > 1) { MessageBox.Show("Select only one bill to edit."); return; }
        OpenChild(new PurchaseForm(ids[0]));
    }

    private void DeleteSelected()
    {
        var ids = SelectedIds();
        if (ids.Count == 0) { MessageBox.Show("Select one or more bills to delete."); return; }

        string msg = ids.Count == 1
            ? "Delete this purchase bill? This reverses its stock and ledger effect."
            : $"Delete these {ids.Count} purchase bills? This reverses each one's stock and ledger effect.";
        if (MessageBox.Show(msg, "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        try
        {
            foreach (var id in ids)
            {
                DbHelper.ExecuteTransaction((conn, tx) =>
                {
                    DbHelper.ReversePurchaseBillPostings(conn, tx, id);
                    using var delCmd = new Npgsql.NpgsqlCommand("DELETE FROM purchase_bill WHERE purchase_id=@id", conn, tx);
                    delCmd.Parameters.AddWithValue("id", id);
                    delCmd.ExecuteNonQuery();
                });
                DbHelper.LogAction($"Purchase Bill: Deleted #{id}");
            }
            MessageBox.Show($"Deleted {ids.Count} purchase bill(s).");
            LoadGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message);
        }
    }

    private void PrintSelected()
    {
        var ids = SelectedIds();
        if (ids.Count == 0) { MessageBox.Show("Select one or more bills to print."); return; }

        if (ids.Count == 1)
        {
            using var printForm = new InvoicePrintForm(ids[0], isPurchase: true);
            printForm.ShowDialog(this);
        }
        else
        {
            BatchInvoicePrinter.PrintOrPreview(this, ids, isPurchase: true);
        }
    }
}
