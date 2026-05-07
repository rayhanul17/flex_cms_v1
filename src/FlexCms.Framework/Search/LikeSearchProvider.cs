using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Search;

/// <summary>
/// Default search provider — runs each registered <see cref="IFcmsSearchableSource"/>
/// in parallel + merges hits. Each source is responsible for its own
/// matching strategy (typically <c>LIKE '%query%'</c> over title / content).
///
/// <para>
/// Adequate for small corpora (under ~10k entities). For larger sites,
/// register a vendor-specific provider (FULLTEXT / tsvector / FTS / Mongo
/// text index) — same interface, different impl.
/// </para>
/// </summary>
public sealed class LikeSearchProvider : IFcmsSearchProvider
{
    private readonly IEnumerable<IFcmsSearchableSource> _sources;
    private readonly IFcmsSearchAnalytics _analytics;
    private readonly ILogger<LikeSearchProvider> _logger;

    public LikeSearchProvider(
        IEnumerable<IFcmsSearchableSource> sources,
        IFcmsSearchAnalytics analytics,
        ILogger<LikeSearchProvider> logger)
    {
        _sources = sources;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task<SearchResults> SearchAsync(string query, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchResults(query ?? "", [], 0, page, pageSize);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Fan-out: each source independently. One slow source shouldn't
        // hold up the others — failures degrade to "this source returned 0".
        var perSource = await Task.WhenAll(_sources.Select(async s =>
        {
            try { return await s.SearchAsync(query, max: page * pageSize + 1, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search source {Source} failed for query '{Query}'", s.SourceId, query);
                return (IReadOnlyList<SearchHit>)[];
            }
        }));

        var allHits = perSource.SelectMany(h => h)
            .OrderByDescending(h => h.Score)
            .ToList();
        var total = allHits.Count;

        // Track searches with zero results — admin "No-Result Queries"
        // panel uses this to find content gaps.
        try { await _analytics.RecordAsync(query, total, ct); }
        catch { /* analytics is best-effort */ }

        var skip = (page - 1) * pageSize;
        var paged = allHits.Skip(skip).Take(pageSize).ToList();
        return new SearchResults(query, paged, total, page, pageSize);
    }
}
