namespace FlexCms.Framework.Db;

/// <summary>
/// Wraps a page of results with pagination metadata.
/// </summary>
public sealed class PagedResponse<T>
{
    public List<T> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public static PagedResponse<T> Create(List<T> items, int total, int page, int pageSize) =>
        new() { Items = items, Total = total, Page = page, PageSize = pageSize };
}
