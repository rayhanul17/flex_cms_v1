using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Search.Providers;

/// <summary>
/// Postgres <c>tsvector</c>/<c>tsquery</c>-backed search source. Same
/// fallback semantics as <see cref="MySqlFullTextSearchSource"/>: requires
/// pre-created GIN indexes; otherwise logs a warning + returns empty.
///
/// <para>
/// <b>Admin setup</b> (one-time):
/// </para>
/// <code>
/// CREATE INDEX fcms_pages_search_idx ON fcms_pages
///   USING GIN (to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, '')));
/// CREATE INDEX fcms_posts_search_idx ON fcms_posts
///   USING GIN (to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, '')));
/// </code>
///
/// <para>
/// Use <c>english</c> (or <c>bengali</c> via the <c>tsearch_data</c>
/// extension) instead of <c>simple</c> if your corpus is monolingual and
/// you want stemming.
/// </para>
/// </summary>
public sealed class PostgresFullTextSearchSource : IFcmsSearchableSource
{
    private readonly FcmsDbContext _db;
    private readonly ILogger<PostgresFullTextSearchSource> _logger;

    public PostgresFullTextSearchSource(FcmsDbContext db, ILogger<PostgresFullTextSearchSource> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string SourceId => "pg-fts";
    public string DisplayName => "Pages + Posts (Postgres FTS)";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int max, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var hits = new List<SearchHit>(capacity: max);
        var perTableMax = Math.Max(1, max / 2);

        // plainto_tsquery handles user-entered free text safely (vs to_tsquery
        // which requires a structured query and rejects user input). Matches
        // any prefix; for exact-phrase use phraseto_tsquery.
        try
        {
            var pageHits = await _db.Pages
                .FromSqlInterpolated($@"
                    SELECT * FROM fcms_pages
                    WHERE is_published = TRUE
                      AND to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, ''))
                          @@ plainto_tsquery('simple', {query})
                    ORDER BY ts_rank(
                        to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, '')),
                        plainto_tsquery('simple', {query})) DESC
                    LIMIT {perTableMax}")
                .AsNoTracking()
                .ToListAsync(ct);

            var postHits = await _db.Posts
                .FromSqlInterpolated($@"
                    SELECT * FROM fcms_posts
                    WHERE is_published = TRUE
                      AND to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, ''))
                          @@ plainto_tsquery('simple', {query})
                    ORDER BY ts_rank(
                        to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, '')),
                        plainto_tsquery('simple', {query})) DESC
                    LIMIT {perTableMax}")
                .AsNoTracking()
                .ToListAsync(ct);

            hits.AddRange(pageHits.Select(p => new SearchHit(p.Title, $"/{p.Slug}", "page", null, 1.0)));
            hits.AddRange(postHits.Select(p => new SearchHit(p.Title, $"/blog/{p.Slug}", "post", null, 1.0)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Postgres FTS search failed for query '{Query}'. Verify GIN indexes exist on fcms_pages + fcms_posts.",
                query);
            return [];
        }

        return hits.Take(max).ToList();
    }
}
