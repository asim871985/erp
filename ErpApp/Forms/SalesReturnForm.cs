using System.ComponentModel;
using System.Data;
using ErpApp.Data;
using ErpApp.Models;

namespace ErpApp.Forms;

/// <summary>Matches mockup form 4 "Sales Return".</summary>
public class SalesReturnForm : AppFormBase
{
    private readonly ComboBox cboCustomer = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtReturnNo = new() { ReadOnly = true };
    private readonly DateTimePicker dtReturnDate = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboSalesInvoice = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboWarehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtRemarks = new() { Multiline = true };

    private readonly DataGridView grid = new();
    private readonly BindingList<InvoiceLine> lines = new();
    private readonly Label lblTotalItems = new();
    private readonly Label lblTotalAmount = new() { Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };

    private DataTable itemLookup = new();
    private int? currentReturnId;
    private bool isEditMode;

    public SalesReturnForm() : this(null) { }

    public SalesReturnForm(int? editReturnId)
    {
        Text = "Sales Return";
        Width = 950;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadLookups();

        if (editReturnId != null)
            LoadForEdit(editReturnId.Value);
        else
            NewDocNo();
    }

    private void BuildLayout()
    {
        var header = new GroupBox { Text = "Return Information", Dock = DockStyle.Top, Height = 170 };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, Padding = new Padding(10) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        void L(int r, int c, string text) => t.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, c, r);
        void C(int r, int c, Control ctrl) { ctrl.Dock = DockStyle.Fill; t.Controls.Add(ctrl, c, r); }

        L(0, 0, "Customer"); C(0, 1, cboCustomer);
        txtRemarks.Height = 100;
        L(0, 2, "Remarks"); C(0, 3, txtRemarks);
        t.SetRowSpan(txtRemarks, 4);

        L(1, 0, "Return No."); C(1, 1, txtReturnNo);
        L(2, 0, "Return Date"); C(2, 1, dtReturnDate);
        L(3, 0, "Sales Invoice"); C(3, 1, cboSalesInvoice);

        cboCustomer.SelectedIndexChanged += (s, e) => LoadInvoicesForCustomer();

        header.Controls.Add(t);

        var lineBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(5) };
        var btnAddLine = new Button { Text = "+ Add Item" };
        var btnRemoveLine = new Button { Text = "Remove Item" };
        btnAddLine.Click += (s, e) => AddLineViaDialog();
        btnRemoveLine.Click += (s, e) => RemoveSelectedLine();
        lineBtnPanel.Controls.Add(btnAddLine);
        lineBtnPanel.Controls.Add(btnRemoveLine);

        BuildGrid();

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        lblTotalItems.Location = new Point(10, 10);
        lblTotalItems.AutoSize = true;
        lblTotalAmount.Dock = DockStyle.Right;
        lblTotalAmount.Width = 300;
        bottomPanel.Controls.Add(lblTotalItems);
        bottomPanel.Controls.Add(lblTotalAmount);

        var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(8) };
        var btnNew = new Button { Text = "+ New" };
        var btnSave = new Button { Text = "Save" };
        var btnCancel = new Button { Text = "Cancel" };
        var btnDelete = new Button { Text = "Delete" };
        var btnPrint = new Button { Text = "Print" };
        var btnBrowse = new Button { Text = "Browse All..." };
        btnNew.Click += (s, e) => ResetForm();
        btnSave.Click += BtnSave_Click;
        btnCancel.Click += (s, e) => ResetForm();
        btnDelete.Click += BtnDelete_Click;
        btnPrint.Click += BtnPrint_Click;
        btnBrowse.Click += (s, e) =>
        {
            var list = new SalesReturnListForm();
            MdiHelper.ShowCentered(MdiParent, list);
        };
        actionPanel.Controls.Add(btnNew);
        actionPanel.Controls.Add(btnSave);
        actionPanel.Controls.Add(btnCancel);
        actionPanel.Controls.Add(btnDelete);
        actionPanel.Controls.Add(btnPrint);
        actionPanel.Controls.Add(btnBrowse);

        // The header grid's Remarks field spans every row, so the warehouse picker
        // lives on its own slim row just below the header.
        var whRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 35, ColumnCount = 2 };
        whRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        whRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
        whRow.Controls.Add(new Label { Text = "Warehouse", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        cboWarehouse.Dock = DockStyle.Fill;
        whRow.Controls.Add(cboWarehouse, 1, 0);

        Controls.Add(grid);
        Controls.Add(bottomPanel);
        Controls.Add(actionPanel);
        Controls.Add(lineBtnPanel);
        Controls.Add(whRow);
        Controls.Add(header);

        lines.ListChanged += (s, e) => RecalculateTotals();
    }

    private void BuildGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SNo", HeaderText = "S.No", ReadOnly = true, FillWeight = 40 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "Item Name", DataPropertyName = "ItemName", ReadOnly = true, FillWeight = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Model", HeaderText = "Model", DataPropertyName = "Model", ReadOnly = true, FillWeight = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Uom", HeaderText = "UOM", DataPropertyName = "Uom", ReadOnly = true, FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "Qty", DataPropertyName = "Qty", FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rate", HeaderText = "Rate", DataPropertyName = "Rate", FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DiscPercent", HeaderText = "Disc %", DataPropertyName = "DiscPercent", FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Amount", DataPropertyName = "Amount", ReadOnly = true, FillWeight = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" } });
        grid.DataSource = lines;
        grid.CellEndEdit += (s, e) => RecalculateTotals();
        grid.RowPostPaint += (s, e) => grid.Rows[e.RowIndex].Cells["SNo"].Value = (e.RowIndex + 1).ToString();
    }

    private void LoadLookups()
    {
        try
        {
            var customers = DbHelper.ExecuteQuery("SELECT customer_id, customer_name FROM customer_master WHERE active ORDER BY customer_name");
            cboCustomer.DisplayMember = "customer_name";
            cboCustomer.ValueMember = "customer_id";
            cboCustomer.DataSource = customers;

            var warehouses = DbHelper.ExecuteQuery("SELECT warehouse_id, warehouse_name FROM warehouse_master WHERE active ORDER BY warehouse_name");
            cboWarehouse.DisplayMember = "warehouse_name";
            cboWarehouse.ValueMember = "warehouse_id";
            cboWarehouse.DataSource = warehouses;
            if (cboWarehouse.Items.Count > 0) cboWarehouse.SelectedIndex = 0;

            itemLookup = DbHelper.ExecuteQuery(@"
                SELECT i.item_id, i.item_name, i.model, u.uom_name, i.rate
                FROM item_master i
                LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                WHERE i.active ORDER BY i.item_name");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load customers/items: " + ex.Message);
        }
    }

    private void LoadInvoicesForCustomer()
    {
        cboSalesInvoice.DataSource = null;
        if (cboCustomer.SelectedValue is not int custId) return;

        try
        {
            var invoices = DbHelper.ExecuteQuery(
                "SELECT invoice_id, invoice_no FROM sales_invoice WHERE customer_id=@id ORDER BY invoice_date DESC",
                new() { ["id"] = custId });
            cboSalesInvoice.DisplayMember = "invoice_no";
            cboSalesInvoice.ValueMember = "invoice_id";
            cboSalesInvoice.DataSource = invoices;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load sales invoices: " + ex.Message);
        }
    }

    private void NewDocNo()
    {
        try
        {
            var num = DbHelper.ExecuteScalar("SELECT prefix || LPAD(next_number::text, padding, '0') || COALESCE(suffix,'') FROM document_numbering WHERE doc_type='SALES_RETURN'");
            txtReturnNo.Text = num?.ToString() ?? "SR-00001";
        }
        catch { txtReturnNo.Text = "(auto on save)"; }
        dtReturnDate.Value = DateTime.Today;
    }

    private void LoadForEdit(int returnId)
    {
        try
        {
            var header = DbHelper.ExecuteQuery("SELECT * FROM sales_return WHERE return_id=@id", new() { ["id"] = returnId });
            if (header.Rows.Count == 0) { MessageBox.Show("That sales return no longer exists."); NewDocNo(); return; }
            var h = header.Rows[0];

            isEditMode = true;
            currentReturnId = returnId;
            txtReturnNo.Text = h["return_no"].ToString();
            dtReturnDate.Value = Convert.ToDateTime(h["return_date"]);
            cboCustomer.SelectedValue = Convert.ToInt32(h["customer_id"]);
            LoadInvoicesForCustomer();
            if (h["invoice_id"] != DBNull.Value) cboSalesInvoice.SelectedValue = Convert.ToInt32(h["invoice_id"]);
            txtRemarks.Text = h["remarks"]?.ToString();

            lines.Clear();
            var items = DbHelper.ExecuteQuery(@"
                SELECT sri.item_id, i.item_name, i.model, u.uom_name, sri.qty, sri.rate, sri.disc_percent
                FROM sales_return_item sri
                LEFT JOIN item_master i ON i.item_id = sri.item_id
                LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                WHERE sri.return_id=@id ORDER BY sri.line_id", new() { ["id"] = returnId });
            foreach (DataRow r in items.Rows)
            {
                lines.Add(new InvoiceLine
                {
                    ItemId = Convert.ToInt32(r["item_id"]),
                    ItemName = r["item_name"]?.ToString() ?? "",
                    Model = r["model"]?.ToString() ?? "",
                    Uom = r["uom_name"]?.ToString() ?? "",
                    Qty = Convert.ToDecimal(r["qty"]),
                    Rate = Convert.ToDecimal(r["rate"]),
                    DiscPercent = Convert.ToDecimal(r["disc_percent"])
                });
            }
            RecalculateTotals();
            Text = $"Sales Return — Editing {txtReturnNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load sales return for editing: " + ex.Message);
        }
    }

    private void AddLineViaDialog()
    {
        using var picker = new ItemPickerDialog(itemLookup);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedRow == null) return;

        var r = picker.SelectedRow;
        lines.Add(new InvoiceLine
        {
            ItemId = Convert.ToInt32(r["item_id"]),
            ItemName = r["item_name"].ToString() ?? "",
            Model = r["model"]?.ToString() ?? "",
            Uom = r["uom_name"]?.ToString() ?? "",
            Qty = 1,
            Rate = r["rate"] is DBNull ? 0 : Convert.ToDecimal(r["rate"]),
            DiscPercent = 0
        });
    }

    private void RemoveSelectedLine()
    {
        if (grid.CurrentRow?.DataBoundItem is InvoiceLine line)
            lines.Remove(line);
    }

    private void RecalculateTotals()
    {
        lblTotalItems.Text = $"Total Items: {lines.Count}";
        decimal total = lines.Sum(l => l.Amount);
        lblTotalAmount.Text = "Total Amount: " + total.ToString("N2");
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (cboCustomer.SelectedValue == null) { MessageBox.Show("Select a customer."); return; }
        if (cboWarehouse.SelectedValue == null) { MessageBox.Show("Select a warehouse."); return; }
        if (lines.Count == 0) { MessageBox.Show("Add at least one item."); return; }

        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                decimal total = lines.Sum(l => l.Amount);
                int? invoiceId = cboSalesInvoice.SelectedValue as int?;

                string returnNo;
                int returnId;

                if (isEditMode && currentReturnId != null)
                {
                    returnId = currentReturnId.Value;
                    returnNo = txtReturnNo.Text;
                    DbHelper.ReverseSalesReturnPostings(conn, tx, returnId);

                    using var updCmd = new Npgsql.NpgsqlCommand(@"
                        UPDATE sales_return SET return_date=@dt, invoice_id=@invid, customer_id=@cust,
                               remarks=@remarks, total_amount=@total
                        WHERE return_id=@id", conn, tx);
                    updCmd.Parameters.AddWithValue("dt", dtReturnDate.Value.Date);
                    updCmd.Parameters.AddWithValue("invid", (object?)invoiceId ?? DBNull.Value);
                    updCmd.Parameters.AddWithValue("cust", (int)cboCustomer.SelectedValue!);
                    updCmd.Parameters.AddWithValue("remarks", (object?)txtRemarks.Text.Trim() ?? "");
                    updCmd.Parameters.AddWithValue("total", total);
                    updCmd.Parameters.AddWithValue("id", returnId);
                    updCmd.ExecuteNonQuery();
                }
                else
                {
                    returnNo = DbHelper.GetNextDocumentNumber(conn, tx, "SALES_RETURN");
                    using var cmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO sales_return (return_no, return_date, invoice_id, customer_id, remarks, total_amount)
                        VALUES (@no, @dt, @invid, @cust, @remarks, @total)
                        RETURNING return_id", conn, tx);
                    cmd.Parameters.AddWithValue("no", returnNo);
                    cmd.Parameters.AddWithValue("dt", dtReturnDate.Value.Date);
                    cmd.Parameters.AddWithValue("invid", (object?)invoiceId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cust", (int)cboCustomer.SelectedValue!);
                    cmd.Parameters.AddWithValue("remarks", (object?)txtRemarks.Text.Trim() ?? "");
                    cmd.Parameters.AddWithValue("total", total);
                    returnId = (int)cmd.ExecuteScalar()!;
                }

                foreach (var line in lines)
                {
                    using var lineCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO sales_return_item (return_id, item_id, qty, rate, disc_percent, amount)
                        VALUES (@r, @item, @qty, @rate, @disc, @amt)", conn, tx);
                    lineCmd.Parameters.AddWithValue("r", returnId);
                    lineCmd.Parameters.AddWithValue("item", line.ItemId);
                    lineCmd.Parameters.AddWithValue("qty", line.Qty);
                    lineCmd.Parameters.AddWithValue("rate", line.Rate);
                    lineCmd.Parameters.AddWithValue("disc", line.DiscPercent);
                    lineCmd.Parameters.AddWithValue("amt", line.Amount);
                    lineCmd.ExecuteNonQuery();

                    // Returned goods come back into our stock
                    int wh = (int)cboWarehouse.SelectedValue!;
                    using var stockCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                        VALUES (@item, @wh, 'IN', @qty, 'SALES_RETURN', @r)", conn, tx);
                    stockCmd.Parameters.AddWithValue("item", line.ItemId);
                    stockCmd.Parameters.AddWithValue("wh", wh);
                    stockCmd.Parameters.AddWithValue("qty", line.Qty);
                    stockCmd.Parameters.AddWithValue("r", returnId);
                    stockCmd.ExecuteNonQuery();

                    DbHelper.AdjustBalance(conn, tx, line.ItemId, wh, line.Qty);
                }

                // Ledger: Credit customer (receivable down) / Debit Sales (income down)
                using var ledgerCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                    SELECT @no, 'Sales Return', account_id, 'By Sales Return', 0, @total, @r
                    FROM customer_master WHERE customer_id=@cust", conn, tx);
                ledgerCmd.Parameters.AddWithValue("no", returnNo);
                ledgerCmd.Parameters.AddWithValue("total", total);
                ledgerCmd.Parameters.AddWithValue("r", returnId);
                ledgerCmd.Parameters.AddWithValue("cust", (int)cboCustomer.SelectedValue!);
                ledgerCmd.ExecuteNonQuery();

                int salesAccountId = DbHelper.GetAccountIdByCode(conn, tx, "4000");
                using var salesCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                    VALUES (@no, 'Sales Return', @acc, 'To Sales Return', @total, 0, @r)", conn, tx);
                salesCmd.Parameters.AddWithValue("no", returnNo);
                salesCmd.Parameters.AddWithValue("acc", salesAccountId);
                salesCmd.Parameters.AddWithValue("total", total);
                salesCmd.Parameters.AddWithValue("r", returnId);
                salesCmd.ExecuteNonQuery();

                txtReturnNo.Text = returnNo;
                currentReturnId = returnId;
                isEditMode = true;
            });

            DbHelper.LogAction($"Sales Return: Saved {txtReturnNo.Text}");
            MessageBox.Show("Sales return saved successfully.");
            Text = $"Sales Return — Editing {txtReturnNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (currentReturnId == null) { MessageBox.Show("Nothing saved yet to delete."); return; }
        if (MessageBox.Show($"Delete sales return {txtReturnNo.Text}? This reverses its stock and ledger effect.",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            int returnId = currentReturnId.Value;
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                DbHelper.ReverseSalesReturnPostings(conn, tx, returnId);
                using var delCmd = new Npgsql.NpgsqlCommand("DELETE FROM sales_return WHERE return_id=@id", conn, tx);
                delCmd.Parameters.AddWithValue("id", returnId);
                delCmd.ExecuteNonQuery();
            });
            DbHelper.LogAction($"Sales Return: Deleted #{returnId}");
            MessageBox.Show("Sales return deleted.");
            ResetForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message);
        }
    }

    private void BtnPrint_Click(object? sender, EventArgs e)
    {
        if (currentReturnId == null) { MessageBox.Show("Save the return first."); return; }
        using var printForm = new InvoicePrintForm(currentReturnId.Value, PrintDocType.SalesReturn);
        printForm.ShowDialog(this);
    }

    private void ResetForm()
    {
        lines.Clear();
        currentReturnId = null;
        isEditMode = false;
        NewDocNo();
        cboCustomer.SelectedIndex = -1;
        txtRemarks.Clear();
        RecalculateTotals();
        Text = "Sales Return";
    }
}
