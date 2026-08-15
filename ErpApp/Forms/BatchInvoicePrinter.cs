using System.Drawing.Printing;

namespace ErpApp.Forms;

/// <summary>
/// Prints (or previews) several Sales Invoices / Purchase Bills as one print job — one
/// document per page, in the order given. Used by the "Print Selected" buttons on the
/// invoice list screens when more than one row is checked.
/// </summary>
public static class BatchInvoicePrinter
{
    public static void PrintOrPreview(IWin32Window owner, List<int> documentIds, bool isPurchase) =>
        PrintOrPreview(owner, documentIds, isPurchase ? PrintDocType.PurchaseBill : PrintDocType.SalesInvoice);

    public static void PrintOrPreview(IWin32Window owner, List<int> documentIds, PrintDocType docType)
    {
        if (documentIds.Count == 0)
        {
            MessageBox.Show("Select at least one document.");
            return;
        }

        var docs = new List<InvoiceDocumentData>();
        foreach (var id in documentIds)
        {
            var d = InvoiceDocumentData.Load(id, docType);
            if (d != null) docs.Add(d);
        }

        if (docs.Count == 0)
        {
            MessageBox.Show("None of the selected documents could be loaded.");
            return;
        }

        int pageIndex = 0;
        var printDocument = new PrintDocument();
        printDocument.PrintPage += (s, e) =>
        {
            if (e.Graphics == null) return;
            var doc = docs[pageIndex];
            float scale = Math.Min(
                e.MarginBounds.Width / (float)InvoiceDocumentData.DocWidth,
                e.MarginBounds.Height / (float)InvoiceDocumentData.DocHeight);
            e.Graphics.TranslateTransform(e.MarginBounds.Left, e.MarginBounds.Top);
            InvoiceDocumentRenderer.Draw(e.Graphics, scale, doc);

            pageIndex++;
            e.HasMorePages = pageIndex < docs.Count;
        };

        using var dlg = new PrintDialog { Document = printDocument, AllowSomePages = false };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;

        try
        {
            pageIndex = 0;
            printDocument.Print();
            MessageBox.Show($"Sent {docs.Count} document(s) to the printer.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Print failed: " + ex.Message);
        }
    }
}
