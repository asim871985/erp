using System.Drawing.Printing;

namespace ErpApp.Forms;

/// <summary>
/// Matches mockup forms 11 ("Purchase Invoice") and 12 ("Sales Invoice") — a letterhead-style
/// printable document, single-invoice preview + print. For printing several invoices in one
/// job, see <see cref="BatchInvoicePrinter"/>.
/// </summary>
public class InvoicePrintForm : AppFormBase
{
    private readonly Panel previewPanel = new() { AutoScroll = true, BackColor = Color.Gainsboro };
    private readonly PrintDocument printDocument = new();
    private InvoiceDocumentData? data;

    public InvoicePrintForm(int documentId, bool isPurchase)
        : this(documentId, isPurchase ? PrintDocType.PurchaseBill : PrintDocType.SalesInvoice)
    {
    }

    public InvoicePrintForm(int documentId, PrintDocType docType)
    {
        Text = InvoiceDocumentData.BadgeText(docType);
        Width = 850;
        Height = 750;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();

        try
        {
            data = InvoiceDocumentData.Load(documentId, docType);
            if (data == null) MessageBox.Show("That document no longer exists.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load document for printing: " + ex.Message);
        }

        previewPanel.Invalidate();
    }

    private void BuildLayout()
    {
        previewPanel.Dock = DockStyle.Fill;
        previewPanel.Paint += (s, e) => { if (data != null) InvoiceDocumentRenderer.Draw(e.Graphics, 1f, data); };
        previewPanel.AutoScrollMinSize = new Size(InvoiceDocumentData.DocWidth + 20, InvoiceDocumentData.DocHeight + 20);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(10) };
        var btnPrint = new Button { Text = "Print", Width = 90 };
        var btnClose = new Button { Text = "Close", Width = 90 };
        btnPrint.Click += BtnPrint_Click;
        btnClose.Click += (s, e) => Close();
        btnPanel.Controls.Add(btnPrint);
        btnPanel.Controls.Add(btnClose);

        printDocument.PrintPage += PrintDocument_PrintPage;

        Controls.Add(previewPanel);
        Controls.Add(btnPanel);
    }

    private void PrintDocument_PrintPage(object? sender, PrintPageEventArgs e)
    {
        if (e.Graphics == null || data == null) return;
        float scale = Math.Min(
            e.MarginBounds.Width / (float)InvoiceDocumentData.DocWidth,
            e.MarginBounds.Height / (float)InvoiceDocumentData.DocHeight);
        e.Graphics.TranslateTransform(e.MarginBounds.Left, e.MarginBounds.Top);
        InvoiceDocumentRenderer.Draw(e.Graphics, scale, data);
        e.HasMorePages = false;
    }

    private void BtnPrint_Click(object? sender, EventArgs e)
    {
        if (data == null) { MessageBox.Show("Nothing loaded to print."); return; }
        using var dlg = new PrintDialog { Document = printDocument };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try { printDocument.Print(); }
            catch (Exception ex) { MessageBox.Show("Print failed: " + ex.Message); }
        }
    }
}
