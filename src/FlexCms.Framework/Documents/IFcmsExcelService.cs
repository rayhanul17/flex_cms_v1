namespace FlexCms.Framework.Documents;

/// <summary>
/// Generate Excel byte arrays from headers + data rows AND parse uploaded
/// workbooks into rows / DTOs. The default implementation
/// (<see cref="ClosedXmlExcelService"/>) uses ClosedXML — pure managed code.
/// Use a module-supplied impl for advanced layouts (charts, formulas, styles).
/// </summary>
public interface IFcmsExcelService
{
    /// <summary>
    /// Render a single worksheet with a styled header row + raw data rows.
    /// Cell types: numeric strings auto-convert; everything else stays text.
    /// </summary>
    Task<byte[]> RenderTableAsync(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, CancellationToken ct = default);

    /// <summary>
    /// Parse an uploaded workbook into header-keyed rows. The first non-empty
    /// row is treated as the header row; every subsequent row becomes a
    /// dictionary keyed by header name (case-insensitive). Empty rows are
    /// skipped, blank cells become empty strings.
    /// </summary>
    /// <param name="stream">The uploaded workbook. Caller owns the stream's lifetime.</param>
    /// <param name="sheetName">
    /// Optional sheet name. When null/empty, the first worksheet is used.
    /// </param>
    Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ParseTableAsync(
        Stream stream,
        string? sheetName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Strongly-typed variant of <see cref="ParseTableAsync"/>. Each row maps
    /// onto a fresh <typeparamref name="T"/> instance using property names
    /// (case-insensitive); <c>[FcmsExcelColumn("Custom Header")]</c> on a
    /// property overrides the header lookup.
    ///
    /// <para>
    /// Conversion rules: <see cref="string"/> passes through;
    /// <see cref="int"/> / <see cref="long"/> / <see cref="decimal"/> /
    /// <see cref="double"/> / <see cref="bool"/> / <see cref="Guid"/> /
    /// <see cref="DateTime"/> + their nullable counterparts use invariant
    /// culture; enums accept name or integer; unknown types are skipped.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<T>> ParseAsync<T>(
        Stream stream,
        string? sheetName = null,
        CancellationToken ct = default) where T : class, new();
}

/// <summary>
/// Optional column-name override for <see cref="IFcmsExcelService.ParseAsync{T}"/>.
/// Use when the spreadsheet header doesn't match the DTO property name.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class FcmsExcelColumnAttribute : Attribute
{
    public string HeaderName { get; }
    public FcmsExcelColumnAttribute(string headerName) => HeaderName = headerName;
}
