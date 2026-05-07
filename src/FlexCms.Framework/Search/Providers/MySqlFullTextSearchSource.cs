using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Search.Providers;

/// <summary>
/// MySQL <c>FULLTEXT</c>-backed search source covering both Pages and Posts
/// in one impl (each with its own SQL). Replaces the LIKE-based defaults
/// for sites with non-trivial corpora — sub-100ms on 10k+ rows.
///
/// <para>
/// <b>Admin setup</b> (one-time): create the FULLTEXT indexes
/// (<c>fcms_pages</c> + <c>fcms_posts</c>) before registering this source.
/// MySQL syntax:
/// </para>
/// <code>
/// ALTER TABLE fcms_pages ADD FULLTEXT INDEX ftx_pages_title_content (title, content);
/// ALTER TABLE fcms_posts ADD FULLTEXT INDEX ftx_posts_title_content (title, content);
/// </code>
///
/// <para>
/// Registration:
/// </para>
/// <code>
/// services.AddScoped&lt;IFcmsSearchableSource, MySqlFullTextSearchSource&gt;();
/// </code>
///
/// <para>
/// Falls back gracefully if the indexes don't exist (returns empty + logs
/// a warning) — that way an admin who registers this provider before
/// running the DDL doesn't crash the whole search page.
/// </para>
/// </summary>
public sealed class MySqlFullTextSearchSource : IFcmsSearchableSource
{
    private readonly FcmsDbContext _db;
    private readonly ILogger<MySqlFullTextSearchSource> _logger;

    public MySqlFullTextSearchSource(FcmsDbContext db, ILogger<MySqlFullTextSearchSource> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string SourceId => "mysql-ft";
    public string DisplayName => "Pages + Posts (MySQL FULLTEXT)";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int max, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var hits = new List<SearchHit>(capacity: max);
        var perTableMax = Math.Max(1, max / 2);

        try
        {
            // BOOLEAN MODE supports operators (+ required, - excluded, * wildcard).
            // We pass the raw query — admin can teach end-users about the syntax,
            // or the search controller can pre-process.
            var pageHits = await _db.Pages
                .FromSqlInterpolated($@"
                    SELECT * FROM fcms_pages
                    WHERE is_published = 1
                      AND MATCH(title, content) AGAINST({query} IN BOOLEAN MODE)
                    ORDER BY MATCH(title, content) AGAINST({query} IN BOOLEAN MODE) DESC
                    LIMIT {perTableMax}")
                .AsNoTracking()
                .ToListAsync(ct);

            var postHits = await _db.Posts
                .FromSqlInterpolated($@"
                    SELECT * FROM fcms_posts
                    WHERE is_published = 1
                      AND MATCH(title, content) AGAINST({query} IN BOOLEAN MODE)
                    ORDER BY MATCH(title, content) AGAINST({query} IN BOOLEAN MODE) DESC
                    LIMIT {perTableMax}")
                .AsNoTracking()
                .ToListAsync(ct);

            // Score 1.0 — MySQL already returned results in relevance order;
            // we just preserve the order without inventing a numeric weight.
            hits.AddRange(pageHits.Select(p => new SearchHit(p.Title, $"/{p.Slug}", "page", null, 1.0)));
            hits.AddRange(postHits.Select(p => new SearchHit(p.Title, $"/blog/{p.Slug}", "post", null, 1.0)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MySQL FULLTEXT search failed for query '{Query}'. Verify FULLTEXT indexes exist on fcms_pages + fcms_posts.",
                query);
            return [];
        }

        return hits.Take(max).ToList();
    }
}
