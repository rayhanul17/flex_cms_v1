using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FlexCms.Framework.Documents;

/// <summary>
/// QuestPDF-backed PDF generator. Class name kept as <c>PdfSharpPdfService</c>
/// to avoid breaking callers / DI registration / module manifests, but the
/// underlying engine is now QuestPDF (MIT-compatible Community licence). The
/// switch dropped a 7-CVE-bearing transitive (SixLabors.ImageSharp 1.0.4 via
/// PdfSharpCore) and gives us native multi-page + automatic header reflow.
///
/// <para>
/// QuestPDF requires a one-time licence acknowledgement before any document
/// is rendered. We set it lazily on first use so the framework doesn't need
/// custom startup wiring; the static guard is idempotent.
/// </para>
/// </summary>
public sealed class PdfSharpPdfService : IFcmsPdfService
{
    private static int _licenceConfigured;

    static PdfSharpPdfService() => EnsureLicence();

    private static void EnsureLicence()
    {
        if (Interlocked.Exchange(ref _licenceConfigured, 1) == 0)
        {
            // Community licence: free for individuals + companies under the
            // QuestPDF revenue threshold + open-source projects. Downstream
            // commercial users above the threshold must set their own licence
            // via app startup before this class is touched.
            QuestPDF.Settings.License = LicenseType.Community;
        }
    }

    public Task<byte[]> RenderTextAsync(string title, IEnumerable<string> lines, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var bodyLines = lines?.ToList() ?? [];
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(11));

                    page.Header().Text(title ?? "").FontSize(16).Bold();
                    page.Content().PaddingVertical(8).Column(col =>
                    {
                        foreach (var line in bodyLines)
                            col.Item().Text(line ?? "");
                    });
                });
            });
            return doc.GeneratePdf();
        }, ct);
    }

    public Task<byte[]> RenderTableAsync(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var headerList = headers ?? Array.Empty<string>();
            var rowList = rows ?? Array.Empty<IReadOnlyList<string>>();
            var columnCount = Math.Max(1, headerList.Count);

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(11));

                    page.Header().Text(title ?? "").FontSize(16).Bold();
                    page.Content().PaddingVertical(8).Table(table =>
                    {
                        // Equal-width columns (refinement = future work; matches the
                        // PdfSharp version's behaviour).
                        table.ColumnsDefinition(cols =>
                        {
                            for (int c = 0; c < columnCount; c++)
                                cols.RelativeColumn();
                        });

                        // Header row repeats automatically on every page so readers
                        // don't lose context across page breaks.
                        table.Header(header =>
                        {
                            for (int c = 0; c < columnCount; c++)
                                header.Cell().BorderBottom(1).PaddingVertical(4)
                                    .Text(headerList[c] ?? "").Bold();
                        });

                        foreach (var row in rowList)
                        {
                            for (int c = 0; c < columnCount; c++)
                            {
                                var value = c < row.Count ? row[c] ?? "" : "";
                                table.Cell().PaddingVertical(2).Text(value);
                            }
                        }
                    });
                });
            });
            return doc.GeneratePdf();
        }, ct);
    }
}
