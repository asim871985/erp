using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

public enum VoucherType { Receipt, Payment }

/// <summary>Plain data holder for one printable Receipt or Payment voucher.</summary>
public class VoucherDocumentData
{
    public VoucherType Type;
    public string CompanyName = "", CompanyAddress = "", CompanyPhone = "", CompanyEmail = "";
    public string VoucherNo = "", VoucherDate = "";
    public string AccountName = "", PaymentMode = "", HandledBy = "", Reference = "";
    public string BankName = ""; // filled from the ledger leg for non-cash vouchers
    public decimal Amount;
    public string AmountInWords = "";

    public const int DocWidth = 700;
    public const int DocHeight = 500;

    public static VoucherDocumentData? Load(int id, VoucherType type)
    {
        var data = new VoucherDocumentData { Type = type };

        var company = DbHelper.ExecuteQuery("SELECT * FROM company_profile ORDER BY company_id LIMIT 1");
        if (company.Rows.Count > 0)
        {
            var c = company.Rows[0];
            data.CompanyName = c["company_name"]?.ToString() ?? "";
            data.CompanyAddress = c["address"]?.ToString() ?? "";
            data.CompanyPhone = c["phone"]?.ToString() ?? "";
            data.CompanyEmail = c["email"]?.ToString() ?? "";
        }

        if (type == VoucherType.Receipt)
        {
            var t = DbHelper.ExecuteQuery(@"
                SELECT rv.*, a.account_name
                FROM receipt_voucher rv LEFT JOIN chart_of_accounts a ON a.account_id = rv.account_id
                WHERE rv.receipt_id=@id", new Dictionary<string, object?> { ["id"] = id });
            if (t.Rows.Count == 0) return null;
            var r = t.Rows[0];

            data.VoucherNo = r["receipt_no"].ToString() ?? "";
            data.VoucherDate = Convert.ToDateTime(r["receipt_date"]).ToString("dd/MM/yyyy");
            data.AccountName = r["account_name"]?.ToString() ?? "";
            data.PaymentMode = r["payment_mode"]?.ToString() ?? "";
            data.HandledBy = r["received_by"]?.ToString() ?? "";
            data.Reference = r["reference"]?.ToString() ?? "";
            data.Amount = Convert.ToDecimal(r["amount"]);
        }
        else
        {
            var t = DbHelper.ExecuteQuery(@"
                SELECT pv.*, a.account_name
                FROM payment_voucher pv LEFT JOIN chart_of_accounts a ON a.account_id = pv.account_id
                WHERE pv.payment_id=@id", new Dictionary<string, object?> { ["id"] = id });
            if (t.Rows.Count == 0) return null;
            var r = t.Rows[0];

            data.VoucherNo = r["payment_no"].ToString() ?? "";
            data.VoucherDate = Convert.ToDateTime(r["payment_date"]).ToString("dd/MM/yyyy");
            data.AccountName = r["account_name"]?.ToString() ?? "";
            data.PaymentMode = r["payment_mode"]?.ToString() ?? "";
            data.HandledBy = r["paid_by"]?.ToString() ?? "";
            data.Reference = r["reference"]?.ToString() ?? "";
            data.Amount = Convert.ToDecimal(r["amount"]);
        }

        // Bank account actually posted to for non-cash vouchers (the cash/bank leg:
        // debit for a receipt, credit for a payment). The party account is always on
        // the opposite side, so the side check alone isolates the bank leg.
        if (data.PaymentMode != "Cash")
        {
            string leg = type == VoucherType.Receipt ? "debit" : "credit";
            var bank = DbHelper.ExecuteQuery(@$"
                SELECT a.account_name FROM ledger_entry l
                JOIN chart_of_accounts a ON a.account_id = l.account_id
                WHERE l.voucher_no=@no AND l.voucher_type=@vt AND l.{leg} > 0
                ORDER BY l.entry_id LIMIT 1",
                new Dictionary<string, object?> { ["no"] = data.VoucherNo, ["vt"] = type == VoucherType.Receipt ? "Receipt" : "Payment" });
            if (bank.Rows.Count > 0) data.BankName = bank.Rows[0]["account_name"]?.ToString() ?? "";
        }

        data.AmountInWords = NumberToWords.Convert(data.Amount);
        return data;
    }
}

/// <summary>Drawing routine shared by the on-screen preview and every printer output for vouchers.</summary>
public static class VoucherDocumentRenderer
{
    public static void Draw(Graphics g, float scale, VoucherDocumentData d)
    {
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(10, 10);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var titleFont = new Font("Segoe UI", 14, FontStyle.Bold);
        using var headerFont = new Font("Segoe UI", 9, FontStyle.Regular);
        using var boldFont = new Font("Segoe UI", 9, FontStyle.Bold);
        using var badgeFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using var amountFont = new Font("Segoe UI", 16, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 8, FontStyle.Regular);
        using var black = new SolidBrush(Color.Black);
        using var gray = new SolidBrush(Color.DimGray);
        using var white = new SolidBrush(Color.White);
        using var badgeBg = new SolidBrush(d.Type == VoucherType.Receipt ? Color.SeaGreen : Color.IndianRed);
        using var linePen = new Pen(Color.Black, 1);
        using var canvasBrush = new SolidBrush(Color.White);

        int w = VoucherDocumentData.DocWidth, h = VoucherDocumentData.DocHeight;
        g.FillRectangle(canvasBrush, 0, 0, w, h);
        g.DrawRectangle(Pens.LightGray, 0, 0, w, h);

        float x = 30, y = 25;
        g.DrawString(d.CompanyName, titleFont, black, x, y); y += 24;
        g.DrawString(d.CompanyAddress, headerFont, gray, x, y); y += 15;
        g.DrawString($"Phone: {d.CompanyPhone}   Email: {d.CompanyEmail}", headerFont, gray, x, y);

        string badgeText = d.Type == VoucherType.Receipt ? "RECEIPT" : "PAYMENT";
        var badgeSize = g.MeasureString(badgeText, badgeFont);
        float badgeW = badgeSize.Width + 24, badgeH = 26;
        g.FillRectangle(badgeBg, w - 30 - badgeW, 25, badgeW, badgeH);
        g.DrawString(badgeText, badgeFont, white, w - 30 - badgeW + 12, 30);

        y += 40;
        g.DrawLine(linePen, x, y, w - 30, y);
        y += 20;

        void Row(string label, string value)
        {
            g.DrawString(label, boldFont, black, x, y);
            g.DrawString(value, headerFont, black, x + 160, y);
            y += 24;
        }

        Row("Voucher No.", d.VoucherNo);
        Row("Date", d.VoucherDate);
        Row(d.Type == VoucherType.Receipt ? "Received From" : "Paid To", d.AccountName);
        Row("Payment Mode", d.PaymentMode);
        if (!string.IsNullOrEmpty(d.BankName)) Row("Bank Account", d.BankName);
        Row(d.Type == VoucherType.Receipt ? "Received By" : "Paid By", d.HandledBy);
        Row("Reference", d.Reference);

        y += 15;
        g.DrawLine(linePen, x, y, w - 30, y);
        y += 20;

        g.DrawString(d.Type == VoucherType.Receipt ? "Amount Received" : "Amount Paid", boldFont, gray, x, y);
        y += 22;
        g.DrawString(d.Amount.ToString("N2"), amountFont, black, x, y);
        y += 40;

        g.DrawString("Amount in Words :", boldFont, black, x, y); y += 16;
        g.DrawString(d.AmountInWords, headerFont, gray, x, y);

        float sigY = h - 55;
        g.DrawLine(linePen, w - 220, sigY, w - 30, sigY);
        g.DrawString("Authorized Signature", smallFont, gray, w - 210, sigY + 4);
    }
}
