using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Plain data holder for one printable Stock Transfer note.</summary>
public class StockTransferDocumentData
{
    public string CompanyName = "", CompanyAddress = "", CompanyPhone = "", CompanyEmail = "";
    public string TransferNo = "", TransferDate = "";
    public string FromWarehouse = "", ToWarehouse = "";
    public string Remarks = "";
    public DataTable Items = new();
    public decimal TotalQty;

    public const int DocWidth = 700;
    public const int DocHeight = 800;

    /// <summary>Loads one transfer's data. Returns null if it no longer exists.</summary>
    public static StockTransferDocumentData? Load(int transferId)
    {
        var data = new StockTransferDocumentData();

        var company = DbHelper.ExecuteQuery("SELECT * FROM company_profile ORDER BY company_id LIMIT 1");
        if (company.Rows.Count > 0)
        {
            var c = company.Rows[0];
            data.CompanyName = c["company_name"]?.ToString() ?? "";
            data.CompanyAddress = c["address"]?.ToString() ?? "";
            data.CompanyPhone = c["phone"]?.ToString() ?? "";
            data.CompanyEmail = c["email"]?.ToString() ?? "";
        }

        var t = DbHelper.ExecuteQuery(@"
            SELECT st.transfer_no, st.transfer_date, st.remarks,
                   fw.warehouse_name AS from_name, tw.warehouse_name AS to_name
            FROM stock_transfer st
            LEFT JOIN warehouse_master fw ON fw.warehouse_id = st.from_warehouse_id
            LEFT JOIN warehouse_master tw ON tw.warehouse_id = st.to_warehouse_id
            WHERE st.transfer_id=@id", new Dictionary<string, object?> { ["id"] = transferId });
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];

        data.TransferNo = r["transfer_no"]?.ToString() ?? "";
        data.TransferDate = Convert.ToDateTime(r["transfer_date"]).ToString("dd/MM/yyyy");
        data.FromWarehouse = r["from_name"]?.ToString() ?? "";
        data.ToWarehouse = r["to_name"]?.ToString() ?? "";
        data.Remarks = r["remarks"]?.ToString() ?? "";

        data.Items = DbHelper.ExecuteQuery(@"
            SELECT i.item_name, i.model, u.uom_name, sti.qty
            FROM stock_transfer_item sti
            LEFT JOIN item_master i ON i.item_id = sti.item_id
            LEFT JOIN uom_master u ON u.uom_id = i.uom_id
            WHERE sti.transfer_id=@id ORDER BY sti.line_id", new Dictionary<string, object?> { ["id"] = transferId });
        data.TotalQty = data.Items.AsEnumerable().Sum(row => Convert.ToDecimal(row["qty"]));

        return data;
    }
}

/// <summary>The single drawing routine shared by the on-screen preview and the printer output.</summary>
public static class StockTransferDocumentRenderer
{
    public static void Draw(Graphics g, float scale, StockTransferDocumentData d)
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
        using var badgeBg = new SolidBrush(Color.DarkOrange);
        using var linePen = new Pen(Color.Black, 1);
        using var canvasBrush = new SolidBrush(Color.White);

        int docWidth = StockTransferDocumentData.DocWidth;
        int docHeight = StockTransferDocumentData.DocHeight;

        g.FillRectangle(canvasBrush, 0, 0, docWidth, docHeight);
        g.DrawRectangle(Pens.LightGray, 0, 0, docWidth, docHeight);

        float x = 30, y = 25;

        // Company header
        g.DrawString(d.CompanyName, titleFont, black, x, y); y += 28;
        g.DrawString(d.CompanyAddress, headerFont, gray, x, y); y += 16;
        g.DrawString($"Phone: {d.CompanyPhone}   Email: {d.CompanyEmail}", headerFont, gray, x, y);

        // Badge top-right
        string badgeText = "STOCK TRANSFER";
        var badgeSize = g.MeasureString(badgeText, badgeFont);
        float badgeW = badgeSize.Width + 24, badgeH = 26;
        g.FillRectangle(badgeBg, docWidth - 30 - badgeW, 25, badgeW, badgeH);
        g.DrawString(badgeText, badgeFont, white, docWidth - 30 - badgeW + 12, 25 + 5);

        // Doc info top-right, under badge
        float infoY = 25 + badgeH + 10;
        DrawRightAligned(g, $"Transfer No : {d.TransferNo}", headerFont, black, docWidth - 30, infoY); infoY += 16;
        DrawRightAligned(g, $"Date : {d.TransferDate}", headerFont, black, docWidth - 30, infoY);

        y += 45;
        g.DrawLine(linePen, x, y, docWidth - 30, y); y += 18;

        // Warehouses — the point of the document
        g.DrawString("From Warehouse :", boldFont, black, x, y); y += 16;
        g.DrawString(d.FromWarehouse, headerFont, black, x, y); y += 22;
        g.DrawString("To Warehouse :", boldFont, black, x, y); y += 16;
        g.DrawString(d.ToWarehouse, headerFont, black, x, y); y += 22;

        g.DrawLine(linePen, x, y, docWidth - 30, y); y += 14;

        // Item table header
        float[] colX = { x, x + 40, x + 260, x + 380, x + 440 };
        string[] headers = { "S.No", "Item Name", "Model", "UOM", "Qty" };
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
            y += 18;
            sno++;
        }

        y += 6;
        g.DrawLine(linePen, x, y, docWidth - 30, y);
        y += 12;

        g.DrawString($"Total Items : {d.Items.Rows.Count}   Total Qty : {d.TotalQty:N2}", boldFont, black, x, y);
        y += 24;

        if (!string.IsNullOrWhiteSpace(d.Remarks))
        {
            g.DrawString("Remarks :", boldFont, black, x, y); y += 16;
            g.DrawString(d.Remarks, headerFont, gray, x, y);
        }

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
