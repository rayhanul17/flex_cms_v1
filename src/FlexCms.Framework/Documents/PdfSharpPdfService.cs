using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace FlexCms.Framework.Documents;

public sealed class PdfSharpPdfService : IFcmsPdfService
{
    // PdfSharpCore measures everything in points (1/72 in). A4 = 595.28 × 841.89.
    // Margin/font sizes are conservative defaults — modules with branding
    // requirements should ship their own IFcmsPdfService.
    private const double Margin = 40;
    private const double TitleFontSize = 16;
    private const double BodyFontSize = 11;
    private const double LineHeight = 16;

    public Task<byte[]> RenderTextAsync(string title, IEnumerable<string> lines, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);

            var titleFont = new XFont("Arial", TitleFontSize, XFontStyle.Bold);
            var bodyFont = new XFont("Arial", BodyFontSize, XFontStyle.Regular);

            double y = Margin;
            gfx.DrawString(title ?? "", titleFont, XBrushes.Black,
                new XRect(Margin, y, page.Width - 2 * Margin, LineHeight),
                XStringFormats.TopLeft);
            y += LineHeight + 8;

            foreach (var line in lines ?? [])
            {
                if (y > page.Height - Margin) break;   // overflow → first page only (simple text mode)
                gfx.DrawString(line ?? "", bodyFont, XBrushes.Black,
                    new XRect(Margin, y, page.Width - 2 * Margin, LineHeight),
                    XStringFormats.TopLeft);
                y += LineHeight;
            }

            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }, ct);
    }

    public Task<byte[]> RenderTableAsync(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);

            var titleFont = new XFont("Arial", TitleFontSize, XFontStyle.Bold);
            var headerFont = new XFont("Arial", BodyFontSize, XFontStyle.Bold);
            var bodyFont = new XFont("Arial", BodyFontSize, XFontStyle.Regular);

            double y = Margin;
            gfx.DrawString(title ?? "", titleFont, XBrushes.Black,
                new XRect(Margin, y, page.Width - 2 * Margin, LineHeight),
                XStringFormats.TopLeft);
            y += LineHeight + 8;

            // Equal-width columns — refinement (e.g. auto-fit) is out of scope here.
            var columnCount = Math.Max(1, headers?.Count ?? 0);
            var availableWidth = page.Width - 2 * Margin;
            var columnWidth = availableWidth / columnCount;

            // Header row
            for (int c = 0; c < columnCount; c++)
            {
                gfx.DrawString(headers![c] ?? "", headerFont, XBrushes.Black,
                    new XRect(Margin + c * columnWidth, y, columnWidth, LineHeight),
                    XStringFormats.TopLeft);
            }
            y += LineHeight;
            gfx.DrawLine(XPens.Black, Margin, y, Margin + availableWidth, y);
            y += 2;

            // Data rows
            foreach (var row in rows ?? [])
            {
                if (y > page.Height - Margin) break;   // single-page output for v1
                for (int c = 0; c < columnCount && c < row.Count; c++)
                {
                    gfx.DrawString(row[c] ?? "", bodyFont, XBrushes.Black,
                        new XRect(Margin + c * columnWidth, y, columnWidth, LineHeight),
                        XStringFormats.TopLeft);
                }
                y += LineHeight;
            }

            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }, ct);
    }
}
