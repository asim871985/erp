using System.ComponentModel;
using System.Data;
using ErpApp.Data;
using ErpApp.Models;

namespace ErpApp.Forms;

/// <summary>Matches the "Sales Invoice" window in screenshot 2 — header, line-item grid, totals.</summary>
public class SalesInvoiceForm : AppFormBase
{
    private readonly TextBox txtInvoiceNo = new() { ReadOnly = true };
    private readonly DateTimePicker dtInvoiceDate = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboCustomer = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtAddress = new() { ReadOnly = true };
    private readonly TextBox txtMobile = new() { ReadOnly = true };
    private readonly ComboBox cboPaymentTerms = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboSalesman = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboWarehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly DataGridView grid = new();
    private readonly BindingList<InvoiceLine> lines = new();

    private readonly Label lblSubTotal = new() { TextAlign = ContentAlignment.MiddleRight };
    private readonly Label lblDiscount = new() { TextAlign = ContentAlignment.MiddleRight };
    private readonly Label lblTax = new() { TextAlign = ContentAlignment.MiddleRight };
    private readonly Label lblGrandTotal = new() { TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Label lblAmountWords = new() { AutoSize = false, Height = 20 };

    private DataTable itemLookup = new();
    private int? currentInvoiceId;
    private bool isEditMode;

    public SalesInvoiceForm() : this(null) { }

    public SalesInvoiceForm(int? editInvoiceId)
    {
        Text = "Sales Invoice";
        Width = 950;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadLookups();

        if (editInvoiceId != null)
            LoadForEdit(editInvoiceId.Value);
        else
            NewInvoiceNo();
    }

    private void BuildLayout()
    {
        var header = new GroupBox { Text = "SALES INVOICE", Dock = DockStyle.Top, Height = 190 };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 5, Padding = new Padding(10) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        void L(int r, int c, string text) => t.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, c, r);
        void C(int r, int c, Control ctrl) { ctrl.Dock = DockStyle.Fill; t.Controls.Add(ctrl, c, r); }

        L(0, 0, "Invoice No."); C(0, 1, txtInvoiceNo);
        L(0, 2, "Invoice Date"); C(0, 3, dtInvoiceDate);

        L(1, 0, "Customer"); C(1, 1, cboCustomer);
        L(1, 2, "Address"); C(1, 3, txtAddress);

        L(2, 0, "Mobile"); C(2, 1, txtMobile);
        L(2, 2, "Payment Terms"); C(2, 3, cboPaymentTerms);

        L(3, 0, "Salesman"); C(3, 1, cboSalesman);
        L(3, 2, "Warehouse"); C(3, 3, cboWarehouse);

        cboCustomer.SelectedIndexChanged += CboCustomer_SelectedIndexChanged;
        cboPaymentTerms.Items.AddRange(new object[] { "Cash", "Credit" });
        cboPaymentTerms.SelectedIndex = 0;

        header.Controls.Add(t);

        var lineBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(5) };
        var btnAddLine = new Button { Text = "+ Add Line" };
        var btnRemoveLine = new Button { Text = "Remove Line" };
        btnAddLine.Click += (s, e) => AddLineViaDialog();
        btnRemoveLine.Click += (s, e) => RemoveSelectedLine();
        lineBtnPanel.Controls.Add(btnAddLine);
        lineBtnPanel.Controls.Add(btnRemoveLine);

        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SNo", HeaderText = "S.No", ReadOnly = true, FillWeight = 40 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "Item Name", DataPropertyName = "ItemName", ReadOnly = true, FillWeight = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Model", HeaderText = "Model", DataPropertyName = "Model", ReadOnly = true, FillWeight = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SideSize", HeaderText = "Side / Size", DataPropertyName = "SideSize", ReadOnly = true, FillWeight = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Brand", HeaderText = "Brand", DataPropertyName = "Brand", ReadOnly = true, FillWeight = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Uom", HeaderText = "UOM", DataPropertyName = "Uom", ReadOnly = true, FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "Qty", DataPropertyName = "Qty", FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rate", HeaderText = "Rate", DataPropertyName = "Rate", FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DiscPercent", HeaderText = "Disc %", DataPropertyName = "DiscPercent", FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Amount", DataPropertyName = "Amount", ReadOnly = true, FillWeight = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" } });
        grid.DataSource = lines;
        grid.CellEndEdit += Grid_CellEndEdit;
        grid.RowPostPaint += (s, e) => grid.Rows[e.RowIndex].Cells["SNo"].Value = (e.RowIndex + 1).ToString();

        var totalsPanel = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 130, ColumnCount = 2 };
        totalsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        totalsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var wordsPanel = new GroupBox { Text = "Amount in Words", Dock = DockStyle.Fill };
        lblAmountWords.Dock = DockStyle.Fill;
        lblAmountWords.Padding = new Padding(8);
        wordsPanel.Controls.Add(lblAmountWords);

        var sumsGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        for (int i = 0; i < sumsGrid.RowCount; i++) sumsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / sumsGrid.RowCount));
        sumsGrid.Controls.Add(new Label { Text = "Sub Total", Dock = DockStyle.Fill }, 0, 0);
        sumsGrid.Controls.Add(lblSubTotal, 1, 0);
        sumsGrid.Controls.Add(new Label { Text = "Discount", Dock = DockStyle.Fill }, 0, 1);
        sumsGrid.Controls.Add(lblDiscount, 1, 1);
        sumsGrid.Controls.Add(new Label { Text = "Tax", Dock = DockStyle.Fill }, 0, 2);
        sumsGrid.Controls.Add(lblTax, 1, 2);
        sumsGrid.Controls.Add(new Label { Text = "Grand Total", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold) }, 0, 3);
        sumsGrid.Controls.Add(lblGrandTotal, 1, 3);

        totalsPanel.Controls.Add(wordsPanel, 0, 0);
        totalsPanel.Controls.Add(sumsGrid, 1, 0);

        var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(8) };
        var btnNew = new Button { Text = "New" };
        var btnSave = new Button { Text = "Save" };
        var btnPrint = new Button { Text = "Print" };
        var btnEmail = new Button { Text = "Email" };
        var btnDelete = new Button { Text = "Delete" };
        var btnBrowse = new Button { Text = "Browse All..." };
        var btnCancel = new Button { Text = "Cancel" };
        btnNew.Click += (s, e) => ResetForm();
        btnSave.Click += BtnSave_Click;
        btnPrint.Click += (s, e) =>
        {
            if (currentInvoiceId == null) { MessageBox.Show("Save the invoice first."); return; }
            using var printForm = new InvoicePrintForm(currentInvoiceId.Value, isPurchase: false);
            printForm.ShowDialog(this);
        };
        btnEmail.Click += (s, e) => MessageBox.Show("Wire this up to SMTP / an email API to send the invoice.");
        btnDelete.Click += BtnDelete_Click;
        btnBrowse.Click += (s, e) =>
        {
            var list = new SalesInvoiceListForm();
            MdiHelper.ShowCentered(MdiParent, list);
        };
        btnCancel.Click += (s, e) => ResetForm();
        actionPanel.Controls.Add(btnNew);
        actionPanel.Controls.Add(btnSave);
        actionPanel.Controls.Add(btnPrint);
        actionPanel.Controls.Add(btnEmail);
        actionPanel.Controls.Add(btnDelete);
        actionPanel.Controls.Add(btnBrowse);
        actionPanel.Controls.Add(btnCancel);

        Controls.Add(grid);
        Controls.Add(totalsPanel);
        Controls.Add(actionPanel);
        Controls.Add(lineBtnPanel);
        Controls.Add(header);

        lines.ListChanged += (s, e) => RecalculateTotals();
    }

    private void LoadLookups()
    {
        try
        {
            var customers = DbHelper.ExecuteQuery("SELECT customer_id, customer_name, address, mobile FROM customer_master WHERE active ORDER BY customer_name");
            cboCustomer.DisplayMember = "customer_name";
            cboCustomer.ValueMember = "customer_id";
            cboCustomer.DataSource = customers;

            var warehouses = DbHelper.ExecuteQuery("SELECT warehouse_id, warehouse_name FROM warehouse_master WHERE active ORDER BY warehouse_name");
            cboWarehouse.DisplayMember = "warehouse_name";
            cboWarehouse.ValueMember = "warehouse_id";
            cboWarehouse.DataSource = warehouses;
            if (cboWarehouse.Items.Count > 0) cboWarehouse.SelectedIndex = 0;

            itemLookup = DbHelper.ExecuteQuery(@"
                SELECT i.item_id, i.item_name, i.model, i.side_size, b.brand_name, u.uom_name, i.rate
                FROM item_master i
                LEFT JOIN brand_master b ON b.brand_id = i.brand_id
                LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                WHERE i.active ORDER BY i.item_name");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load items for the invoice: " + ex.Message);
        }

        cboSalesman.Items.AddRange(new object[] { "Admin", "Salesman 1", "Salesman 2" });
        cboSalesman.SelectedIndex = 0;
    }

    private void CboCustomer_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cboCustomer.SelectedItem is DataRowView row)
        {
            txtAddress.Text = row["address"]?.ToString();
            txtMobile.Text = row["mobile"]?.ToString();
        }
    }

    private void NewInvoiceNo()
    {
        try
        {
            var num = DbHelper.ExecuteScalar("SELECT prefix || LPAD(next_number::text, padding, '0') || COALESCE(suffix,'') FROM document_numbering WHERE doc_type='INVOICE'");
            txtInvoiceNo.Text = num?.ToString() ?? "INV-00001";
        }
        catch { txtInvoiceNo.Text = "(auto on save)"; }
        dtInvoiceDate.Value = DateTime.Today;
    }

    private void LoadForEdit(int invoiceId)
    {
        try
        {
            var header = DbHelper.ExecuteQuery("SELECT * FROM sales_invoice WHERE invoice_id=@id", new() { ["id"] = invoiceId });
            if (header.Rows.Count == 0) { MessageBox.Show("That invoice no longer exists."); NewInvoiceNo(); return; }
            var h = header.Rows[0];

            isEditMode = true;
            currentInvoiceId = invoiceId;
            txtInvoiceNo.Text = h["invoice_no"].ToString();
            dtInvoiceDate.Value = Convert.ToDateTime(h["invoice_date"]);
            cboCustomer.SelectedValue = Convert.ToInt32(h["customer_id"]);
            txtAddress.Text = h["address"]?.ToString();
            txtMobile.Text = h["mobile"]?.ToString();
            cboPaymentTerms.Text = h["payment_terms"]?.ToString() ?? "Cash";
            if (h["salesman"] != DBNull.Value) cboSalesman.Text = h["salesman"].ToString();

            lines.Clear();
            var items = DbHelper.ExecuteQuery(@"
                SELECT si.item_id, i.item_name, i.model, i.side_size, b.brand_name, u.uom_name,
                       si.qty, si.rate, si.disc_percent
                FROM sales_invoice_item si
                LEFT JOIN item_master i ON i.item_id = si.item_id
                LEFT JOIN brand_master b ON b.brand_id = i.brand_id
                LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                WHERE si.invoice_id=@id ORDER BY si.line_id", new() { ["id"] = invoiceId });
            foreach (DataRow r in items.Rows)
            {
                lines.Add(new InvoiceLine
                {
                    ItemId = Convert.ToInt32(r["item_id"]),
                    ItemName = r["item_name"]?.ToString() ?? "",
                    Model = r["model"]?.ToString() ?? "",
                    SideSize = r["side_size"]?.ToString() ?? "",
                    Brand = r["brand_name"]?.ToString() ?? "",
                    Uom = r["uom_name"]?.ToString() ?? "",
                    Qty = Convert.ToDecimal(r["qty"]),
                    Rate = Convert.ToDecimal(r["rate"]),
                    DiscPercent = Convert.ToDecimal(r["disc_percent"])
                });
            }
            RecalculateTotals();
            Text = $"Sales Invoice — Editing {txtInvoiceNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load invoice for editing: " + ex.Message);
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
            SideSize = r["side_size"]?.ToString() ?? "",
            Brand = r["brand_name"]?.ToString() ?? "",
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

    private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        RecalculateTotals();
        grid.InvalidateColumn(grid.Columns["Amount"]!.Index);
    }

    private void RecalculateTotals()
    {
        decimal subTotal = lines.Sum(l => l.Qty * l.Rate);
        decimal discount = lines.Sum(l => l.Qty * l.Rate * l.DiscPercent / 100m);
        decimal tax = 0; // hook up tax_master % here if needed
        decimal grand = subTotal - discount + tax;

        lblSubTotal.Text = subTotal.ToString("N2");
        lblDiscount.Text = discount.ToString("N2");
        lblTax.Text = tax.ToString("N2");
        lblGrandTotal.Text = grand.ToString("N2");
        lblAmountWords.Text = NumberToWords.Convert(grand);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (cboCustomer.SelectedValue == null) { MessageBox.Show("Select a customer."); return; }
        if (cboWarehouse.SelectedValue == null) { MessageBox.Show("Select a warehouse."); return; }
        if (lines.Count == 0) { MessageBox.Show("Add at least one item line."); return; }

        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                decimal subTotal = lines.Sum(l => l.Qty * l.Rate);
                decimal discount = lines.Sum(l => l.Qty * l.Rate * l.DiscPercent / 100m);
                decimal grand = subTotal - discount;
                string words = NumberToWords.Convert(grand);

                string invoiceNo;
                int invoiceId;

                if (isEditMode && currentInvoiceId != null)
                {
                    // Undo the old lines' stock/ledger effect, then treat this like a fresh save
                    // against the same invoice_id/invoice_no.
                    invoiceId = currentInvoiceId.Value;
                    invoiceNo = txtInvoiceNo.Text;
                    DbHelper.ReverseSalesInvoicePostings(conn, tx, invoiceId);

                    using var updCmd = new Npgsql.NpgsqlCommand(@"
                        UPDATE sales_invoice SET invoice_date=@dt, customer_id=@cust, address=@addr, mobile=@mob,
                               payment_terms=@terms, salesman=@sales, sub_total=@sub, discount=@disc,
                               tax=0, grand_total=@grand, amount_in_words=@words
                        WHERE invoice_id=@id", conn, tx);
                    updCmd.Parameters.AddWithValue("dt", dtInvoiceDate.Value.Date);
                    updCmd.Parameters.AddWithValue("cust", (int)cboCustomer.SelectedValue!);
                    updCmd.Parameters.AddWithValue("addr", (object?)txtAddress.Text ?? "");
                    updCmd.Parameters.AddWithValue("mob", (object?)txtMobile.Text ?? "");
                    updCmd.Parameters.AddWithValue("terms", cboPaymentTerms.Text);
                    updCmd.Parameters.AddWithValue("sales", cboSalesman.Text);
                    updCmd.Parameters.AddWithValue("sub", subTotal);
                    updCmd.Parameters.AddWithValue("disc", discount);
                    updCmd.Parameters.AddWithValue("grand", grand);
                    updCmd.Parameters.AddWithValue("words", words);
                    updCmd.Parameters.AddWithValue("id", invoiceId);
                    updCmd.ExecuteNonQuery();
                }
                else
                {
                    invoiceNo = DbHelper.GetNextDocumentNumber(conn, tx, "INVOICE");
                    using var cmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO sales_invoice (invoice_no, invoice_date, customer_id, address, mobile,
                            payment_terms, salesman, sub_total, discount, tax, grand_total, amount_in_words)
                        VALUES (@no, @dt, @cust, @addr, @mob, @terms, @sales, @sub, @disc, 0, @grand, @words)
                        RETURNING invoice_id", conn, tx);
                    cmd.Parameters.AddWithValue("no", invoiceNo);
                    cmd.Parameters.AddWithValue("dt", dtInvoiceDate.Value.Date);
                    cmd.Parameters.AddWithValue("cust", (int)cboCustomer.SelectedValue!);
                    cmd.Parameters.AddWithValue("addr", (object?)txtAddress.Text ?? "");
                    cmd.Parameters.AddWithValue("mob", (object?)txtMobile.Text ?? "");
                    cmd.Parameters.AddWithValue("terms", cboPaymentTerms.Text);
                    cmd.Parameters.AddWithValue("sales", cboSalesman.Text);
                    cmd.Parameters.AddWithValue("sub", subTotal);
                    cmd.Parameters.AddWithValue("disc", discount);
                    cmd.Parameters.AddWithValue("grand", grand);
                    cmd.Parameters.AddWithValue("words", words);
                    invoiceId = (int)cmd.ExecuteScalar()!;
                }

                foreach (var line in lines)
                {
                    using var lineCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO sales_invoice_item (invoice_id, item_id, qty, rate, disc_percent, amount)
                        VALUES (@inv, @item, @qty, @rate, @disc, @amt)", conn, tx);
                    lineCmd.Parameters.AddWithValue("inv", invoiceId);
                    lineCmd.Parameters.AddWithValue("item", line.ItemId);
                    lineCmd.Parameters.AddWithValue("qty", line.Qty);
                    lineCmd.Parameters.AddWithValue("rate", line.Rate);
                    lineCmd.Parameters.AddWithValue("disc", line.DiscPercent);
                    lineCmd.Parameters.AddWithValue("amt", line.Amount);
                    lineCmd.ExecuteNonQuery();

                    int wh = (int)cboWarehouse.SelectedValue!;
                    using var stockCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                        VALUES (@item, @wh, 'OUT', @qty, 'SALES', @inv)", conn, tx);
                    stockCmd.Parameters.AddWithValue("item", line.ItemId);
                    stockCmd.Parameters.AddWithValue("wh", wh);
                    stockCmd.Parameters.AddWithValue("qty", line.Qty);
                    stockCmd.Parameters.AddWithValue("inv", invoiceId);
                    stockCmd.ExecuteNonQuery();

                    DbHelper.AdjustBalance(conn, tx, line.ItemId, wh, -line.Qty);
                }

                // Ledger: Debit customer (receivable up) / Credit Sales (income up)
                using var ledgerCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                    SELECT @no, 'Sales Invoice', account_id, 'To Sales Invoice', @grand, 0, @inv
                    FROM customer_master WHERE customer_id=@cust", conn, tx);
                ledgerCmd.Parameters.AddWithValue("no", invoiceNo);
                ledgerCmd.Parameters.AddWithValue("grand", grand);
                ledgerCmd.Parameters.AddWithValue("inv", invoiceId);
                ledgerCmd.Parameters.AddWithValue("cust", (int)cboCustomer.SelectedValue!);
                ledgerCmd.ExecuteNonQuery();

                int salesAccountId = DbHelper.GetAccountIdByCode(conn, tx, "4000");
                using var salesCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                    VALUES (@no, 'Sales Invoice', @acc, 'By Sales Invoice', 0, @grand, @inv)", conn, tx);
                salesCmd.Parameters.AddWithValue("no", invoiceNo);
                salesCmd.Parameters.AddWithValue("acc", salesAccountId);
                salesCmd.Parameters.AddWithValue("grand", grand);
                salesCmd.Parameters.AddWithValue("inv", invoiceId);
                salesCmd.ExecuteNonQuery();

                txtInvoiceNo.Text = invoiceNo;
                currentInvoiceId = invoiceId;
                isEditMode = true; // further Saves in this session update the same invoice
            });

            DbHelper.LogAction($"Sales Invoice: Saved {txtInvoiceNo.Text}");
            MessageBox.Show("Invoice saved successfully.");
            Text = $"Sales Invoice — Editing {txtInvoiceNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (currentInvoiceId == null) { MessageBox.Show("Nothing saved yet to delete."); return; }
        if (MessageBox.Show($"Delete invoice {txtInvoiceNo.Text}? This reverses its stock and ledger effect.",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        try
        {
            int invoiceId = currentInvoiceId.Value;
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                DbHelper.ReverseSalesInvoicePostings(conn, tx, invoiceId);
                using var delCmd = new Npgsql.NpgsqlCommand("DELETE FROM sales_invoice WHERE invoice_id=@id", conn, tx);
                delCmd.Parameters.AddWithValue("id", invoiceId);
                delCmd.ExecuteNonQuery();
            });
            DbHelper.LogAction($"Sales Invoice: Deleted #{invoiceId}");
            MessageBox.Show("Invoice deleted.");
            ResetForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message);
        }
    }

    private void ResetForm()
    {
        lines.Clear();
        currentInvoiceId = null;
        isEditMode = false;
        NewInvoiceNo();
        cboCustomer.SelectedIndex = -1;
        txtAddress.Clear();
        txtMobile.Clear();
        RecalculateTotals();
        Text = "Sales Invoice";
    }
}
