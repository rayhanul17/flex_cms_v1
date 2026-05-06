namespace FlexCms.Framework.Documents;

/// <summary>
/// Generate PDF byte arrays from simple text-content models. The default
/// implementation (<see cref="PdfSharpPdfService"/>) uses PdfSharpCore (MIT)
/// — pure managed code, no native deps. For complex layouts a module can
/// register its own implementation against this interface.
/// </summary>
public interface IFcmsPdfService
{
    /// <summary>Render a single A4 page from <paramref name="title"/> + <paramref name="lines"/> body text.</summary>
    Task<byte[]> RenderTextAsync(string title, IEnumerable<string> lines, CancellationToken ct = default);

    /// <summary>
    /// Render an HTML-style table (header row + data rows). Returns the PDF
    /// as a byte array. Cell text is plain string — no HTML/CSS rendering
    /// (use a module-supplied implementation if you need that).
    /// </summary>
    Task<byte[]> RenderTableAsync(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, CancellationToken ct = default);
}
