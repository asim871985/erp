using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches the "Receipt" window in screenshot 2.</summary>
public class ReceiptForm : AppFormBase
{
    private readonly TextBox txtReceiptNo = new() { ReadOnly = true };
    private readonly DateTimePicker dtReceiptDate = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboAccount = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboPaymentMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboBankAccount = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboReceivedBy = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtReference = new();
    private readonly NumericUpDown numAmount = new() { DecimalPlaces = 2, Maximum = 100_000_000, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
    private readonly Label lblAmountWords = new();

    private int? currentReceiptId;
    private bool isEditMode;

    public ReceiptForm() : this(null) { }

    public ReceiptForm(int? editReceiptId)
    {
        Text = "Receipt";
        Width = 520;
        Height = 480;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadLookups();

        if (editReceiptId != null)
            LoadForEdit(editReceiptId.Value);
        else
            NewReceiptNo();
    }

    private void BuildLayout()
    {
        var title = new Label { Text = "RECEIPT", Dock = DockStyle.Top, Height = 35, Font = new Font("Segoe UI", 13, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

        var t = new TableLayoutPanel { Dock = DockStyle.Top, Height = 300, ColumnCount = 2, Padding = new Padding(15) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c);
        }

        Row("Receipt No.", txtReceiptNo);
        Row("Receipt Date", dtReceiptDate);
        Row("Account", cboAccount);
        Row("Payment Mode", cboPaymentMode);
        Row("Bank Account", cboBankAccount);
        Row("Received By", cboReceivedBy);
        Row("Reference", txtReference);

        int fieldRowCount = t.Controls.Count / t.ColumnCount;
        for (int i = 0; i < fieldRowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / fieldRowCount));

        var amountPanel = new GroupBox { Text = "", Dock = DockStyle.Top, Height = 100 };
        var lblCaption = new Label { Text = "Amount Received", Location = new Point(15, 15), AutoSize = true };
        numAmount.Location = new Point(15, 40);
        numAmount.Width = 200;
        amountPanel.Controls.Add(lblCaption);
        amountPanel.Controls.Add(numAmount);

        var wordsPanel = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(15, 5, 15, 5) };
        var wordsCaption = new Label { Text = "Amount in Words", Dock = DockStyle.Top, Height = 18, ForeColor = Color.Gray };
        lblAmountWords.Dock = DockStyle.Top;
        lblAmountWords.Height = 22;
        wordsPanel.Controls.Add(lblAmountWords);
        wordsPanel.Controls.Add(wordsCaption);

        numAmount.ValueChanged += (s, e) => lblAmountWords.Text = NumberToWords.Convert(numAmount.Value);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(15) };
        var btnSave = new Button { Text = "Save", Width = 90 };
        var btnDelete = new Button { Text = "Delete", Width = 90 };
        var btnPrint = new Button { Text = "Print", Width = 90 };
        var btnCancel = new Button { Text = "Cancel", Width = 90 };
        var btnBrowse = new Button { Text = "Browse All...", Width = 100 };
        btnSave.Click += BtnSave_Click;
        btnDelete.Click += BtnDelete_Click;
        btnPrint.Click += BtnPrint_Click;
        btnCancel.Click += (s, e) => ResetForm();
        btnBrowse.Click += (s, e) =>
        {
            var list = new ReceiptListForm();
            MdiHelper.ShowCentered(MdiParent, list);
        };
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnDelete);
        btnPanel.Controls.Add(btnPrint);
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnBrowse);

        Controls.Add(wordsPanel);
        Controls.Add(amountPanel);
        Controls.Add(t);
        Controls.Add(title);
        Controls.Add(btnPanel);
    }

    private void LoadLookups()
    {
        try
        {
            var accounts = DbHelper.ExecuteQuery(@"
                SELECT account_id, account_name FROM chart_of_accounts
                UNION SELECT c.account_id, c.customer_name FROM customer_master c WHERE c.account_id IS NOT NULL
                ORDER BY account_name");
            cboAccount.DisplayMember = "account_name";
            cboAccount.ValueMember = "account_id";
            cboAccount.DataSource = accounts;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load accounts: " + ex.Message);
        }

        var banks = DbHelper.ExecuteQuery("SELECT account_id, account_name FROM chart_of_accounts WHERE account_type='BANK' AND active ORDER BY account_name");
        cboBankAccount.DisplayMember = "account_name";
        cboBankAccount.ValueMember = "account_id";
        cboBankAccount.DataSource = banks;
        if (cboBankAccount.Items.Count > 0) cboBankAccount.SelectedIndex = 0;

        cboPaymentMode.Items.AddRange(new object[] { "Cash", "Bank Transfer", "Cheque", "Card" });
        cboPaymentMode.SelectedIndexChanged += (s, e) => UpdateBankState();
        cboPaymentMode.SelectedIndex = 0;
        cboReceivedBy.Items.AddRange(new object[] { "Admin" });
        cboReceivedBy.SelectedIndex = 0;
    }

    /// <summary>Bank Account picker only matters for non-cash payment modes.</summary>
    private void UpdateBankState()
    {
        bool isCash = cboPaymentMode.Text == "Cash";
        cboBankAccount.Enabled = !isCash;
        if (isCash) cboBankAccount.SelectedIndex = 0;
    }

    /// <summary>
    /// On edit, finds which bank/cash account the receipt actually posted to. The
    /// receipt's cash/bank leg is the ledger row whose account isn't the party
    /// account (the debit side for a receipt). Works for old receipts too, which
    /// don't store a bank account anywhere.
    /// </summary>
    private void RestoreBankFromLedger(string voucherNo, int partyAccountId)
    {
        try
        {
            var t = DbHelper.ExecuteQuery(@"
                SELECT l.account_id FROM ledger_entry l
                WHERE l.voucher_no=@no AND l.voucher_type='Receipt'
                  AND l.debit > 0 AND l.account_id <> @acc
                ORDER BY l.entry_id LIMIT 1",
                new Dictionary<string, object?> { ["no"] = voucherNo, ["acc"] = partyAccountId });
            if (t.Rows.Count > 0)
                cboBankAccount.SelectedValue = Convert.ToInt32(t.Rows[0]["account_id"]);
        }
        catch { /* leave the bank picker on its default */ }
    }

    private void NewReceiptNo()
    {
        try
        {
            var num = DbHelper.ExecuteScalar("SELECT prefix || LPAD(next_number::text, padding, '0') || COALESCE(suffix,'') FROM document_numbering WHERE doc_type='RECEIPT'");
            txtReceiptNo.Text = num?.ToString() ?? "RCPT-00001";
        }
        catch { txtReceiptNo.Text = "(auto on save)"; }
        dtReceiptDate.Value = DateTime.Today;
    }

    private void LoadForEdit(int receiptId)
    {
        try
        {
            var t = DbHelper.ExecuteQuery("SELECT * FROM receipt_voucher WHERE receipt_id=@id", new() { ["id"] = receiptId });
            if (t.Rows.Count == 0) { MessageBox.Show("That receipt no longer exists."); NewReceiptNo(); return; }
            var r = t.Rows[0];

            isEditMode = true;
            currentReceiptId = receiptId;
            txtReceiptNo.Text = r["receipt_no"].ToString();
            dtReceiptDate.Value = Convert.ToDateTime(r["receipt_date"]);
            cboAccount.SelectedValue = Convert.ToInt32(r["account_id"]);
            cboPaymentMode.Text = r["payment_mode"]?.ToString() ?? "Cash";
            UpdateBankState();
            RestoreBankFromLedger(txtReceiptNo.Text, (int)cboAccount.SelectedValue!);
            cboReceivedBy.Text = r["received_by"]?.ToString() ?? "Admin";
            txtReference.Text = r["reference"]?.ToString();
            numAmount.Value = Convert.ToDecimal(r["amount"]);
            lblAmountWords.Text = NumberToWords.Convert(numAmount.Value);
            Text = $"Receipt — Editing {txtReceiptNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load receipt for editing: " + ex.Message);
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (cboAccount.SelectedValue == null) { MessageBox.Show("Select an account."); return; }
        if (cboPaymentMode.Text != "Cash" && cboBankAccount.SelectedValue == null)
        {
            MessageBox.Show("Select a bank account for non-cash payment modes.");
            return;
        }
        if (numAmount.Value <= 0) { MessageBox.Show("Enter an amount."); return; }

        try
        {
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                int accountId = (int)cboAccount.SelectedValue!;
                string receiptNo;
                int receiptId;

                if (isEditMode && currentReceiptId != null)
                {
                    receiptId = currentReceiptId.Value;
                    receiptNo = txtReceiptNo.Text;
                    DbHelper.ReverseReceiptPostings(conn, tx, receiptNo);

                    using var updCmd = new Npgsql.NpgsqlCommand(@"
                        UPDATE receipt_voucher SET receipt_date=@dt, account_id=@acc, payment_mode=@mode,
                               received_by=@by, reference=@ref, amount=@amt
                        WHERE receipt_id=@id", conn, tx);
                    updCmd.Parameters.AddWithValue("dt", dtReceiptDate.Value.Date);
                    updCmd.Parameters.AddWithValue("acc", accountId);
                    updCmd.Parameters.AddWithValue("mode", cboPaymentMode.Text);
                    updCmd.Parameters.AddWithValue("by", cboReceivedBy.Text);
                    updCmd.Parameters.AddWithValue("ref", (object?)txtReference.Text ?? "");
                    updCmd.Parameters.AddWithValue("amt", numAmount.Value);
                    updCmd.Parameters.AddWithValue("id", receiptId);
                    updCmd.ExecuteNonQuery();
                }
                else
                {
                    receiptNo = DbHelper.GetNextDocumentNumber(conn, tx, "RECEIPT");
                    using var cmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO receipt_voucher (receipt_no, receipt_date, account_id, payment_mode, received_by, reference, amount)
                        VALUES (@no, @dt, @acc, @mode, @by, @ref, @amt) RETURNING receipt_id", conn, tx);
                    cmd.Parameters.AddWithValue("no", receiptNo);
                    cmd.Parameters.AddWithValue("dt", dtReceiptDate.Value.Date);
                    cmd.Parameters.AddWithValue("acc", accountId);
                    cmd.Parameters.AddWithValue("mode", cboPaymentMode.Text);
                    cmd.Parameters.AddWithValue("by", cboReceivedBy.Text);
                    cmd.Parameters.AddWithValue("ref", (object?)txtReference.Text ?? "");
                    cmd.Parameters.AddWithValue("amt", numAmount.Value);
                    receiptId = (int)cmd.ExecuteScalar()!;
                }

                // Ledger: Credit the account (money received reduces receivable), Debit Cash/Bank
                using var ledgerCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit)
                    VALUES (@no, 'Receipt', @acc, 'By Cash Receipt', 0, @amt)", conn, tx);
                ledgerCmd.Parameters.AddWithValue("no", receiptNo);
                ledgerCmd.Parameters.AddWithValue("acc", accountId);
                ledgerCmd.Parameters.AddWithValue("amt", numAmount.Value);
                ledgerCmd.ExecuteNonQuery();

                // Cash lands in Cash in Hand (1000); anything else goes to the picked bank account.
                int cashBankAccountId = cboPaymentMode.Text == "Cash"
                    ? DbHelper.GetAccountIdByCode(conn, tx, "1000")
                    : (int)cboBankAccount.SelectedValue!;
                using var cashCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit)
                    VALUES (@no, 'Receipt', @acc, 'To Cash Receipt', @amt, 0)", conn, tx);
                cashCmd.Parameters.AddWithValue("no", receiptNo);
                cashCmd.Parameters.AddWithValue("acc", cashBankAccountId);
                cashCmd.Parameters.AddWithValue("amt", numAmount.Value);
                cashCmd.ExecuteNonQuery();

                txtReceiptNo.Text = receiptNo;
                currentReceiptId = receiptId;
                isEditMode = true;
            });

            DbHelper.LogAction($"Receipt: Saved {txtReceiptNo.Text}");
            MessageBox.Show("Receipt saved successfully.");
            Text = $"Receipt — Editing {txtReceiptNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (currentReceiptId == null) { MessageBox.Show("Nothing saved yet to delete."); return; }
        if (MessageBox.Show($"Delete receipt {txtReceiptNo.Text}?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            int receiptId = currentReceiptId.Value;
            string receiptNo = txtReceiptNo.Text;
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                DbHelper.ReverseReceiptPostings(conn, tx, receiptNo);
                using var delCmd = new Npgsql.NpgsqlCommand("DELETE FROM receipt_voucher WHERE receipt_id=@id", conn, tx);
                delCmd.Parameters.AddWithValue("id", receiptId);
                delCmd.ExecuteNonQuery();
            });
            DbHelper.LogAction($"Receipt: Deleted {receiptNo}");
            MessageBox.Show("Receipt deleted.");
            ResetForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message);
        }
    }

    private void BtnPrint_Click(object? sender, EventArgs e)
    {
        if (currentReceiptId == null) { MessageBox.Show("Save the receipt first."); return; }
        using var printForm = new VoucherPrintForm(currentReceiptId.Value, VoucherType.Receipt);
        printForm.ShowDialog(this);
    }

    private void ResetForm()
    {
        currentReceiptId = null;
        isEditMode = false;
        NewReceiptNo();
        cboAccount.SelectedIndex = -1;
        cboPaymentMode.SelectedIndex = 0; // triggers UpdateBankState via SelectedIndexChanged
        if (cboBankAccount.Items.Count > 0) cboBankAccount.SelectedIndex = 0;
        txtReference.Clear();
        numAmount.Value = 0;
        lblAmountWords.Text = "";
        Text = "Receipt";
    }
}
