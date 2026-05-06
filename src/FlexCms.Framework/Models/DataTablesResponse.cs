namespace FlexCms.Framework.Models;

/// <summary>
/// Server-side jQuery DataTables response payload. Echoes <c>draw</c> back to
/// the client + carries a single <c>permissions</c> object (evaluated once for
/// the current user — JS uses these flags to render action buttons).
/// </summary>
public class DataTablesResponse<T>
{
    public int Draw { get; set; }
    public int RecordsTotal { get; set; }
    public int RecordsFiltered { get; set; }
    public List<T> Data { get; set; } = new();

    /// <summary>
    /// Permission flags evaluated server-side once per request. JS uses these
    /// to decide which action buttons to render per row (no key strings
    /// shipped to the client).
    /// </summary>
    public Dictionary<string, bool> Permissions { get; set; } = new();
}
