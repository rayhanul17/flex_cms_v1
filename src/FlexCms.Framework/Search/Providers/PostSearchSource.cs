using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Search.Providers;

/// <summary>Default LIKE-based search source for <see cref="FcmsPost"/>.</summary>
public sealed class PostSearchSource : IFcmsSearchableSource
{
    private readonly IRepository<FcmsPost> _posts;
    public PostSearchSource(IRepository<FcmsPost> posts) => _posts = posts;

    public string SourceId => "post";
    public string DisplayName => "Posts";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int max, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var q = query.Trim();

        var rows = await _posts.FindAsync(p =>
            p.IsPublished &&
            (p.Title.Contains(q) || p.Content.Contains(q) ||
             (p.Excerpt != null && p.Excerpt.Contains(q))), ct);

        return rows
            .Take(max)
            .Select(p => new SearchHit(
                Title: p.Title,
                Url: $"/blog/{p.Slug}",
                SourceId: SourceId,
                Snippet: Excerpt(p.Excerpt ?? p.Content, q),
                Score: p.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ? 2.0 : 1.0))
            .ToList();
    }

    private static string? Excerpt(string content, string query)
    {
        if (string.IsNullOrEmpty(content)) return null;
        var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return content.Length <= 160 ? content : content[..160] + "…";
        var start = Math.Max(0, idx - 60);
        var end = Math.Min(content.Length, idx + query.Length + 100);
        return (start > 0 ? "…" : "") + content[start..end] + (end < content.Length ? "…" : "");
    }
}
