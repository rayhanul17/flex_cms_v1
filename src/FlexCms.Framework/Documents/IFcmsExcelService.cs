namespace FlexCms.Framework.Documents;

/// <summary>
/// Generate Excel byte arrays from headers + data rows. The default
/// implementation (<see cref="ClosedXmlExcelService"/>) uses ClosedXML — pure
/// managed code. Use a module-supplied impl for advanced layouts (charts,
/// formulas, styles).
/// </summary>
public interface IFcmsExcelService
{
    /// <summary>
    /// Render a single worksheet with a styled header row + raw data rows.
    /// Cell types: numeric strings auto-convert; everything else stays text.
    /// </summary>
    Task<byte[]> RenderTableAsync(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, CancellationToken ct = default);
}
