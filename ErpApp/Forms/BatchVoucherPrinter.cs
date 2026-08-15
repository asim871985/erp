using System.Drawing.Printing;

namespace ErpApp.Forms;

/// <summary>Prints (or previews) several Receipt/Payment vouchers as one job — one voucher per page.</summary>
public static class BatchVoucherPrinter
{
    public static void PrintOrPreview(IWin32Window owner, List<int> ids, VoucherType type)
    {
        if (ids.Count == 0)
        {
            MessageBox.Show("Select at least one voucher.");
            return;
        }

        var docs = new List<VoucherDocumentData>();
        foreach (var id in ids)
        {
            var d = VoucherDocumentData.Load(id, type);
            if (d != null) docs.Add(d);
        }

        if (docs.Count == 0)
        {
            MessageBox.Show("None of the selected vouchers could be loaded.");
            return;
        }

        int pageIndex = 0;
        var printDocument = new PrintDocument();
        printDocument.PrintPage += (s, e) =>
        {
            if (e.Graphics == null) return;
            var doc = docs[pageIndex];
            float scale = Math.Min(
                e.MarginBounds.Width / (float)VoucherDocumentData.DocWidth,
                e.MarginBounds.Height / (float)VoucherDocumentData.DocHeight);
            e.Graphics.TranslateTransform(e.MarginBounds.Left, e.MarginBounds.Top);
            VoucherDocumentRenderer.Draw(e.Graphics, scale, doc);

            pageIndex++;
            e.HasMorePages = pageIndex < docs.Count;
        };

        using var dlg = new PrintDialog { Document = printDocument, AllowSomePages = false };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;

        try
        {
            pageIndex = 0;
            printDocument.Print();
            MessageBox.Show($"Sent {docs.Count} voucher(s) to the printer.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Print failed: " + ex.Message);
        }
    }
}
