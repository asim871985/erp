using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches mockup form 10 "Contra Entry" — fund transfer between cash/bank accounts.</summary>
public class ContraEntryForm : AppFormBase
{
    private readonly TextBox txtVoucherNo = new() { ReadOnly = true };
    private readonly DateTimePicker dtVoucherDate = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboFromAccount = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboToAccount = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown numAmount = new() { DecimalPlaces = 2, Maximum = 100_000_000 };
    private readonly TextBox txtNarration = new() { Multiline = true };

    private int? currentContraId;

    public ContraEntryForm()
    {
        Text = "Contra Entry";
        Width = 550;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadAccounts();
        NewDocNo();
    }

    private void BuildLayout()
    {
        var title = new Label { Text = "Contra Information", Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 11, FontStyle.Bold), Padding = new Padding(10, 5, 0, 0) };

        var t = new TableLayoutPanel { Dock = DockStyle.Top, Height = 260, ColumnCount = 2, Padding = new Padding(15) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c);
        }

        Row("Voucher No.", txtVoucherNo);
        Row("Voucher Date", dtVoucherDate);
        Row("From Account", cboFromAccount);
        Row("To Account", cboToAccount);
        Row("Amount", numAmount);

        int fieldRowCount = t.Controls.Count / t.ColumnCount;
        for (int i = 0; i < fieldRowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / fieldRowCount));

        var narrationPanel = new Panel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(15, 0, 15, 5) };
        var narrationLabel = new Label { Text = "Narration", Dock = DockStyle.Top, Height = 18, ForeColor = Color.Gray };
        txtNarration.Dock = DockStyle.Fill;
        narrationPanel.Controls.Add(txtNarration);
        narrationPanel.Controls.Add(narrationLabel);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(15) };
        var btnNew = new Button { Text = "New", Width = 80 };
        var btnSave = new Button { Text = "Save", Width = 80 };
        var btnCancel = new Button { Text = "Cancel", Width = 80 };
        var btnDelete = new Button { Text = "Delete", Width = 80 };
        var btnPrint = new Button { Text = "Print", Width = 80 };
        btnNew.Click += (s, e) => ResetForm();
        btnSave.Click += BtnSave_Click;
        btnCancel.Click += (s, e) => ResetForm();
        btnDelete.Click += BtnDelete_Click;
        btnPrint.Click += (s, e) => MessageBox.Show("Wire this up to the pdf skill to print the voucher.");
        btnPanel.Controls.Add(btnNew);
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnDelete);
        btnPanel.Controls.Add(btnPrint);

        Controls.Add(narrationPanel);
        Controls.Add(t);
        Controls.Add(title);
        Controls.Add(btnPanel);
    }

    private void LoadAccounts()
    {
        try
        {
            var accounts = DbHelper.ExecuteQuery("SELECT account_id, account_name FROM chart_of_accounts WHERE active ORDER BY account_name");
            cboFromAccount.DisplayMember = "account_name";
            cboFromAccount.ValueMember = "account_id";
            cboFromAccount.DataSource = accounts;

            var accounts2 = DbHelper.ExecuteQuery("SELECT account_id, account_name FROM chart_of_accounts WHERE active ORDER BY account_name");
            cboToAccount.DisplayMember = "account_name";
            cboToAccount.ValueMember = "account_id";
            cboToAccount.DataSource = accounts2;
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
            var num = DbHelper.ExecuteScalar("SELECT prefix || LPAD(next_number::text, padding, '0') || COALESCE(suffix,'') FROM document_numbering WHERE doc_type='CONTRA'");
            txtVoucherNo.Text = num?.ToString() ?? "CN-00001";
        }
        catch { txtVoucherNo.Text = "(auto on save)"; }
        dtVoucherDate.Value = DateTime.Today;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (cboFromAccount.SelectedValue == null || cboToAccount.SelectedValue == null)
        {
            MessageBox.Show("Select both From Account and To Account.");
            return;
        }
        if (Equals(cboFromAccount.SelectedValue, cboToAccount.SelectedValue))
        {
            MessageBox.Show("From Account and To Account must be different.");
            return;
        }
        if (numAmount.Value <= 0) { MessageBox.Show("Enter an amount."); return; }

        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                string voucherNo = DbHelper.GetNextDocumentNumber(conn, tx, "CONTRA");
                int fromId = (int)cboFromAccount.SelectedValue!;
                int toId = (int)cboToAccount.SelectedValue!;

                using var cmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO contra_entry (voucher_no, voucher_date, from_account_id, to_account_id, amount, narration)
                    VALUES (@no, @dt, @from, @to, @amt, @narr) RETURNING contra_id", conn, tx);
                cmd.Parameters.AddWithValue("no", voucherNo);
                cmd.Parameters.AddWithValue("dt", dtVoucherDate.Value.Date);
                cmd.Parameters.AddWithValue("from", fromId);
                cmd.Parameters.AddWithValue("to", toId);
                cmd.Parameters.AddWithValue("amt", numAmount.Value);
                cmd.Parameters.AddWithValue("narr", (object?)txtNarration.Text.Trim() ?? "");
                int contraId = (int)cmd.ExecuteScalar()!;

                // Ledger: Credit From (money leaves), Debit To (money arrives)
                using var fromCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                    VALUES (@no, 'Contra', @acc, 'By Contra', 0, @amt, @id)", conn, tx);
                fromCmd.Parameters.AddWithValue("no", voucherNo);
                fromCmd.Parameters.AddWithValue("acc", fromId);
                fromCmd.Parameters.AddWithValue("amt", numAmount.Value);
                fromCmd.Parameters.AddWithValue("id", contraId);
                fromCmd.ExecuteNonQuery();

                using var toCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit, reference_id)
                    VALUES (@no, 'Contra', @acc, 'To Contra', @amt, 0, @id)", conn, tx);
                toCmd.Parameters.AddWithValue("no", voucherNo);
                toCmd.Parameters.AddWithValue("acc", toId);
                toCmd.Parameters.AddWithValue("amt", numAmount.Value);
                toCmd.Parameters.AddWithValue("id", contraId);
                toCmd.ExecuteNonQuery();

                txtVoucherNo.Text = voucherNo;
                currentContraId = contraId;
            });

            MessageBox.Show("Contra entry saved successfully.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (currentContraId == null) { MessageBox.Show("Nothing saved yet to delete."); return; }
        if (MessageBox.Show("Delete this contra entry?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            DbHelper.ExecuteNonQuery("DELETE FROM contra_entry WHERE contra_id=@id", new() { ["id"] = currentContraId });
            ResetForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message);
        }
    }

    private void ResetForm()
    {
        currentContraId = null;
        NewDocNo();
        cboFromAccount.SelectedIndex = -1;
        cboToAccount.SelectedIndex = -1;
        numAmount.Value = 0;
        txtNarration.Clear();
    }
}
