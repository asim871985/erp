using System.Drawing.Printing;

namespace ErpApp.Forms;

/// <summary>Single Stock Transfer note preview + print (letterhead, From/To warehouses, item table).</summary>
public class StockTransferPrintForm : AppFormBase
{
    private readonly Panel previewPanel = new() { AutoScroll = true, BackColor = Color.Gainsboro };
    private readonly PrintDocument printDocument = new();
    private StockTransferDocumentData? data;

    public StockTransferPrintForm(int transferId)
    {
        Text = "Stock Transfer";
        Width = 800;
        Height = 750;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();

        try
        {
            data = StockTransferDocumentData.Load(transferId);
            if (data == null) MessageBox.Show("That transfer no longer exists.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load transfer for printing: " + ex.Message);
        }

        previewPanel.Invalidate();
    }

    private void BuildLayout()
    {
        previewPanel.Dock = DockStyle.Fill;
        previewPanel.Paint += (s, e) => { if (data != null) StockTransferDocumentRenderer.Draw(e.Graphics, 1f, data); };
        previewPanel.AutoScrollMinSize = new Size(StockTransferDocumentData.DocWidth + 20, StockTransferDocumentData.DocHeight + 20);

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
            e.MarginBounds.Width / (float)StockTransferDocumentData.DocWidth,
            e.MarginBounds.Height / (float)StockTransferDocumentData.DocHeight);
        e.Graphics.TranslateTransform(e.MarginBounds.Left, e.MarginBounds.Top);
        StockTransferDocumentRenderer.Draw(e.Graphics, scale, data);
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
