using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace FlexCms.Framework.Documents;

/// <summary>
/// PdfSharpCore-based PDF generator. Now multi-page: when content overflows
/// a page, a fresh A4 page is appended and rendering continues with a
/// re-drawn header (table mode) or just the running body (text mode).
/// </summary>
public sealed class PdfSharpPdfService : IFcmsPdfService
{
    // PdfSharpCore measures everything in points (1/72 in). A4 = 595.28 × 841.89.
    private const double Margin = 40;
    private const double TitleFontSize = 16;
    private const double BodyFontSize = 11;
    private const double LineHeight = 16;

    public Task<byte[]> RenderTextAsync(string title, IEnumerable<string> lines, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var doc = new PdfDocument();
            var titleFont = new XFont("Arial", TitleFontSize, XFontStyle.Bold);
            var bodyFont = new XFont("Arial", BodyFontSize, XFontStyle.Regular);

            var page = doc.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            try
            {
                double y = Margin;
                gfx.DrawString(title ?? "", titleFont, XBrushes.Black,
                    new XRect(Margin, y, page.Width - 2 * Margin, LineHeight),
                    XStringFormats.TopLeft);
                y += LineHeight + 8;

                foreach (var line in lines ?? [])
                {
                    if (y > page.Height - Margin)
                    {
                        // Roll over to a new page. Dispose the existing graphics
                        // context first — PdfSharpCore needs the writer flushed
                        // before another page can be added.
                        gfx.Dispose();
                        page = doc.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        y = Margin;
                    }
                    gfx.DrawString(line ?? "", bodyFont, XBrushes.Black,
                        new XRect(Margin, y, page.Width - 2 * Margin, LineHeight),
                        XStringFormats.TopLeft);
                    y += LineHeight;
                }
            }
            finally { gfx.Dispose(); }

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
            var titleFont = new XFont("Arial", TitleFontSize, XFontStyle.Bold);
            var headerFont = new XFont("Arial", BodyFontSize, XFontStyle.Bold);
            var bodyFont = new XFont("Arial", BodyFontSize, XFontStyle.Regular);

            var columnCount = Math.Max(1, headers?.Count ?? 0);
            var page = doc.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            double y;
            double availableWidth;
            double columnWidth;

            void StartPage(bool first)
            {
                y = Margin;
                if (first)
                {
                    gfx.DrawString(title ?? "", titleFont, XBrushes.Black,
                        new XRect(Margin, y, page.Width - 2 * Margin, LineHeight),
                        XStringFormats.TopLeft);
                    y += LineHeight + 8;
                }
                availableWidth = page.Width - 2 * Margin;
                columnWidth = availableWidth / columnCount;
                // Re-draw the header row at the top of each new page so the
                // reader doesn't lose context across page breaks.
                for (int c = 0; c < columnCount; c++)
                {
                    gfx.DrawString(headers![c] ?? "", headerFont, XBrushes.Black,
                        new XRect(Margin + c * columnWidth, y, columnWidth, LineHeight),
                        XStringFormats.TopLeft);
                }
                y += LineHeight;
                gfx.DrawLine(XPens.Black, Margin, y, Margin + availableWidth, y);
                y += 2;
            }

            try
            {
                StartPage(first: true);

                foreach (var row in rows ?? [])
                {
                    if (y > page.Height - Margin)
                    {
                        gfx.Dispose();
                        page = doc.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        StartPage(first: false);
                    }
                    for (int c = 0; c < columnCount && c < row.Count; c++)
                    {
                        gfx.DrawString(row[c] ?? "", bodyFont, XBrushes.Black,
                            new XRect(Margin + c * columnWidth, y, columnWidth, LineHeight),
                            XStringFormats.TopLeft);
                    }
                    y += LineHeight;
                }
            }
            finally { gfx.Dispose(); }

            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }, ct);
    }
}
