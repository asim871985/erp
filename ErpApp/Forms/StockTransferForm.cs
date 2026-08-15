using System.ComponentModel;
using System.Data;
using ErpApp.Data;
using ErpApp.Models;

namespace ErpApp.Forms;

/// <summary>Matches mockup form 5 "Stock Transfer" — move stock between warehouses.</summary>
public class StockTransferForm : AppFormBase
{
    private readonly TextBox txtTransferNo = new() { ReadOnly = true };
    private readonly DateTimePicker dtTransferDate = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboFromWarehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboToWarehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtRemarks = new() { Multiline = true };

    private readonly DataGridView grid = new();
    private readonly BindingList<InvoiceLine> lines = new();
    private readonly Label lblTotalItems = new();

    private DataTable itemLookup = new();
    private int? currentTransferId;

    public StockTransferForm()
    {
        Text = "Stock Transfer";
        Width = 900;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadLookups();
        NewDocNo();
    }

    private void BuildLayout()
    {
        var header = new GroupBox { Text = "Transfer Information", Dock = DockStyle.Top, Height = 150 };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(10) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        void L(int r, int c, string text) => t.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, c, r);
        void C(int r, int c, Control ctrl) { ctrl.Dock = DockStyle.Fill; t.Controls.Add(ctrl, c, r); }

        L(0, 0, "Transfer No."); C(0, 1, txtTransferNo);
        L(0, 2, "Transfer Date"); C(0, 3, dtTransferDate);
        L(1, 0, "From Warehouse"); C(1, 1, cboFromWarehouse);
        L(1, 2, "To Warehouse"); C(1, 3, cboToWarehouse);
        txtRemarks.Height = 50;
        L(2, 0, "Remarks"); C(2, 1, txtRemarks);
        t.SetColumnSpan(txtRemarks, 3);

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
        bottomPanel.Controls.Add(lblTotalItems);

        var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(8) };
        var btnNew = new Button { Text = "+ New" };
        var btnSave = new Button { Text = "Save" };
        var btnCancel = new Button { Text = "Cancel" };
        var btnDelete = new Button { Text = "Delete" };
        var btnPrint = new Button { Text = "Print" };
        btnNew.Click += (s, e) => ResetForm();
        btnSave.Click += BtnSave_Click;
        btnCancel.Click += (s, e) => ResetForm();
        btnDelete.Click += BtnDelete_Click;
        btnPrint.Click += (s, e) =>
        {
            if (currentTransferId == null) { MessageBox.Show("Save the transfer first."); return; }
            using var printForm = new StockTransferPrintForm(currentTransferId.Value);
            printForm.ShowDialog(this);
        };
        actionPanel.Controls.Add(btnNew);
        actionPanel.Controls.Add(btnSave);
        actionPanel.Controls.Add(btnCancel);
        actionPanel.Controls.Add(btnDelete);
        actionPanel.Controls.Add(btnPrint);

        Controls.Add(grid);
        Controls.Add(bottomPanel);
        Controls.Add(actionPanel);
        Controls.Add(lineBtnPanel);
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
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "Item Name", DataPropertyName = "ItemName", ReadOnly = true, FillWeight = 160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Model", HeaderText = "Model", DataPropertyName = "Model", ReadOnly = true, FillWeight = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Uom", HeaderText = "UOM", DataPropertyName = "Uom", ReadOnly = true, FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "Qty", DataPropertyName = "Qty", FillWeight = 80 });
        grid.DataSource = lines;
        grid.CellEndEdit += (s, e) => RecalculateTotals();
        grid.RowPostPaint += (s, e) => grid.Rows[e.RowIndex].Cells["SNo"].Value = (e.RowIndex + 1).ToString();
    }

    private void LoadLookups()
    {
        try
        {
            var warehouses = DbHelper.ExecuteQuery("SELECT warehouse_id, warehouse_name FROM warehouse_master WHERE active ORDER BY warehouse_name");
            cboFromWarehouse.DisplayMember = "warehouse_name";
            cboFromWarehouse.ValueMember = "warehouse_id";
            cboFromWarehouse.DataSource = warehouses;

            var warehouses2 = DbHelper.ExecuteQuery("SELECT warehouse_id, warehouse_name FROM warehouse_master WHERE active ORDER BY warehouse_name");
            cboToWarehouse.DisplayMember = "warehouse_name";
            cboToWarehouse.ValueMember = "warehouse_id";
            cboToWarehouse.DataSource = warehouses2;

            itemLookup = DbHelper.ExecuteQuery(@"
                SELECT i.item_id, i.item_name, i.model, u.uom_name, i.rate
                FROM item_master i
                LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                WHERE i.active ORDER BY i.item_name");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load warehouses/items: " + ex.Message);
        }
    }

    private void NewDocNo()
    {
        try
        {
            var num = DbHelper.ExecuteScalar("SELECT prefix || LPAD(next_number::text, padding, '0') || COALESCE(suffix,'') FROM document_numbering WHERE doc_type='STOCK_TRANSFER'");
            txtTransferNo.Text = num?.ToString() ?? "ST-00001";
        }
        catch { txtTransferNo.Text = "(auto on save)"; }
        dtTransferDate.Value = DateTime.Today;
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
            Qty = 1
        });
    }

    private void RemoveSelectedLine()
    {
        if (grid.CurrentRow?.DataBoundItem is InvoiceLine line)
            lines.Remove(line);
    }

    private void RecalculateTotals() => lblTotalItems.Text = $"Total Items: {lines.Count}";

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (cboFromWarehouse.SelectedValue == null || cboToWarehouse.SelectedValue == null)
        {
            MessageBox.Show("Select both From and To warehouses.");
            return;
        }
        if (Equals(cboFromWarehouse.SelectedValue, cboToWarehouse.SelectedValue))
        {
            MessageBox.Show("From Warehouse and To Warehouse must be different.");
            return;
        }
        if (lines.Count == 0) { MessageBox.Show("Add at least one item."); return; }

        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                string transferNo = DbHelper.GetNextDocumentNumber(conn, tx, "STOCK_TRANSFER");

                using var cmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO stock_transfer (transfer_no, transfer_date, from_warehouse_id, to_warehouse_id, remarks)
                    VALUES (@no, @dt, @from, @to, @remarks) RETURNING transfer_id", conn, tx);
                cmd.Parameters.AddWithValue("no", transferNo);
                cmd.Parameters.AddWithValue("dt", dtTransferDate.Value.Date);
                cmd.Parameters.AddWithValue("from", (int)cboFromWarehouse.SelectedValue!);
                cmd.Parameters.AddWithValue("to", (int)cboToWarehouse.SelectedValue!);
                cmd.Parameters.AddWithValue("remarks", (object?)txtRemarks.Text.Trim() ?? "");
                int transferId = (int)cmd.ExecuteScalar()!;

                foreach (var line in lines)
                {
                    using var lineCmd = new Npgsql.NpgsqlCommand(
                        "INSERT INTO stock_transfer_item (transfer_id, item_id, qty) VALUES (@t, @item, @qty)", conn, tx);
                    lineCmd.Parameters.AddWithValue("t", transferId);
                    lineCmd.Parameters.AddWithValue("item", line.ItemId);
                    lineCmd.Parameters.AddWithValue("qty", line.Qty);
                    lineCmd.ExecuteNonQuery();

                    // OUT leg: stock leaves the From warehouse
                    using var outCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                        VALUES (@item, @from, 'TRANSFER_OUT', @qty, 'TRANSFER', @t)", conn, tx);
                    outCmd.Parameters.AddWithValue("item", line.ItemId);
                    outCmd.Parameters.AddWithValue("from", (int)cboFromWarehouse.SelectedValue!);
                    outCmd.Parameters.AddWithValue("qty", line.Qty);
                    outCmd.Parameters.AddWithValue("t", transferId);
                    outCmd.ExecuteNonQuery();

                    // IN leg: stock arrives at the To warehouse
                    using var inCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                        VALUES (@item, @to, 'TRANSFER_IN', @qty, 'TRANSFER', @t)", conn, tx);
                    inCmd.Parameters.AddWithValue("item", line.ItemId);
                    inCmd.Parameters.AddWithValue("to", (int)cboToWarehouse.SelectedValue!);
                    inCmd.Parameters.AddWithValue("qty", line.Qty);
                    inCmd.Parameters.AddWithValue("t", transferId);
                    inCmd.ExecuteNonQuery();

                    // Balances move between warehouses (overall on-hand is unchanged)
                    DbHelper.AdjustBalance(conn, tx, line.ItemId, (int)cboFromWarehouse.SelectedValue!, -line.Qty);
                    DbHelper.AdjustBalance(conn, tx, line.ItemId, (int)cboToWarehouse.SelectedValue!, line.Qty);
                }

                txtTransferNo.Text = transferNo;
                currentTransferId = transferId;
            });

            MessageBox.Show("Stock transfer saved successfully.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (currentTransferId == null) { MessageBox.Show("Nothing saved yet to delete."); return; }
        if (MessageBox.Show("Delete this stock transfer?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                DbHelper.ReverseStockTransferPostings(conn, tx, currentTransferId.Value);
                using var cmd = new Npgsql.NpgsqlCommand("DELETE FROM stock_transfer WHERE transfer_id=@id", conn, tx);
                cmd.Parameters.AddWithValue("id", currentTransferId.Value);
                cmd.ExecuteNonQuery();
            });
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
        currentTransferId = null;
        NewDocNo();
        cboFromWarehouse.SelectedIndex = -1;
        cboToWarehouse.SelectedIndex = -1;
        txtRemarks.Clear();
        RecalculateTotals();
    }
}
