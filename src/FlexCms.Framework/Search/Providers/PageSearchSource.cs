using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Search.Providers;

/// <summary>
/// Default LIKE-based search source for <see cref="FcmsPage"/>. Cheap on
/// small corpora; for large catalogs, register a vendor-specific source
/// instead (FULLTEXT/tsvector/FTS) — same interface, vendor uses native
/// indexes.
/// </summary>
public sealed class PageSearchSource : IFcmsSearchableSource
{
    private readonly IRepository<FcmsPage> _pages;
    public PageSearchSource(IRepository<FcmsPage> pages) => _pages = pages;

    public string SourceId => "page";
    public string DisplayName => "Pages";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int max, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var q = query.Trim();

        // Title hits score higher than content hits — closer to user intent.
        var rows = await _pages.FindAsync(p =>
            p.IsPublished &&
            (p.Title.Contains(q) || p.Content.Contains(q) ||
             (p.MetaDescription != null && p.MetaDescription.Contains(q))), ct);

        return rows
            .Take(max)
            .Select(p => new SearchHit(
                Title: p.Title,
                Url: $"/{p.Slug}",
                SourceId: SourceId,
                Snippet: Excerpt(p.Content, q),
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
