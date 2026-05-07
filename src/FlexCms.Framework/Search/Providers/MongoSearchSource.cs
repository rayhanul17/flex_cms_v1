using System.Text.RegularExpressions;
using FlexCms.Framework.Cms;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Search.Providers;

/// <summary>
/// MongoDB-native search source covering both Pages and Posts. Uses Mongo
/// regex (case-insensitive) over <c>title</c> / <c>content</c> fields.
/// Replaces the EF-only <c>MySqlFullTextSearchSource</c> /
/// <c>PostgresFullTextSearchSource</c> when the deployment runs on Mongo.
///
/// <para>
/// For very large corpora, switch from regex to a real text index:
/// </para>
/// <code>
/// db.fcms_pages.createIndex({ title: "text", content: "text" })
/// db.fcms_posts.createIndex({ title: "text", content: "text" })
/// </code>
/// <para>
/// Then change the filter to <c>Filter.Text(query)</c>. We default to
/// regex because text indexes have a single-per-collection cap that
/// conflicts with module-defined indexes; regex works without setup.
/// </para>
/// </summary>
public sealed class MongoSearchSource : IFcmsSearchableSource
{
    private readonly IMongoDatabase _db;
    private readonly ILogger<MongoSearchSource> _logger;

    public MongoSearchSource(IMongoDatabase db, ILogger<MongoSearchSource> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string SourceId => "mongo";
    public string DisplayName => "Pages + Posts (MongoDB)";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int max, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var perTableMax = Math.Max(1, max / 2);

        // Escape regex specials so a query like "C# 9.0" doesn't blow up the
        // pattern. Multiline + case-insensitive — content fields can be huge,
        // need IgnoreCase.
        var pattern = Regex.Escape(query.Trim());
        var bsonPattern = new MongoDB.Bson.BsonRegularExpression(pattern, "i");
        var hits = new List<SearchHit>(capacity: max);

        try
        {
            var pages = _db.GetCollection<FcmsPage>("fcms_pages");
            var pageFilter = Builders<FcmsPage>.Filter.And(
                Builders<FcmsPage>.Filter.Eq(p => p.IsPublished, true),
                Builders<FcmsPage>.Filter.Or(
                    Builders<FcmsPage>.Filter.Regex(p => p.Title, bsonPattern),
                    Builders<FcmsPage>.Filter.Regex(p => p.Content, bsonPattern)));
            var pageHits = await pages.Find(pageFilter).Limit(perTableMax).ToListAsync(ct);

            var posts = _db.GetCollection<FcmsPost>("fcms_posts");
            var postFilter = Builders<FcmsPost>.Filter.And(
                Builders<FcmsPost>.Filter.Eq(p => p.IsPublished, true),
                Builders<FcmsPost>.Filter.Or(
                    Builders<FcmsPost>.Filter.Regex(p => p.Title, bsonPattern),
                    Builders<FcmsPost>.Filter.Regex(p => p.Content, bsonPattern)));
            var postHits = await posts.Find(postFilter).Limit(perTableMax).ToListAsync(ct);

            // Title hits score 2.0, content-only hits score 1.0 — same
            // convention as the LIKE source so the ranking is consistent
            // when sources are mixed in one query.
            hits.AddRange(pageHits.Select(p => new SearchHit(
                p.Title, $"/{p.Slug}", "page", null,
                p.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2.0 : 1.0)));
            hits.AddRange(postHits.Select(p => new SearchHit(
                p.Title, $"/blog/{p.Slug}", "post", null,
                p.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2.0 : 1.0)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mongo search failed for query '{Query}'.", query);
            return [];
        }

        return hits.Take(max).ToList();
    }
}
