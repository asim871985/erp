using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>The four printable document types this app produces.</summary>
public enum PrintDocType { SalesInvoice, PurchaseBill, SalesReturn, PurchaseReturn }

/// <summary>Plain data holder for one printable Sales Invoice, Purchase Bill, Sales Return, or Purchase Return.</summary>
public class InvoiceDocumentData
{
    public PrintDocType DocType;
    public bool IsPurchase => DocType is PrintDocType.PurchaseBill or PrintDocType.PurchaseReturn;
    public string CompanyName = "", CompanyAddress = "", CompanyPhone = "", CompanyEmail = "";
    public string DocNo = "", DocDate = "", DueDate = "";
    public string PartyLabel = "", PartyName = "", PartyAddress = "";
    public string WarehouseName = ""; // filled from the document's stock_movement rows
    public DataTable Items = new();
    public decimal SubTotal, Discount, GrandTotal;
    public string AmountInWords = "";

    public const int DocWidth = 780;
    public const int DocHeight = 1000;

    /// <summary>Back-compat overload for existing call sites that only know Sales Invoice vs Purchase Bill.</summary>
    public static InvoiceDocumentData? Load(int documentId, bool isPurchase) =>
        Load(documentId, isPurchase ? PrintDocType.PurchaseBill : PrintDocType.SalesInvoice);

    /// <summary>Loads one document's data from the database. Returns null if it no longer exists.</summary>
    public static InvoiceDocumentData? Load(int documentId, PrintDocType docType)
    {
        var data = new InvoiceDocumentData { DocType = docType };

        var company = DbHelper.ExecuteQuery("SELECT * FROM company_profile ORDER BY company_id LIMIT 1");
        if (company.Rows.Count > 0)
        {
            var c = company.Rows[0];
            data.CompanyName = c["company_name"]?.ToString() ?? "";
            data.CompanyAddress = c["address"]?.ToString() ?? "";
            data.CompanyPhone = c["phone"]?.ToString() ?? "";
            data.CompanyEmail = c["email"]?.ToString() ?? "";
        }

        switch (docType)
        {
            case PrintDocType.PurchaseBill:
            {
                var t = DbHelper.ExecuteQuery(@"
                    SELECT pb.*, s.supplier_name, s.address AS supplier_address
                    FROM purchase_bill pb LEFT JOIN supplier_master s ON s.supplier_id = pb.supplier_id
                    WHERE pb.purchase_id=@id", new Dictionary<string, object?> { ["id"] = documentId });
                if (t.Rows.Count == 0) return null;
                var r = t.Rows[0];

                data.DocNo = r["bill_no"].ToString() ?? "";
                data.DocDate = Convert.ToDateTime(r["bill_date"]).ToString("dd/MM/yyyy");
                data.DueDate = r["due_date"] is DBNull ? "-" : Convert.ToDateTime(r["due_date"]).ToString("dd/MM/yyyy");
                data.PartyLabel = "Supplier";
                data.PartyName = r["supplier_name"]?.ToString() ?? "";
                data.PartyAddress = r["supplier_address"]?.ToString() ?? "";
                data.SubTotal = Convert.ToDecimal(r["sub_total"]);
                data.Discount = Convert.ToDecimal(r["discount"]);
                data.GrandTotal = Convert.ToDecimal(r["grand_total"]);
                data.AmountInWords = NumberToWords.Convert(data.GrandTotal);

                data.Items = DbHelper.ExecuteQuery(@"
                    SELECT i.item_name, i.model, u.uom_name, pi.qty, pi.rate, pi.disc_percent, pi.amount
                    FROM purchase_bill_item pi
                    LEFT JOIN item_master i ON i.item_id = pi.item_id
                    LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                    WHERE pi.purchase_id=@id ORDER BY pi.line_id", new Dictionary<string, object?> { ["id"] = documentId });
                break;
            }

            case PrintDocType.SalesInvoice:
            {
                var t = DbHelper.ExecuteQuery(@"
                    SELECT si.*, c.customer_name, c.address AS customer_address
                    FROM sales_invoice si LEFT JOIN customer_master c ON c.customer_id = si.customer_id
                    WHERE si.invoice_id=@id", new Dictionary<string, object?> { ["id"] = documentId });
                if (t.Rows.Count == 0) return null;
                var r = t.Rows[0];

                data.DocNo = r["invoice_no"].ToString() ?? "";
                data.DocDate = Convert.ToDateTime(r["invoice_date"]).ToString("dd/MM/yyyy");
                data.DueDate = r["due_date"] is DBNull ? data.DocDate : Convert.ToDateTime(r["due_date"]).ToString("dd/MM/yyyy");
                data.PartyLabel = "Customer";
                data.PartyName = r["customer_name"]?.ToString() ?? "";
                data.PartyAddress = r["address"]?.ToString() ?? r["customer_address"]?.ToString() ?? "";
                data.SubTotal = Convert.ToDecimal(r["sub_total"]);
                data.Discount = Convert.ToDecimal(r["discount"]);
                data.GrandTotal = Convert.ToDecimal(r["grand_total"]);
                data.AmountInWords = r["amount_in_words"]?.ToString() ?? NumberToWords.Convert(data.GrandTotal);

                data.Items = DbHelper.ExecuteQuery(@"
                    SELECT i.item_name, i.model, u.uom_name, si.qty, si.rate, si.disc_percent, si.amount
                    FROM sales_invoice_item si
                    LEFT JOIN item_master i ON i.item_id = si.item_id
                    LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                    WHERE si.invoice_id=@id ORDER BY si.line_id", new Dictionary<string, object?> { ["id"] = documentId });
                break;
            }

            case PrintDocType.PurchaseReturn:
            {
                var t = DbHelper.ExecuteQuery(@"
                    SELECT pr.*, s.supplier_name, s.address AS supplier_address
                    FROM purchase_return pr LEFT JOIN supplier_master s ON s.supplier_id = pr.supplier_id
                    WHERE pr.return_id=@id", new Dictionary<string, object?> { ["id"] = documentId });
                if (t.Rows.Count == 0) return null;
                var r = t.Rows[0];

                data.DocNo = r["return_no"].ToString() ?? "";
                data.DocDate = Convert.ToDateTime(r["return_date"]).ToString("dd/MM/yyyy");
                data.DueDate = "-";
                data.PartyLabel = "Supplier";
                data.PartyName = r["supplier_name"]?.ToString() ?? "";
                data.PartyAddress = r["supplier_address"]?.ToString() ?? "";
                data.GrandTotal = Convert.ToDecimal(r["total_amount"]);
                data.AmountInWords = NumberToWords.Convert(data.GrandTotal);

                data.Items = DbHelper.ExecuteQuery(@"
                    SELECT i.item_name, i.model, u.uom_name, pri.qty, pri.rate, pri.disc_percent, pri.amount
                    FROM purchase_return_item pri
                    LEFT JOIN item_master i ON i.item_id = pri.item_id
                    LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                    WHERE pri.return_id=@id ORDER BY pri.line_id", new Dictionary<string, object?> { ["id"] = documentId });

                data.SubTotal = data.Items.AsEnumerable().Sum(row => Convert.ToDecimal(row["qty"]) * Convert.ToDecimal(row["rate"]));
                data.Discount = data.SubTotal - data.GrandTotal;
                break;
            }

            case PrintDocType.SalesReturn:
            default:
            {
                var t = DbHelper.ExecuteQuery(@"
                    SELECT sr.*, c.customer_name, c.address AS customer_address
                    FROM sales_return sr LEFT JOIN customer_master c ON c.customer_id = sr.customer_id
                    WHERE sr.return_id=@id", new Dictionary<string, object?> { ["id"] = documentId });
                if (t.Rows.Count == 0) return null;
                var r = t.Rows[0];

                data.DocNo = r["return_no"].ToString() ?? "";
                data.DocDate = Convert.ToDateTime(r["return_date"]).ToString("dd/MM/yyyy");
                data.DueDate = "-";
                data.PartyLabel = "Customer";
                data.PartyName = r["customer_name"]?.ToString() ?? "";
                data.PartyAddress = r["customer_address"]?.ToString() ?? "";
                data.GrandTotal = Convert.ToDecimal(r["total_amount"]);
                data.AmountInWords = NumberToWords.Convert(data.GrandTotal);

                data.Items = DbHelper.ExecuteQuery(@"
                    SELECT i.item_name, i.model, u.uom_name, sri.qty, sri.rate, sri.disc_percent, sri.amount
                    FROM sales_return_item sri
                    LEFT JOIN item_master i ON i.item_id = sri.item_id
                    LEFT JOIN uom_master u ON u.uom_id = i.uom_id
                    WHERE sri.return_id=@id ORDER BY sri.line_id", new Dictionary<string, object?> { ["id"] = documentId });

                data.SubTotal = data.Items.AsEnumerable().Sum(row => Convert.ToDecimal(row["qty"]) * Convert.ToDecimal(row["rate"]));
                data.Discount = data.SubTotal - data.GrandTotal;
                break;
            }
        }

        // The document's warehouse(s) come from its stock_movement rows (each document
        // is saved with one warehouse picker, so usually a single name). Old documents
        // saved before per-warehouse balances have no warehouse recorded → blank.
        string refType = docType switch
        {
            PrintDocType.SalesInvoice => "SALES",
            PrintDocType.PurchaseBill => "PURCHASE",
            PrintDocType.SalesReturn => "SALES_RETURN",
            PrintDocType.PurchaseReturn => "PURCHASE_RETURN",
            _ => ""
        };
        if (refType.Length > 0)
        {
            var wh = DbHelper.ExecuteQuery(@"
                SELECT DISTINCT w.warehouse_name FROM stock_movement sm
                LEFT JOIN warehouse_master w ON w.warehouse_id = sm.warehouse_id
                WHERE sm.reference_type=@rt AND sm.reference_id=@id AND w.warehouse_name IS NOT NULL",
                new Dictionary<string, object?> { ["rt"] = refType, ["id"] = documentId });
            if (wh.Rows.Count > 0)
                data.WarehouseName = string.Join(", ", wh.Rows.Cast<DataRow>().Select(r => r["warehouse_name"].ToString()));
        }

        return data;
    }

    public static string BadgeText(PrintDocType t) => t switch
    {
        PrintDocType.SalesInvoice => "Sales Invoice",
        PrintDocType.PurchaseBill => "Purchase Invoice",
        PrintDocType.SalesReturn => "Sales Return",
        PrintDocType.PurchaseReturn => "Purchase Return",
        _ => "Document"
    };
}

/// <summary>The single drawing routine shared by the on-screen preview and every printer output.</summary>
public static class InvoiceDocumentRenderer
{
    public static void Draw(Graphics g, float scale, InvoiceDocumentData d)
    {
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(10, 10);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var titleFont = new Font("Segoe UI", 16, FontStyle.Bold);
        using var headerFont = new Font("Segoe UI", 9, FontStyle.Regular);
        using var boldFont = new Font("Segoe UI", 9, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 8, FontStyle.Regular);
        using var badgeFont = new Font("Segoe UI", 10, FontStyle.Bold);
        using var black = new SolidBrush(Color.Black);
        using var gray = new SolidBrush(Color.DimGray);
        using var white = new SolidBrush(Color.White);
        using var badgeBg = new SolidBrush(d.DocType is PrintDocType.SalesReturn or PrintDocType.PurchaseReturn ? Color.IndianRed : Color.SteelBlue);
        using var linePen = new Pen(Color.Black, 1);
        using var canvasBrush = new SolidBrush(Color.White);

        int docWidth = InvoiceDocumentData.DocWidth;
        int docHeight = InvoiceDocumentData.DocHeight;

        g.FillRectangle(canvasBrush, 0, 0, docWidth, docHeight);
        g.DrawRectangle(Pens.LightGray, 0, 0, docWidth, docHeight);

        float x = 30, y = 25;

        // Company header
        g.DrawString(d.CompanyName, titleFont, black, x, y); y += 28;
        g.DrawString(d.CompanyAddress, headerFont, gray, x, y); y += 16;
        g.DrawString($"Phone: {d.CompanyPhone}   Email: {d.CompanyEmail}", headerFont, gray, x, y);

        // Badge top-right
        string badgeText = InvoiceDocumentData.BadgeText(d.DocType);
        var badgeSize = g.MeasureString(badgeText, badgeFont);
        float badgeW = badgeSize.Width + 24, badgeH = 26;
        g.FillRectangle(badgeBg, docWidth - 30 - badgeW, 25, badgeW, badgeH);
        g.DrawString(badgeText, badgeFont, white, docWidth - 30 - badgeW + 12, 25 + 5);

        // Doc info top-right, under badge
        bool isReturn = d.DocType is PrintDocType.SalesReturn or PrintDocType.PurchaseReturn;
        string noLabel = isReturn ? "Return No" : "Invoice No";
        string dateLabel = isReturn ? "Return Date" : "Invoice Date";
        float infoY = 25 + badgeH + 10;
        DrawRightAligned(g, $"{noLabel} : {d.DocNo}", headerFont, black, docWidth - 30, infoY); infoY += 16;
        DrawRightAligned(g, $"{dateLabel} : {d.DocDate}", headerFont, black, docWidth - 30, infoY); infoY += 16;
        if (!isReturn)
            DrawRightAligned(g, $"Due Date : {d.DueDate}", headerFont, black, docWidth - 30, infoY);

        y += 45;
        g.DrawLine(linePen, x, y, docWidth - 30, y); y += 15;

        // Party info
        g.DrawString($"{d.PartyLabel} :", boldFont, black, x, y); y += 16;
        g.DrawString(d.PartyName, headerFont, black, x, y); y += 16;
        g.DrawString(d.PartyAddress, headerFont, gray, x, y); y += 18;
        if (!string.IsNullOrEmpty(d.WarehouseName))
        {
            g.DrawString($"Warehouse : {d.WarehouseName}", headerFont, black, x, y);
            y += 16;
        }
        y += 12;

        // Item table header
        float[] colX = { x, x + 40, x + 260, x + 340, x + 400, x + 460, x + 540, x + 610 };
        string[] headers = { "S.No", "Item Name", "Model", "UOM", "Qty", "Rate", "Disc %", "Amount" };
        g.FillRectangle(new SolidBrush(Color.WhiteSmoke), x, y, docWidth - 60, 22);
        for (int i = 0; i < headers.Length; i++)
            g.DrawString(headers[i], boldFont, black, colX[i], y + 4);
        y += 22;
        g.DrawLine(linePen, x, y, docWidth - 30, y);
        y += 4;

        int sno = 1;
        foreach (DataRow row in d.Items.Rows)
        {
            g.DrawString(sno.ToString(), smallFont, black, colX[0], y);
            g.DrawString(row["item_name"]?.ToString() ?? "", smallFont, black, colX[1], y);
            g.DrawString(row["model"]?.ToString() ?? "", smallFont, black, colX[2], y);
            g.DrawString(row["uom_name"]?.ToString() ?? "", smallFont, black, colX[3], y);
            g.DrawString(Convert.ToDecimal(row["qty"]).ToString("N2"), smallFont, black, colX[4], y);
            g.DrawString(Convert.ToDecimal(row["rate"]).ToString("N2"), smallFont, black, colX[5], y);
            g.DrawString(Convert.ToDecimal(row["disc_percent"]).ToString("N2"), smallFont, black, colX[6], y);
            g.DrawString(Convert.ToDecimal(row["amount"]).ToString("N2"), smallFont, black, colX[7], y);
            y += 18;
            sno++;
        }

        y += 6;
        g.DrawLine(linePen, x, y, docWidth - 30, y);
        y += 12;

        // Totals block (right aligned)
        DrawRightAligned(g, $"Sub Total :        {d.SubTotal:N2}", headerFont, black, docWidth - 30, y); y += 16;
        DrawRightAligned(g, $"Discount :         {d.Discount:N2}", headerFont, black, docWidth - 30, y); y += 16;
        DrawRightAligned(g, $"Total Amount :   {d.GrandTotal:N2}", boldFont, black, docWidth - 30, y);
        y += 30;

        g.DrawString("Amount in Words :", boldFont, black, x, y); y += 16;
        g.DrawString(d.AmountInWords, headerFont, gray, x, y);

        // Signature line near the bottom of the page
        float sigY = docHeight - 60;
        g.DrawLine(linePen, docWidth - 220, sigY, docWidth - 30, sigY);
        g.DrawString("Authorized Signature", smallFont, gray, docWidth - 210, sigY + 4);
    }

    public static void DrawRightAligned(Graphics g, string text, Font font, Brush brush, float rightX, float y)
    {
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, rightX - size.Width, y);
    }
}
