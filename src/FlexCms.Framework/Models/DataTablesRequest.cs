namespace FlexCms.Framework.Models;

/// <summary>
/// Server-side jQuery DataTables request payload (subset — only the fields we use).
/// DataTables sends form-encoded fields like <c>order[0][column]</c>; ASP.NET Core
/// model binding maps them via the property names below. The <c>Columns</c> array
/// is collected only when needed for column-specific search.
/// </summary>
public class DataTablesRequest
{
    /// <summary>Sequence counter to detect out-of-order responses; echoed back unchanged.</summary>
    public int Draw { get; set; }

    public int Start { get; set; }
    public int Length { get; set; } = 25;

    public DataTablesSearch Search { get; set; } = new();
    public List<DataTablesOrder> Order { get; set; } = new();
    public List<DataTablesColumn> Columns { get; set; } = new();

    /// <summary>Convenience — global search value (or empty).</summary>
    public string SearchValue => Search?.Value ?? "";

    /// <summary>Convenience — primary order column index (or 0 if none).</summary>
    public int OrderColumnIndex => Order is { Count: > 0 } ? Order[0].Column : 0;

    /// <summary>Convenience — primary order direction "asc" / "desc".</summary>
    public string OrderDir => Order is { Count: > 0 } ? Order[0].Dir : "asc";

    public bool IsDescending => OrderDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

    public int Page => Length > 0 ? (Start / Length) + 1 : 1;
}

public class DataTablesSearch
{
    public string Value { get; set; } = "";
    public bool Regex { get; set; }
}

public class DataTablesOrder
{
    public int Column { get; set; }
    public string Dir { get; set; } = "asc";
}

public class DataTablesColumn
{
    public string Data { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Searchable { get; set; } = true;
    public bool Orderable { get; set; } = true;
    public DataTablesSearch Search { get; set; } = new();
}
