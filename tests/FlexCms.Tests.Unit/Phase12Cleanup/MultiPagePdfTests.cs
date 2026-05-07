using System.Text;
using System.Text.RegularExpressions;
using FlexCms.Framework.Documents;
using Xunit;

namespace FlexCms.Tests.Unit.Phase12Cleanup;

/// <summary>
/// Verifies the multi-page upgrade in <see cref="PdfSharpPdfService"/> —
/// content that previously got truncated on page 1 now spills onto
/// additional pages. Counts pages by scanning the PDF byte stream for
/// page-object markers (<c>/Type /Page</c>) — PDF spec § 7.7.3 names
/// every leaf page node with that key, and the regex's word boundary
/// after <c>Page</c> excludes the <c>/Pages</c> tree root that names
/// the catalog node.
/// </summary>
public class MultiPagePdfTests
{
    private readonly PdfSharpPdfService _svc = new();

    private static readonly Regex PageObjectRegex = new(
        @"/Type\s*/Page\b(?!s)",
        RegexOptions.Compiled);

    private static int CountPdfPages(byte[] pdfBytes)
    {
        // Latin-1 so 0x00–0xFF round-trip without re-encoding interpretation.
        var text = Encoding.Latin1.GetString(pdfBytes);
        return PageObjectRegex.Matches(text).Count;
    }

    private static bool IsValidPdf(byte[] pdfBytes)
        => pdfBytes.Length > 8
           && Encoding.ASCII.GetString(pdfBytes, 0, 5) == "%PDF-";

    [Fact]
    public async Task RenderTextAsync_short_input_produces_single_page()
    {
        var bytes = await _svc.RenderTextAsync("Title", ["one", "two", "three"]);

        Assert.True(IsValidPdf(bytes));
        Assert.Equal(1, CountPdfPages(bytes));
    }

    [Fact]
    public async Task RenderTextAsync_overflowing_content_creates_multiple_pages()
    {
        // 200 body lines >> single A4 page (~50 lines per page at 11pt).
        var lines = Enumerable.Range(1, 200).Select(i => $"Line {i}: lorem ipsum dolor sit amet.");

        var bytes = await _svc.RenderTextAsync("Long doc", lines);

        Assert.True(IsValidPdf(bytes));
        var pageCount = CountPdfPages(bytes);
        Assert.True(pageCount >= 2,
            $"Expected at least 2 pages for 200 lines but got {pageCount}.");
    }

    [Fact]
    public async Task RenderTableAsync_overflowing_rows_creates_multiple_pages()
    {
        var headers = new[] { "ID", "Name", "Email" };
        var rows = Enumerable.Range(1, 200)
            .Select(i => (IReadOnlyList<string>)new[] { i.ToString(), $"User {i}", $"user{i}@example.com" })
            .ToList();

        var bytes = await _svc.RenderTableAsync("Users", headers, rows);

        Assert.True(IsValidPdf(bytes));
        var pageCount = CountPdfPages(bytes);
        Assert.True(pageCount >= 2,
            $"Expected at least 2 pages for 200 rows but got {pageCount}.");
    }

    [Fact]
    public async Task RenderTableAsync_empty_rows_still_produces_valid_single_page()
    {
        var bytes = await _svc.RenderTableAsync("Empty", ["A", "B"], []);

        Assert.True(IsValidPdf(bytes));
        Assert.Equal(1, CountPdfPages(bytes));
    }
}
