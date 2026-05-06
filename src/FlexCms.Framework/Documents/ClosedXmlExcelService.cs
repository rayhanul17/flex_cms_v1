using ClosedXML.Excel;

namespace FlexCms.Framework.Documents;

public sealed class ClosedXmlExcelService : IFcmsExcelService
{
    public Task<byte[]> RenderTableAsync(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add(string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : SafeSheetName(sheetName));

            // Header row — bold + light grey background so the export still
            // looks readable in Excel/LibreOffice without any styling work
            // on the caller's part.
            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c] ?? "";
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            }

            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    var cell = ws.Cell(r + 2, c + 1);
                    cell.Value = ToXLValue(row[c]);
                }
            }

            // Auto-size — bounded so a stray 10MB cell doesn't blow out the column width.
            ws.Columns().AdjustToContents(1, 80);

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }, ct);
    }

    private static XLCellValue ToXLValue(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b,
        DateTime dt => dt,
        DateTimeOffset dto => dto.DateTime,
        decimal d => d,
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        _ => v.ToString() ?? ""
    };

    /// <summary>Excel sheet names cap at 31 chars and disallow <c>:\/?*[]</c>.</summary>
    private static string SafeSheetName(string name)
    {
        var clean = new string(name.Where(c => !"\\/?*[]:".Contains(c)).ToArray());
        return clean.Length > 31 ? clean[..31] : clean;
    }
}
