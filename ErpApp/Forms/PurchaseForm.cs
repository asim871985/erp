using System.ComponentModel;
using System.Data;
using ErpApp.Data;
using ErpApp.Models;

namespace ErpApp.Forms;

/// <summary>Matches mockup form 1 "Purchase" — supplier bill entry with item lines.</summary>
public class PurchaseForm : AppFormBase
{
    private readonly TextBox txtInvoiceNo = new() { ReadOnly = true };
    private readonly DateTimePicker dtInvoiceDate = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboSupplier = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtRefNo = new();
    private readonly ComboBox cboCreditDays = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly DateTimePicker dtDueDate = new() { Format = DateTimePickerFormat.Short };
    private readonly TextBox txtRemarks = new() { Multiline = true };
    private readonly ComboBox cboWarehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly DataGridView grid = new();
    private readonly BindingList<InvoiceLine> lines = new();
    private readonly Label lblTotalItems = new();
    private readonly Label lblTotalAmount = new() { Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };

    private DataTable itemLookup = new();
    private int? currentPurchaseId;
    private bool isEditMode;

    public PurchaseForm() : this(null) { }

    public PurchaseForm(int? editPurchaseId)
    {
        Text = "Purchase";
        Width = 950;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadLookups();

        if (editPurchaseId != null)
            LoadForEdit(editPurchaseId.Value);
        else
            NewDocNo();
    }

    private void BuildLayout()
    {
        var header = new GroupBox { Text = "Purchase Information", Dock = DockStyle.Top, Height = 170 };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, Padding = new Padding(10) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        void L(int r, int c, string text) => t.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, c, r);
        void C(int r, int c, Control ctrl, int span = 1) { ctrl.Dock = DockStyle.Fill; t.Controls.Add(ctrl, c, r); if (span > 1) t.SetColumnSpan(ctrl, span); }

        L(0, 0, "Supplier"); C(0, 1, cboSupplier);
        txtRemarks.Height = 60;
        L(0, 2, "Remarks"); C(0, 3, txtRemarks);
        t.SetRowSpan(txtRemarks, 3);

        L(1, 0, "Invoice No."); C(1, 1, txtInvoiceNo);
        L(2, 0, "Invoice Date"); C(2, 1, dtInvoiceDate);
        L(3, 0, "Ref No"); C(3, 1, txtRefNo);
        L(3, 2, "Warehouse"); C(3, 3, cboWarehouse);

        cboCreditDays.Items.AddRange(new object[] { "0", "7", "15", "30", "45", "60", "90" });
        cboCreditDays.Text = "30";
        cboCreditDays.TextChanged += (s, e) => RecalculateDueDate();
        dtInvoiceDate.ValueChanged += (s, e) => RecalculateDueDate();

        header.Controls.Add(t);

        var creditRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 35, ColumnCount = 4 };
        for (int i = 0; i < 4; i++) creditRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        creditRow.Controls.Add(new Label { Text = "Credit (Days)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        cboCreditDays.Dock = DockStyle.Fill;
        creditRow.Controls.Add(cboCreditDays, 1, 0);
        creditRow.Controls.Add(new Label { Text = "Due Date", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        dtDueDate.Dock = DockStyle.Fill;
        creditRow.Controls.Add(dtDueDate, 3, 0);

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
            var list = new PurchaseInvoiceListForm();
            MdiHelper.ShowCentered(MdiParent, list);
        };
        actionPanel.Controls.Add(btnNew);
        actionPanel.Controls.Add(btnSave);
        actionPanel.Controls.Add(btnCancel);
        actionPanel.Controls.Add(btnDelete);
        actionPanel.Controls.Add(btnPrint);
        actionPanel.Controls.Add(btnBrowse);

        Controls.Add(grid);
        Controls.Add(bottomPanel);
        Controls.Add(actionPanel);
        Controls.Add(lineBtnPanel);
        Controls.Add(creditRow);
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
            var suppliers = DbHelper.ExecuteQuery("SELECT supplier_id, supplier_name FROM supplier_master WHERE active ORDER BY supplier_name");
            cboSupplier.DisplayMember = "supplier_name";
            cboSupplier.ValueMember = "supplier_id";
            cboSupplier.DataSource = suppliers;

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
            MessageBox.Show("Could not load suppliers/items: " + ex.Message);
        }
    }

    private void NewDocNo()
    {
        try
        {
            var num = DbHelper.ExecuteScalar("SELECT prefix || LPAD(next_number::text, padding, '0') || COALESCE(suffix,'') FROM document_numbering WHERE doc_type='PURCHASE'");
            txtInvoiceNo.Text = num?.ToString() ?? "PB-00001";
        }
        catch { txtInvoiceNo.Text = "(auto on save)"; }
        dtInvoiceDate.Value = DateTime.Today;
        RecalculateDueDate();
    }

    private void LoadForEdit(int purchaseId)
    {
        try
        {
            var header = DbHelper.ExecuteQuery("SELECT * FROM purchase_bill WHERE purchase_id=@id", new() { ["id"] = purchaseId });
            if (header.Rows.Count == 0) { MessageBox.Show("That purchase bill no longer exists."); NewDocNo(); return; }
            var h = header.Rows[0];

            isEditMode = true;
            currentPurchaseId = purchaseId;
            txtInvoiceNo.Text = h["bill_no"].ToString();
            dtInvoiceDate.Value = Convert.ToDateTime(h["bill_date"]);
            cboSupplier.SelectedValue = Convert.ToInt32(h["supplier_id"]);
            txtRefNo.Text = h["ref_no"]?.ToString();
            cboCreditDays.Text = h["credit_days"]?.ToString() ?? "0";
            if (h["due_date"] != DBNull.Value) dtDueDate.Value = Convert.ToDateTime(h["due_date"]);
            txtRemarks.Text = h["remarks"]?.ToString();

            lines.Clear();
            var items = DbHelper.ExecuteQuery(@"
                SELECT pi.item_id, i.item_name, i.model, u.uom_name, pi.qty, pi.rate, pi.disc_percent
                FROM purchase_bill_item pi
                LEFT JOIN item_master i ON i.item_id = pi.item_id
                LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                WHERE pi.purchase_id=@id ORDER BY pi.line_id", new() { ["id"] = purchaseId });
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
            Text = $"Purchase — Editing {txtInvoiceNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load purchase bill for editing: " + ex.Message);
        }
    }

    private void RecalculateDueDate()
    {
        if (int.TryParse(cboCreditDays.Text, out int days))
            dtDueDate.Value = dtInvoiceDate.Value.Date.AddDays(days);
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
        if (cboSupplier.SelectedValue == null) { MessageBox.Show("Select a supplier."); return; }
        if (cboWarehouse.SelectedValue == null) { MessageBox.Show("Select a warehouse."); return; }
        if (lines.Count == 0) { MessageBox.Show("Add at least one item."); return; }

        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                decimal subTotal = lines.Sum(l => l.Qty * l.Rate);
                decimal discount = lines.Sum(l => l.Qty * l.Rate * l.DiscPercent / 100m);
                decimal grand = subTotal - discount;

                string billNo;
                int purchaseId;

                if (isEditMode && currentPurchaseId != null)
                {
                    purchaseId = currentPurchaseId.Value;
                    billNo = txtInvoiceNo.Text;
                    DbHelper.ReversePurchaseBillPostings(conn, tx, purchaseId);

                    using var updCmd = new Npgsql.NpgsqlCommand(@"
                        UPDATE purchase_bill SET bill_date=@dt, supplier_id=@sup, ref_no=@ref, credit_days=@credit,
                               due_date=@due, remarks=@remarks, sub_total=@sub, discount=@disc, tax=0, grand_total=@grand
                        WHERE purchase_id=@id", conn, tx);
                    updCmd.Parameters.AddWithValue("dt", dtInvoiceDate.Value.Date);
                    updCmd.Parameters.AddWithValue("sup", (int)cboSupplier.SelectedValue!);
                    updCmd.Parameters.AddWithValue("ref", (object?)txtRefNo.Text.Trim() ?? "");
                    updCmd.Parameters.AddWithValue("credit", int.TryParse(cboCreditDays.Text, out int d1) ? d1 : 0);
                    updCmd.Parameters.AddWithValue("due", dtDueDate.Value.Date);
                    updCmd.Parameters.AddWithValue("remarks", (object?)txtRemarks.Text.Trim() ?? "");
                    updCmd.Parameters.AddWithValue("sub", subTotal);
                    updCmd.Parameters.AddWithValue("disc", discount);
                    updCmd.Parameters.AddWithValue("grand", grand);
                    updCmd.Parameters.AddWithValue("id", purchaseId);
                    updCmd.ExecuteNonQuery();
                }
                else
                {
                    billNo = DbHelper.GetNextDocumentNumber(conn, tx, "PURCHASE");
                    using var cmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO purchase_bill (bill_no, bill_date, supplier_id, ref_no, credit_days, due_date, remarks, sub_total, discount, tax, grand_total)
                        VALUES (@no, @dt, @sup, @ref, @credit, @due, @remarks, @sub, @disc, 0, @grand)
                        RETURNING purchase_id", conn, tx);
                    cmd.Parameters.AddWithValue("no", billNo);
                    cmd.Parameters.AddWithValue("dt", dtInvoiceDate.Value.Date);
                    cmd.Parameters.AddWithValue("sup", (int)cboSupplier.SelectedValue!);
                    cmd.Parameters.AddWithValue("ref", (object?)txtRefNo.Text.Trim() ?? "");
                    cmd.Parameters.AddWithValue("credit", int.TryParse(cboCreditDays.Text, out int d2) ? d2 : 0);
                    cmd.Parameters.AddWithValue("due", dtDueDate.Value.Date);
                    cmd.Parameters.AddWithValue("remarks", (object?)txtRemarks.Text.Trim() ?? "");
                    cmd.Parameters.AddWithValue("sub", subTotal);
                    cmd.Parameters.AddWithValue("disc", discount);
                    cmd.Parameters.AddWithValue("grand", grand);
                    purchaseId = (int)cmd.ExecuteScalar()!;
                }

                foreach (var line in lines)
                {
                    using var lineCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO purchase_bill_item (purchase_id, item_id, qty, rate, disc_percent, amount)
                        VALUES (@p, @item, @qty, @rate, @disc, @amt)", conn, tx);
                    lineCmd.Parameters.AddWithValue("p", purchaseId);
                    lineCmd.Parameters.AddWithValue("item", line.ItemId);
                    lineCmd.Parameters.AddWithValue("qty", line.Qty);
                    lineCmd.Parameters.AddWithValue("rate", line.Rate);
                    lineCmd.Parameters.AddWithValue("disc", line.DiscPercent);
                    lineCmd.Parameters.AddWithValue("amt", line.Amount);
                    lineCmd.ExecuteNonQuery();

                    int wh = (int)cboWarehouse.SelectedValue!;
                    using var stockCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                        VALUES (@item, @wh, 'IN', @qty, 'PURCHASE', @p)", conn, tx);
                    stockCmd.Parameters.AddWithValue("item", line.ItemId);
                    stockCmd.Parameters.AddWithValue("wh", wh);
                    stockCmd.Parameters.AddWithValue("qty", line.Qty);
                    stockCmd.Parameters.AddWithValue("p", purchaseId);
                    stockCmd.ExecuteNonQuery();

                    DbHelper.AdjustBalance(conn, tx, line.ItemId, wh, line.Qty);
                }

                // Ledger: Credit supplier (payable up) / Debit Purchases (expense up)
                using var ledgerCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                    SELECT @no, 'Purchase Bill', account_id, 'By Purchase Bill', 0, @grand, @p
                    FROM supplier_master WHERE supplier_id=@sup", conn, tx);
                ledgerCmd.Parameters.AddWithValue("no", billNo);
                ledgerCmd.Parameters.AddWithValue("grand", grand);
                ledgerCmd.Parameters.AddWithValue("p", purchaseId);
                ledgerCmd.Parameters.AddWithValue("sup", (int)cboSupplier.SelectedValue!);
                ledgerCmd.ExecuteNonQuery();

                int purchaseAccountId = DbHelper.GetAccountIdByCode(conn, tx, "5000");
                using var purchExpCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                    VALUES (@no, 'Purchase Bill', @acc, 'To Purchase Bill', @grand, 0, @p)", conn, tx);
                purchExpCmd.Parameters.AddWithValue("no", billNo);
                purchExpCmd.Parameters.AddWithValue("acc", purchaseAccountId);
                purchExpCmd.Parameters.AddWithValue("grand", grand);
                purchExpCmd.Parameters.AddWithValue("p", purchaseId);
                purchExpCmd.ExecuteNonQuery();

                txtInvoiceNo.Text = billNo;
                currentPurchaseId = purchaseId;
                isEditMode = true; // further Saves in this session update the same bill
            });

            DbHelper.LogAction($"Purchase Bill: Saved {txtInvoiceNo.Text}");
            MessageBox.Show("Purchase bill saved successfully.");
            Text = $"Purchase — Editing {txtInvoiceNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (currentPurchaseId == null) { MessageBox.Show("Nothing saved yet to delete."); return; }
        if (MessageBox.Show($"Delete purchase bill {txtInvoiceNo.Text}? This reverses its stock and ledger effect.",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            int purchaseId = currentPurchaseId.Value;
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                DbHelper.ReversePurchaseBillPostings(conn, tx, purchaseId);
                using var delCmd = new Npgsql.NpgsqlCommand("DELETE FROM purchase_bill WHERE purchase_id=@id", conn, tx);
                delCmd.Parameters.AddWithValue("id", purchaseId);
                delCmd.ExecuteNonQuery();
            });
            DbHelper.LogAction($"Purchase Bill: Deleted #{purchaseId}");
            MessageBox.Show("Purchase bill deleted.");
            ResetForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message);
        }
    }

    private void BtnPrint_Click(object? sender, EventArgs e)
    {
        if (currentPurchaseId == null) { MessageBox.Show("Save the purchase bill first."); return; }
        using var printForm = new InvoicePrintForm(currentPurchaseId.Value, isPurchase: true);
        printForm.ShowDialog(this);
    }

    private void ResetForm()
    {
        lines.Clear();
        currentPurchaseId = null;
        isEditMode = false;
        NewDocNo();
        cboSupplier.SelectedIndex = -1;
        txtRefNo.Clear();
        txtRemarks.Clear();
        RecalculateTotals();
        Text = "Purchase";
    }
}
