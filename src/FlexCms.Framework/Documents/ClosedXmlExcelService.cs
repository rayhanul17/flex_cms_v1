using System.Globalization;
using System.Reflection;
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

    public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ParseTableAsync(
        Stream stream,
        string? sheetName = null,
        CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<IReadOnlyDictionary<string, string>>>(() =>
        {
            using var workbook = new XLWorkbook(stream);
            var ws = string.IsNullOrWhiteSpace(sheetName)
                ? workbook.Worksheets.First()
                : workbook.Worksheets.Worksheet(sheetName);

            var range = ws.RangeUsed();
            var result = new List<IReadOnlyDictionary<string, string>>();
            if (range is null) return result;

            // Headers: first row, trimmed, blank columns dropped.
            var headerCells = range.Row(1).Cells().ToList();
            var headers = headerCells
                .Select(c => (c.GetString() ?? "").Trim())
                .ToList();

            for (int r = 2; r <= range.RowCount(); r++)
            {
                ct.ThrowIfCancellationRequested();
                var rowRange = range.Row(r);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool anyNonEmpty = false;
                for (int c = 1; c <= headers.Count; c++)
                {
                    var header = headers[c - 1];
                    if (string.IsNullOrEmpty(header)) continue;
                    var value = rowRange.Cell(c).GetString() ?? "";
                    if (value.Length > 0) anyNonEmpty = true;
                    dict[header] = value;
                }
                if (anyNonEmpty) result.Add(dict);
            }

            return result;
        }, ct);
    }

    public async Task<IReadOnlyList<T>> ParseAsync<T>(
        Stream stream,
        string? sheetName = null,
        CancellationToken ct = default) where T : class, new()
    {
        var rows = await ParseTableAsync(stream, sheetName, ct);
        if (rows.Count == 0) return Array.Empty<T>();

        // Build a header→property map once.
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        var headerToProp = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            var attr = p.GetCustomAttribute<FcmsExcelColumnAttribute>();
            var header = attr?.HeaderName ?? p.Name;
            headerToProp[header] = p;
        }

        var output = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            var item = new T();
            foreach (var (header, raw) in row)
            {
                if (!headerToProp.TryGetValue(header, out var prop)) continue;
                var converted = ConvertValue(raw, prop.PropertyType);
                if (converted is not null || IsNullableType(prop.PropertyType))
                    prop.SetValue(item, converted);
            }
            output.Add(item);
        }

        return output;
    }

    // ── value conversion ──────────────────────────────────────────────────

    private static bool IsNullableType(Type t)
        => !t.IsValueType || Nullable.GetUnderlyingType(t) is not null;

    private static object? ConvertValue(string raw, Type targetType)
    {
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (string.IsNullOrWhiteSpace(raw))
            return target == typeof(string) ? "" : null;

        var s = raw.Trim();

        if (target == typeof(string)) return s;
        if (target == typeof(int)    && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        if (target == typeof(long)   && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        if (target == typeof(decimal) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec)) return dec;
        if (target == typeof(double) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        if (target == typeof(bool))
        {
            var t = s.ToLowerInvariant();
            if (t is "true" or "1" or "yes" or "y") return true;
            if (t is "false" or "0" or "no" or "n") return false;
        }
        if (target == typeof(Guid) && Guid.TryParse(s, out var g)) return g;
        if (target == typeof(DateTime) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return dt;
        if (target.IsEnum)
        {
            if (Enum.TryParse(target, s, ignoreCase: true, out var ev)) return ev;
        }

        return null;
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
