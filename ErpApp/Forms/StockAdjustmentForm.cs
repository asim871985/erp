using System.ComponentModel;
using System.Data;
using ErpApp.Data;
using ErpApp.Models;

namespace ErpApp.Forms;

/// <summary>Matches mockup form 6 "Stock Adjustment" — increase/decrease stock (damage, count corrections, etc).</summary>
public class StockAdjustmentForm : AppFormBase
{
    private readonly TextBox txtAdjustmentNo = new() { ReadOnly = true };
    private readonly DateTimePicker dtAdjustmentDate = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboAdjustmentType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboWarehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtRemarks = new() { Multiline = true };

    private readonly DataGridView grid = new();
    private readonly BindingList<InvoiceLine> lines = new();
    private readonly Label lblTotalItems = new();
    private readonly Label lblTotalAmount = new() { Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };

    private DataTable itemLookup = new();
    private int? currentAdjustmentId;

    public StockAdjustmentForm()
    {
        Text = "Stock Adjustment";
        Width = 900;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadLookups();
        NewDocNo();
    }

    private void BuildLayout()
    {
        var header = new GroupBox { Text = "Adjustment Information", Dock = DockStyle.Top, Height = 150 };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(10) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        void L(int r, int c, string text) => t.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, c, r);
        void C(int r, int c, Control ctrl) { ctrl.Dock = DockStyle.Fill; t.Controls.Add(ctrl, c, r); }

        L(0, 0, "Adjustment No."); C(0, 1, txtAdjustmentNo);
        L(0, 2, "Adjustment Date"); C(0, 3, dtAdjustmentDate);
        cboAdjustmentType.Items.AddRange(new object[] { "Increase", "Decrease" });
        cboAdjustmentType.SelectedIndex = 0;
        L(1, 0, "Adjustment Type"); C(1, 1, cboAdjustmentType);
        L(1, 2, "Warehouse"); C(1, 3, cboWarehouse);
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
        btnNew.Click += (s, e) => ResetForm();
        btnSave.Click += BtnSave_Click;
        btnCancel.Click += (s, e) => ResetForm();
        btnDelete.Click += BtnDelete_Click;
        btnPrint.Click += (s, e) => MessageBox.Show("Wire this up to the pdf skill to print the adjustment note.");
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
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "Item Name", DataPropertyName = "ItemName", ReadOnly = true, FillWeight = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Model", HeaderText = "Model", DataPropertyName = "Model", ReadOnly = true, FillWeight = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Uom", HeaderText = "UOM", DataPropertyName = "Uom", ReadOnly = true, FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "Qty", DataPropertyName = "Qty", FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rate", HeaderText = "Rate", DataPropertyName = "Rate", FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Amount", DataPropertyName = "Amount", ReadOnly = true, FillWeight = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" } });
        grid.DataSource = lines;
        grid.CellEndEdit += (s, e) => RecalculateTotals();
        grid.RowPostPaint += (s, e) => grid.Rows[e.RowIndex].Cells["SNo"].Value = (e.RowIndex + 1).ToString();
    }

    private void LoadLookups()
    {
        try
        {
            var warehouses = DbHelper.ExecuteQuery("SELECT warehouse_id, warehouse_name FROM warehouse_master WHERE active ORDER BY warehouse_name");
            cboWarehouse.DisplayMember = "warehouse_name";
            cboWarehouse.ValueMember = "warehouse_id";
            cboWarehouse.DataSource = warehouses;

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
            var num = DbHelper.ExecuteScalar("SELECT prefix || LPAD(next_number::text, padding, '0') || COALESCE(suffix,'') FROM document_numbering WHERE doc_type='STOCK_ADJUSTMENT'");
            txtAdjustmentNo.Text = num?.ToString() ?? "ADJ-00001";
        }
        catch { txtAdjustmentNo.Text = "(auto on save)"; }
        dtAdjustmentDate.Value = DateTime.Today;
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
            Rate = r["rate"] is DBNull ? 0 : Convert.ToDecimal(r["rate"])
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
        decimal total = lines.Sum(l => l.Qty * l.Rate);
        lblTotalAmount.Text = "Total Amount: " + total.ToString("N2");
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (cboWarehouse.SelectedValue == null) { MessageBox.Show("Select a warehouse."); return; }
        if (lines.Count == 0) { MessageBox.Show("Add at least one item."); return; }

        bool isIncrease = cboAdjustmentType.SelectedItem?.ToString() == "Increase";

        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                string adjNo = DbHelper.GetNextDocumentNumber(conn, tx, "STOCK_ADJUSTMENT");

                using var cmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO stock_adjustment (adjustment_no, adjustment_date, adjustment_type, warehouse_id, remarks)
                    VALUES (@no, @dt, @type, @wh, @remarks) RETURNING adjustment_id", conn, tx);
                cmd.Parameters.AddWithValue("no", adjNo);
                cmd.Parameters.AddWithValue("dt", dtAdjustmentDate.Value.Date);
                cmd.Parameters.AddWithValue("type", cboAdjustmentType.SelectedItem!.ToString()!);
                cmd.Parameters.AddWithValue("wh", (int)cboWarehouse.SelectedValue!);
                cmd.Parameters.AddWithValue("remarks", (object?)txtRemarks.Text.Trim() ?? "");
                int adjId = (int)cmd.ExecuteScalar()!;

                foreach (var line in lines)
                {
                    using var lineCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO stock_adjustment_item (adjustment_id, item_id, qty, rate, amount)
                        VALUES (@a, @item, @qty, @rate, @amt)", conn, tx);
                    lineCmd.Parameters.AddWithValue("a", adjId);
                    lineCmd.Parameters.AddWithValue("item", line.ItemId);
                    lineCmd.Parameters.AddWithValue("qty", line.Qty);
                    lineCmd.Parameters.AddWithValue("rate", line.Rate);
                    lineCmd.Parameters.AddWithValue("amt", line.Qty * line.Rate);
                    lineCmd.ExecuteNonQuery();

                    using var stockCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO stock_movement (item_id, warehouse_id, movement_type, qty, reference_type, reference_id)
                        VALUES (@item, @wh, 'ADJUSTMENT', @qty, 'ADJUSTMENT', @a)", conn, tx);
                    stockCmd.Parameters.AddWithValue("item", line.ItemId);
                    stockCmd.Parameters.AddWithValue("wh", (int)cboWarehouse.SelectedValue!);
                    stockCmd.Parameters.AddWithValue("qty", line.Qty);
                    stockCmd.Parameters.AddWithValue("a", adjId);
                    stockCmd.ExecuteNonQuery();

                    decimal signedQty = isIncrease ? line.Qty : -line.Qty;
                    DbHelper.AdjustBalance(conn, tx, line.ItemId, (int)cboWarehouse.SelectedValue!, signedQty);
                }

                txtAdjustmentNo.Text = adjNo;
                currentAdjustmentId = adjId;
            });

            MessageBox.Show("Stock adjustment saved successfully.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (currentAdjustmentId == null) { MessageBox.Show("Nothing saved yet to delete."); return; }
        if (MessageBox.Show("Delete this stock adjustment?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                DbHelper.ReverseStockAdjustmentPostings(conn, tx, currentAdjustmentId.Value);
                using var cmd = new Npgsql.NpgsqlCommand("DELETE FROM stock_adjustment WHERE adjustment_id=@id", conn, tx);
                cmd.Parameters.AddWithValue("id", currentAdjustmentId.Value);
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
        currentAdjustmentId = null;
        NewDocNo();
        cboWarehouse.SelectedIndex = -1;
        cboAdjustmentType.SelectedIndex = 0;
        txtRemarks.Clear();
        RecalculateTotals();
    }
}
