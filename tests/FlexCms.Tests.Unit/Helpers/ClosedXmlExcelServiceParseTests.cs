using ClosedXML.Excel;
using FlexCms.Framework.Documents;

namespace FlexCms.Tests.Unit.Helpers;

public class ClosedXmlExcelServiceParseTests
{
    private readonly ClosedXmlExcelService _excel = new();

    private static MemoryStream BuildWorkbook(params (string header, string[] cells)[] columns)
    {
        // Helper that writes a workbook column-by-column. Each tuple is (header, rows).
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");
        for (int c = 0; c < columns.Length; c++)
        {
            ws.Cell(1, c + 1).Value = columns[c].header;
            for (int r = 0; r < columns[c].cells.Length; r++)
                ws.Cell(r + 2, c + 1).Value = columns[c].cells[r];
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task ParseTableAsync_returns_each_row_keyed_by_header()
    {
        using var ms = BuildWorkbook(
            ("Name",   new[] { "Alice", "Bob" }),
            ("Amount", new[] { "100",   "200" }));

        var rows = await _excel.ParseTableAsync(ms);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0]["Name"]);
        Assert.Equal("100",   rows[0]["Amount"]);
        Assert.Equal("Bob",   rows[1]["Name"]);
        Assert.Equal("200",   rows[1]["Amount"]);
    }

    [Fact]
    public async Task ParseTableAsync_keys_are_case_insensitive()
    {
        using var ms = BuildWorkbook(("Name", new[] { "Alice" }));
        var rows = await _excel.ParseTableAsync(ms);
        Assert.Equal("Alice", rows[0]["NAME"]);
    }

    [Fact]
    public async Task ParseTableAsync_skips_fully_empty_rows()
    {
        using var ms = BuildWorkbook(
            ("Name", new[] { "Alice", "",      "Bob" }),
            ("Age",  new[] { "30",    "",      "40"  }));

        var rows = await _excel.ParseTableAsync(ms);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0]["Name"]);
        Assert.Equal("Bob",   rows[1]["Name"]);
    }

    public class Sample
    {
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
        public bool IsActive { get; set; }
    }

    [Fact]
    public async Task ParseAsync_maps_properties_by_header_name()
    {
        using var ms = BuildWorkbook(
            ("Name",     new[] { "Alice" }),
            ("Amount",   new[] { "150.50" }),
            ("IsActive", new[] { "true" }));

        var rows = await _excel.ParseAsync<Sample>(ms);

        var first = Assert.Single(rows);
        Assert.Equal("Alice", first.Name);
        Assert.Equal(150.50m, first.Amount);
        Assert.True(first.IsActive);
    }

    public class CustomHeader
    {
        [FcmsExcelColumn("Investor Name")] public string Name { get; set; } = "";
    }

    [Fact]
    public async Task ParseAsync_honours_FcmsExcelColumn_override()
    {
        using var ms = BuildWorkbook(("Investor Name", new[] { "Alice" }));
        var rows = await _excel.ParseAsync<CustomHeader>(ms);
        Assert.Equal("Alice", rows.Single().Name);
    }
}
