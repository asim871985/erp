using System.ComponentModel;
using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public class JournalLine
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

/// <summary>Matches mockup form 9 "Journal Voucher" — multi-line debit/credit entry that must balance.</summary>
public class JournalVoucherForm : AppFormBase
{
    private readonly TextBox txtVoucherNo = new() { ReadOnly = true };
    private readonly DateTimePicker dtVoucherDate = new() { Format = DateTimePickerFormat.Short };
    private readonly TextBox txtNarration = new() { Multiline = true };

    private readonly DataGridView grid = new();
    private readonly BindingList<JournalLine> lines = new();
    private readonly Label lblTotalDebit = new() { Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    private readonly Label lblTotalCredit = new() { Font = new Font("Segoe UI", 9, FontStyle.Bold) };

    private DataTable accountLookup = new();
    private int? currentJournalId;
    private DataGridViewComboBoxColumn accountCol = null!;

    public JournalVoucherForm()
    {
        Text = "Journal Voucher";
        Width = 850;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadAccounts();
        NewDocNo();
    }

    private void BuildLayout()
    {
        var header = new GroupBox { Text = "Voucher Information", Dock = DockStyle.Top, Height = 130 };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10) };
        for (int i = 0; i < t.RowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / t.RowCount));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

        t.Controls.Add(new Label { Text = "Voucher No.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        txtVoucherNo.Dock = DockStyle.Fill;
        t.Controls.Add(txtVoucherNo, 1, 0);
        t.Controls.Add(new Label { Text = "Voucher Date", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        dtVoucherDate.Dock = DockStyle.Fill;
        t.Controls.Add(dtVoucherDate, 1, 1);
        header.Controls.Add(t);

        var narrationPanel = new Panel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(10, 0, 10, 5) };
        var narrationLabel = new Label { Text = "Narration", Dock = DockStyle.Top, Height = 18 };
        txtNarration.Dock = DockStyle.Fill;
        narrationPanel.Controls.Add(txtNarration);
        narrationPanel.Controls.Add(narrationLabel);

        var lineBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(5) };
        var btnAddLine = new Button { Text = "+ Add Line" };
        var btnRemoveLine = new Button { Text = "Remove Line" };
        btnAddLine.Click += (s, e) => lines.Add(new JournalLine());
        btnRemoveLine.Click += (s, e) => RemoveSelectedLine();
        lineBtnPanel.Controls.Add(btnAddLine);
        lineBtnPanel.Controls.Add(btnRemoveLine);

        BuildGrid();

        var bottomPanel = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 40, ColumnCount = 2 };
        lblTotalDebit.Dock = DockStyle.Fill;
        lblTotalDebit.TextAlign = ContentAlignment.MiddleRight;
        lblTotalCredit.Dock = DockStyle.Fill;
        lblTotalCredit.TextAlign = ContentAlignment.MiddleRight;
        bottomPanel.Controls.Add(lblTotalDebit, 0, 0);
        bottomPanel.Controls.Add(lblTotalCredit, 1, 0);

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
        btnPrint.Click += (s, e) => MessageBox.Show("Wire this up to the pdf skill to print the voucher.");
        actionPanel.Controls.Add(btnNew);
        actionPanel.Controls.Add(btnSave);
        actionPanel.Controls.Add(btnCancel);
        actionPanel.Controls.Add(btnDelete);
        actionPanel.Controls.Add(btnPrint);

        Controls.Add(grid);
        Controls.Add(bottomPanel);
        Controls.Add(actionPanel);
        Controls.Add(lineBtnPanel);
        Controls.Add(narrationPanel);
        Controls.Add(header);

        lines.ListChanged += (s, e) => RecalculateTotals();
    }

    private void BuildGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var accountCol = new DataGridViewComboBoxColumn
        {
            Name = "AccountName",
            HeaderText = "Account Head",
            DataPropertyName = "AccountName",
            FillWeight = 160
        };
        this.accountCol = accountCol;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SNo", HeaderText = "S.No", ReadOnly = true, FillWeight = 40 });
        grid.Columns.Add(accountCol);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Description", DataPropertyName = "Description", FillWeight = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Debit", HeaderText = "Debit", DataPropertyName = "Debit", FillWeight = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Credit", HeaderText = "Credit", DataPropertyName = "Credit", FillWeight = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" } });

        grid.DataSource = lines;
        grid.CellEndEdit += Grid_CellEndEdit;
        grid.RowPostPaint += (s, e) => grid.Rows[e.RowIndex].Cells["SNo"].Value = (e.RowIndex + 1).ToString();
    }

    private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (grid.Rows[e.RowIndex].DataBoundItem is not JournalLine line) return;

        if (grid.Columns[e.ColumnIndex].Name == "AccountName")
        {
            var match = accountLookup.AsEnumerable().FirstOrDefault(r => r["account_name"].ToString() == line.AccountName);
            if (match != null) line.AccountId = Convert.ToInt32(match["account_id"]);
        }
        RecalculateTotals();
    }

    private void LoadAccounts()
    {
        try
        {
            accountLookup = DbHelper.ExecuteQuery("SELECT account_id, account_name FROM chart_of_accounts WHERE active ORDER BY account_name");
            accountCol.Items.Clear();
            foreach (DataRow row in accountLookup.Rows)
                accountCol.Items.Add(row["account_name"].ToString()!);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load accounts: " + ex.Message);
        }
    }

    private void NewDocNo()
    {
        try
        {
            var num = DbHelper.ExecuteScalar("SELECT prefix || LPAD(next_number::text, padding, '0') || COALESCE(suffix,'') FROM document_numbering WHERE doc_type='JOURNAL'");
            txtVoucherNo.Text = num?.ToString() ?? "JV-00001";
        }
        catch { txtVoucherNo.Text = "(auto on save)"; }
        dtVoucherDate.Value = DateTime.Today;
    }

    private void RemoveSelectedLine()
    {
        if (grid.CurrentRow?.DataBoundItem is JournalLine line)
            lines.Remove(line);
    }

    private void RecalculateTotals()
    {
        decimal debit = lines.Sum(l => l.Debit);
        decimal credit = lines.Sum(l => l.Credit);
        lblTotalDebit.Text = "Total Debit: " + debit.ToString("N2");
        lblTotalCredit.Text = "Total Credit: " + credit.ToString("N2");
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (lines.Count < 2) { MessageBox.Show("Add at least two lines (one debit, one credit)."); return; }
        if (lines.Any(l => l.AccountId == 0)) { MessageBox.Show("Every line needs an Account Head."); return; }

        decimal totalDebit = lines.Sum(l => l.Debit);
        decimal totalCredit = lines.Sum(l => l.Credit);
        if (totalDebit != totalCredit)
        {
            MessageBox.Show($"Debit ({totalDebit:N2}) and Credit ({totalCredit:N2}) must be equal before saving.");
            return;
        }
        if (totalDebit == 0) { MessageBox.Show("Enter debit/credit amounts."); return; }

        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                string voucherNo = DbHelper.GetNextDocumentNumber(conn, tx, "JOURNAL");

                using var cmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO journal_voucher (voucher_no, voucher_date, narration)
                    VALUES (@no, @dt, @narr) RETURNING journal_id", conn, tx);
                cmd.Parameters.AddWithValue("no", voucherNo);
                cmd.Parameters.AddWithValue("dt", dtVoucherDate.Value.Date);
                cmd.Parameters.AddWithValue("narr", (object?)txtNarration.Text.Trim() ?? "");
                int journalId = (int)cmd.ExecuteScalar()!;

                foreach (var line in lines)
                {
                    using var lineCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO journal_voucher_item (journal_id, account_id, description, debit, credit)
                        VALUES (@j, @acc, @desc, @debit, @credit)", conn, tx);
                    lineCmd.Parameters.AddWithValue("j", journalId);
                    lineCmd.Parameters.AddWithValue("acc", line.AccountId);
                    lineCmd.Parameters.AddWithValue("desc", (object?)line.Description ?? "");
                    lineCmd.Parameters.AddWithValue("debit", line.Debit);
                    lineCmd.Parameters.AddWithValue("credit", line.Credit);
                    lineCmd.ExecuteNonQuery();

                    using var ledgerCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                        VALUES (@no, 'Journal', @acc, @desc, @debit, @credit, @j)", conn, tx);
                    ledgerCmd.Parameters.AddWithValue("no", voucherNo);
                    ledgerCmd.Parameters.AddWithValue("acc", line.AccountId);
                    ledgerCmd.Parameters.AddWithValue("desc", (object?)line.Description ?? "Journal Entry");
                    ledgerCmd.Parameters.AddWithValue("debit", line.Debit);
                    ledgerCmd.Parameters.AddWithValue("credit", line.Credit);
                    ledgerCmd.Parameters.AddWithValue("j", journalId);
                    ledgerCmd.ExecuteNonQuery();
                }

                txtVoucherNo.Text = voucherNo;
                currentJournalId = journalId;
            });

            MessageBox.Show("Journal voucher saved successfully.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (currentJournalId == null) { MessageBox.Show("Nothing saved yet to delete."); return; }
        if (MessageBox.Show("Delete this journal voucher?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            DbHelper.ExecuteNonQuery("DELETE FROM journal_voucher WHERE journal_id=@id", new() { ["id"] = currentJournalId });
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
        currentJournalId = null;
        NewDocNo();
        txtNarration.Clear();
        RecalculateTotals();
    }
}
