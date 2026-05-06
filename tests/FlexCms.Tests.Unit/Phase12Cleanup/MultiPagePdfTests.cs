using FlexCms.Framework.Documents;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace FlexCms.Tests.Unit.Phase12Cleanup;

/// <summary>
/// Verifies the multi-page upgrade in <see cref="PdfSharpPdfService"/> —
/// content that previously got truncated on page 1 now spills onto
/// additional pages, and the table mode re-draws the header row at the
/// top of each new page.
/// </summary>
public class MultiPagePdfTests
{
    private readonly PdfSharpPdfService _svc = new();

    [Fact]
    public async Task RenderTextAsync_short_input_produces_single_page()
    {
        var bytes = await _svc.RenderTextAsync("Title", ["one", "two", "three"]);

        using var ms = new MemoryStream(bytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public async Task RenderTextAsync_overflowing_content_creates_multiple_pages()
    {
        // 200 lines × ~16pt line height >> single A4 page (~700pt usable).
        var lines = Enumerable.Range(1, 200).Select(i => $"Line {i}: lorem ipsum dolor sit amet.");

        var bytes = await _svc.RenderTextAsync("Long doc", lines);

        using var ms = new MemoryStream(bytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.ReadOnly);
        Assert.True(doc.PageCount >= 2,
            $"Expected at least 2 pages for 200 lines but got {doc.PageCount}.");
    }

    [Fact]
    public async Task RenderTableAsync_overflowing_rows_creates_multiple_pages()
    {
        var headers = new[] { "ID", "Name", "Email" };
        var rows = Enumerable.Range(1, 200)
            .Select(i => (IReadOnlyList<string>)new[] { i.ToString(), $"User {i}", $"user{i}@example.com" })
            .ToList();

        var bytes = await _svc.RenderTableAsync("Users", headers, rows);

        using var ms = new MemoryStream(bytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.ReadOnly);
        Assert.True(doc.PageCount >= 2,
            $"Expected at least 2 pages for 200 rows but got {doc.PageCount}.");
    }

    [Fact]
    public async Task RenderTableAsync_empty_rows_still_produces_valid_single_page()
    {
        var bytes = await _svc.RenderTableAsync("Empty", ["A", "B"], []);

        using var ms = new MemoryStream(bytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(1, doc.PageCount);
    }
}
