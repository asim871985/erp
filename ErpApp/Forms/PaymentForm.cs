using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Matches the "Payment" window in screenshot 2.</summary>
public class PaymentForm : AppFormBase
{
    private readonly TextBox txtPaymentNo = new() { ReadOnly = true };
    private readonly DateTimePicker dtPaymentDate = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboAccount = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboPaymentMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboBankAccount = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboPaidBy = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtReference = new();
    private readonly NumericUpDown numAmount = new() { DecimalPlaces = 2, Maximum = 100_000_000, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
    private readonly Label lblAmountWords = new();

    private int? currentPaymentId;
    private bool isEditMode;

    public PaymentForm() : this(null) { }

    public PaymentForm(int? editPaymentId)
    {
        Text = "Payment";
        Width = 520;
        Height = 480;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadLookups();

        if (editPaymentId != null)
            LoadForEdit(editPaymentId.Value);
        else
            NewPaymentNo();
    }

    private void BuildLayout()
    {
        var title = new Label { Text = "PAYMENT", Dock = DockStyle.Top, Height = 35, Font = new Font("Segoe UI", 13, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

        var t = new TableLayoutPanel { Dock = DockStyle.Top, Height = 300, ColumnCount = 2, Padding = new Padding(15) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c);
        }

        Row("Payment No.", txtPaymentNo);
        Row("Payment Date", dtPaymentDate);
        Row("Account", cboAccount);
        Row("Payment Mode", cboPaymentMode);
        Row("Bank Account", cboBankAccount);
        Row("Paid By", cboPaidBy);
        Row("Reference", txtReference);

        int fieldRowCount = t.Controls.Count / t.ColumnCount;
        for (int i = 0; i < fieldRowCount; i++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / fieldRowCount));

        var amountPanel = new GroupBox { Text = "", Dock = DockStyle.Top, Height = 100 };
        var lblCaption = new Label { Text = "Amount Paid", Location = new Point(15, 15), AutoSize = true };
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
            var list = new PaymentListForm();
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
                UNION SELECT s.account_id, s.supplier_name FROM supplier_master s WHERE s.account_id IS NOT NULL
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
        cboPaidBy.Items.AddRange(new object[] { "Admin" });
        cboPaidBy.SelectedIndex = 0;
    }

    /// <summary>Bank Account picker only matters for non-cash payment modes.</summary>
    private void UpdateBankState()
    {
        bool isCash = cboPaymentMode.Text == "Cash";
        cboBankAccount.Enabled = !isCash;
        if (isCash) cboBankAccount.SelectedIndex = 0;
    }

    /// <summary>
    /// On edit, finds which bank/cash account the payment actually posted to. The
    /// payment's cash/bank leg is the ledger row whose account isn't the party
    /// account (the credit side for a payment). Works for old payments too, which
    /// don't store a bank account anywhere.
    /// </summary>
    private void RestoreBankFromLedger(string voucherNo, int partyAccountId)
    {
        try
        {
            var t = DbHelper.ExecuteQuery(@"
                SELECT l.account_id FROM ledger_entry l
                WHERE l.voucher_no=@no AND l.voucher_type='Payment'
                  AND l.credit > 0 AND l.account_id <> @acc
                ORDER BY l.entry_id LIMIT 1",
                new Dictionary<string, object?> { ["no"] = voucherNo, ["acc"] = partyAccountId });
            if (t.Rows.Count > 0)
                cboBankAccount.SelectedValue = Convert.ToInt32(t.Rows[0]["account_id"]);
        }
        catch { /* leave the bank picker on its default */ }
    }

    private void NewPaymentNo()
    {
        try
        {
            var num = DbHelper.ExecuteScalar("SELECT prefix || LPAD(next_number::text, padding, '0') || COALESCE(suffix,'') FROM document_numbering WHERE doc_type='PAYMENT'");
            txtPaymentNo.Text = num?.ToString() ?? "PAY-00001";
        }
        catch { txtPaymentNo.Text = "(auto on save)"; }
        dtPaymentDate.Value = DateTime.Today;
    }

    private void LoadForEdit(int paymentId)
    {
        try
        {
            var t = DbHelper.ExecuteQuery("SELECT * FROM payment_voucher WHERE payment_id=@id", new() { ["id"] = paymentId });
            if (t.Rows.Count == 0) { MessageBox.Show("That payment no longer exists."); NewPaymentNo(); return; }
            var r = t.Rows[0];

            isEditMode = true;
            currentPaymentId = paymentId;
            txtPaymentNo.Text = r["payment_no"].ToString();
            dtPaymentDate.Value = Convert.ToDateTime(r["payment_date"]);
            cboAccount.SelectedValue = Convert.ToInt32(r["account_id"]);
            cboPaymentMode.Text = r["payment_mode"]?.ToString() ?? "Cash";
            UpdateBankState();
            RestoreBankFromLedger(txtPaymentNo.Text, (int)cboAccount.SelectedValue!);
            cboPaidBy.Text = r["paid_by"]?.ToString() ?? "Admin";
            txtReference.Text = r["reference"]?.ToString();
            numAmount.Value = Convert.ToDecimal(r["amount"]);
            lblAmountWords.Text = NumberToWords.Convert(numAmount.Value);
            Text = $"Payment — Editing {txtPaymentNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load payment for editing: " + ex.Message);
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
                string paymentNo;
                int paymentId;

                if (isEditMode && currentPaymentId != null)
                {
                    paymentId = currentPaymentId.Value;
                    paymentNo = txtPaymentNo.Text;
                    DbHelper.ReversePaymentPostings(conn, tx, paymentNo);

                    using var updCmd = new Npgsql.NpgsqlCommand(@"
                        UPDATE payment_voucher SET payment_date=@dt, account_id=@acc, payment_mode=@mode,
                               paid_by=@by, reference=@ref, amount=@amt
                        WHERE payment_id=@id", conn, tx);
                    updCmd.Parameters.AddWithValue("dt", dtPaymentDate.Value.Date);
                    updCmd.Parameters.AddWithValue("acc", accountId);
                    updCmd.Parameters.AddWithValue("mode", cboPaymentMode.Text);
                    updCmd.Parameters.AddWithValue("by", cboPaidBy.Text);
                    updCmd.Parameters.AddWithValue("ref", (object?)txtReference.Text ?? "");
                    updCmd.Parameters.AddWithValue("amt", numAmount.Value);
                    updCmd.Parameters.AddWithValue("id", paymentId);
                    updCmd.ExecuteNonQuery();
                }
                else
                {
                    paymentNo = DbHelper.GetNextDocumentNumber(conn, tx, "PAYMENT");
                    using var cmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO payment_voucher (payment_no, payment_date, account_id, payment_mode, paid_by, reference, amount)
                        VALUES (@no, @dt, @acc, @mode, @by, @ref, @amt) RETURNING payment_id", conn, tx);
                    cmd.Parameters.AddWithValue("no", paymentNo);
                    cmd.Parameters.AddWithValue("dt", dtPaymentDate.Value.Date);
                    cmd.Parameters.AddWithValue("acc", accountId);
                    cmd.Parameters.AddWithValue("mode", cboPaymentMode.Text);
                    cmd.Parameters.AddWithValue("by", cboPaidBy.Text);
                    cmd.Parameters.AddWithValue("ref", (object?)txtReference.Text ?? "");
                    cmd.Parameters.AddWithValue("amt", numAmount.Value);
                    paymentId = (int)cmd.ExecuteScalar()!;
                }

                // Ledger: Debit the payable account (reduces liability), Credit Cash/Bank
                using var ledgerCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit)
                    VALUES (@no, 'Payment', @acc, 'By Cash Payment', @amt, 0)", conn, tx);
                ledgerCmd.Parameters.AddWithValue("no", paymentNo);
                ledgerCmd.Parameters.AddWithValue("acc", accountId);
                ledgerCmd.Parameters.AddWithValue("amt", numAmount.Value);
                ledgerCmd.ExecuteNonQuery();

                // Cash comes out of Cash in Hand (1000); anything else from the picked bank account.
                int cashBankAccountId = cboPaymentMode.Text == "Cash"
                    ? DbHelper.GetAccountIdByCode(conn, tx, "1000")
                    : (int)cboBankAccount.SelectedValue!;
                using var cashCmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO ledger_entry (voucher_no, voucher_type, account_id, particulars, debit, credit)
                    VALUES (@no, 'Payment', @acc, 'To Cash Payment', 0, @amt)", conn, tx);
                cashCmd.Parameters.AddWithValue("no", paymentNo);
                cashCmd.Parameters.AddWithValue("acc", cashBankAccountId);
                cashCmd.Parameters.AddWithValue("amt", numAmount.Value);
                cashCmd.ExecuteNonQuery();

                txtPaymentNo.Text = paymentNo;
                currentPaymentId = paymentId;
                isEditMode = true;
            });

            DbHelper.LogAction($"Payment: Saved {txtPaymentNo.Text}");
            MessageBox.Show("Payment saved successfully.");
            Text = $"Payment — Editing {txtPaymentNo.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message);
        }
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (currentPaymentId == null) { MessageBox.Show("Nothing saved yet to delete."); return; }
        if (MessageBox.Show($"Delete payment {txtPaymentNo.Text}?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            int paymentId = currentPaymentId.Value;
            string paymentNo = txtPaymentNo.Text;
            DbHelper.ExecuteTransaction((conn, tx) =>
            {
                DbHelper.ReversePaymentPostings(conn, tx, paymentNo);
                using var delCmd = new Npgsql.NpgsqlCommand("DELETE FROM payment_voucher WHERE payment_id=@id", conn, tx);
                delCmd.Parameters.AddWithValue("id", paymentId);
                delCmd.ExecuteNonQuery();
            });
            DbHelper.LogAction($"Payment: Deleted {paymentNo}");
            MessageBox.Show("Payment deleted.");
            ResetForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message);
        }
    }

    private void BtnPrint_Click(object? sender, EventArgs e)
    {
        if (currentPaymentId == null) { MessageBox.Show("Save the payment first."); return; }
        using var printForm = new VoucherPrintForm(currentPaymentId.Value, VoucherType.Payment);
        printForm.ShowDialog(this);
    }

    private void ResetForm()
    {
        currentPaymentId = null;
        isEditMode = false;
        NewPaymentNo();
        cboAccount.SelectedIndex = -1;
        cboPaymentMode.SelectedIndex = 0; // triggers UpdateBankState via SelectedIndexChanged
        if (cboBankAccount.Items.Count > 0) cboBankAccount.SelectedIndex = 0;
        txtReference.Clear();
        numAmount.Value = 0;
        lblAmountWords.Text = "";
        Text = "Payment";
    }
}
