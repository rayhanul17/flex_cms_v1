namespace FlexCms.Host.Models;

public class SearchResultsViewModel
{
    public string Query { get; set; } = "";
    public List<SearchResultItem> Results { get; set; } = [];
}

public class SearchResultItem
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Excerpt { get; set; } = "";
}
