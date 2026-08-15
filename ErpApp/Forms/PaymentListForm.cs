using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Browse/search all Payments; Edit, Delete, Print (single or bulk).</summary>
public class PaymentListForm : AppFormBase
{
    private readonly TextBox txtSearch = new() { Width = 200 };
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly DataGridView grid = new();
    private readonly Label lblHint = new() { AutoSize = true, ForeColor = Color.Gray };

    public PaymentListForm()
    {
        Text = "Payment List";
        Width = 950;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadGrid();
    }

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        var btnNew = new Button { Text = "+ New Payment" };
        var btnEdit = new Button { Text = "Edit" };
        var btnDelete = new Button { Text = "Delete Selected" };
        var btnPrint = new Button { Text = "Print Selected" };
        var btnRefresh = new Button { Text = "Refresh" };
        btnNew.Click += (s, e) => OpenChild(new PaymentForm());
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
                SELECT pv.payment_id AS ""ID"", pv.payment_no AS ""Payment No."", pv.payment_date AS ""Date"",
                       a.account_name AS ""Account"", pv.payment_mode AS ""Mode"", pv.paid_by AS ""Paid By"",
                       pv.amount AS ""Amount""
                FROM payment_voucher pv
                LEFT JOIN chart_of_accounts a ON a.account_id = pv.account_id
                WHERE pv.payment_date BETWEEN @from AND @to";
            var pars = new Dictionary<string, object?> { ["from"] = dtFrom.Value.Date, ["to"] = dtTo.Value.Date };

            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                sql += " AND (pv.payment_no ILIKE @s OR a.account_name ILIKE @s)";
                pars["s"] = $"%{txtSearch.Text.Trim()}%";
            }
            sql += " ORDER BY pv.payment_date DESC, pv.payment_id DESC";

            grid.DataSource = DbHelper.ExecuteQuery(sql, pars);
            if (grid.Columns.Contains("ID")) grid.Columns["ID"]!.Visible = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load payments: " + ex.Message);
        }
    }

    private List<int> SelectedIds() =>
        grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => Convert.ToInt32(r.Cells["ID"].Value))
            .Distinct().ToList();

    private void EditSelected()
    {
        var ids = SelectedIds();
        if (ids.Count == 0) { MessageBox.Show("Select a payment to edit."); return; }
        if (ids.Count > 1) { MessageBox.Show("Select only one payment to edit."); return; }
        OpenChild(new PaymentForm(ids[0]));
    }

    private void DeleteSelected()
    {
        var ids = SelectedIds();
        if (ids.Count == 0) { MessageBox.Show("Select one or more payments to delete."); return; }

        string msg = ids.Count == 1 ? "Delete this payment?" : $"Delete these {ids.Count} payments?";
        if (MessageBox.Show(msg, "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        try
        {
            foreach (var id in ids)
            {
                var noResult = DbHelper.ExecuteScalar("SELECT payment_no FROM payment_voucher WHERE payment_id=@id", new() { ["id"] = id });
                string paymentNo = noResult?.ToString() ?? "";
                DbHelper.ExecuteTransaction((conn, tx) =>
                {
                    DbHelper.ReversePaymentPostings(conn, tx, paymentNo);
                    using var delCmd = new Npgsql.NpgsqlCommand("DELETE FROM payment_voucher WHERE payment_id=@id", conn, tx);
                    delCmd.Parameters.AddWithValue("id", id);
                    delCmd.ExecuteNonQuery();
                });
                DbHelper.LogAction($"Payment: Deleted {paymentNo}");
            }
            MessageBox.Show($"Deleted {ids.Count} payment(s).");
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
        if (ids.Count == 0) { MessageBox.Show("Select one or more payments to print."); return; }

        if (ids.Count == 1)
        {
            using var printForm = new VoucherPrintForm(ids[0], VoucherType.Payment);
            printForm.ShowDialog(this);
        }
        else
        {
            BatchVoucherPrinter.PrintOrPreview(this, ids, VoucherType.Payment);
        }
    }
}
