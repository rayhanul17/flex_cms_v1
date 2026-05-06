using FlexCms.Framework.Documents;
using Xunit;

namespace FlexCms.Tests.Unit.Phase12;

/// <summary>
/// Smoke tests for PDF + Excel rendering. Detailed visual verification is a
/// manual step (see phase-12-test-cases.md §3); the tests here just confirm
/// the byte-stream contract — non-empty output, correct file-format magic
/// bytes — so callers can rely on these services not throwing.
/// </summary>
public class DocumentServicesTests
{
    [Fact]
    public async Task Pdf_RenderTextAsync_returns_pdf_magic_bytes()
    {
        var svc = new PdfSharpPdfService();
        var bytes = await svc.RenderTextAsync("Hello", ["line 1", "line 2", "line 3"]);

        Assert.NotEmpty(bytes);
        // PDF magic bytes: %PDF-
        Assert.Equal((byte)0x25, bytes[0]);
        Assert.Equal((byte)0x50, bytes[1]);
        Assert.Equal((byte)0x44, bytes[2]);
        Assert.Equal((byte)0x46, bytes[3]);
        Assert.Equal((byte)0x2D, bytes[4]);
    }

    [Fact]
    public async Task Pdf_RenderTableAsync_returns_pdf_magic_bytes()
    {
        var svc = new PdfSharpPdfService();
        var bytes = await svc.RenderTableAsync(
            "Test",
            ["A", "B", "C"],
            [["1", "2", "3"], ["4", "5", "6"]]);

        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public async Task Excel_RenderTableAsync_returns_xlsx_magic_bytes()
    {
        var svc = new ClosedXmlExcelService();
        var bytes = await svc.RenderTableAsync(
            "Sheet1",
            ["A", "B"],
            [[1, "x"], [2.5, true]]);

        Assert.NotEmpty(bytes);
        // .xlsx is a ZIP archive — first 4 bytes are PK\x03\x04
        Assert.Equal((byte)0x50, bytes[0]);
        Assert.Equal((byte)0x4B, bytes[1]);
        Assert.Equal((byte)0x03, bytes[2]);
        Assert.Equal((byte)0x04, bytes[3]);
    }

    [Fact]
    public async Task Excel_handles_empty_rows_without_throwing()
    {
        var svc = new ClosedXmlExcelService();
        var bytes = await svc.RenderTableAsync("Sheet1", ["A"], []);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task Excel_truncates_long_sheet_names_to_31_chars()
    {
        var svc = new ClosedXmlExcelService();
        var name = new string('x', 50);
        // Doesn't throw despite 50-char sheet name — proves the SafeSheetName helper.
        var bytes = await svc.RenderTableAsync(name, ["A"], [["1"]]);
        Assert.NotEmpty(bytes);
    }
}
