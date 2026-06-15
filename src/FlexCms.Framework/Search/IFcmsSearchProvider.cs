namespace FlexCms.Framework.Search;

/// <summary>
/// Pluggable full-text search backend (Phase 16 — Issue 106). Default
/// impl is <see cref="LikeSearchProvider"/> (works on any RDBMS via simple
/// <c>LIKE %term%</c>); production deployments swap in a vendor-specific
/// impl that uses MySQL FULLTEXT / Postgres tsvector / SQL Server FTS for
/// sub-100ms results on large corpora.
///
/// <para>
/// Modules can register additional <see cref="IFcmsSearchableSource"/>
/// implementations to contribute their own searchable entities (e-com
/// products, KB articles, etc.) — the framework runs each registered
/// source per query and merges the result.
/// </para>
/// </summary>
public interface IFcmsSearchProvider
{
    /// <summary>Run the query across every registered source.</summary>
    Task<SearchResults> SearchAsync(string query, int page = 1, int pageSize = 20, CancellationToken ct = default);
}

/// <summary>
/// One source per searchable entity (FcmsPost, FcmsPage, module entities).
/// The provider iterates registered sources; each source returns matches
/// scoped to its own data set + the canonical URL/title for the result UI.
/// </summary>
public interface IFcmsSearchableSource
{
    /// <summary>Stable identifier — used in result listings + analytics.</summary>
    string SourceId { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int max, CancellationToken ct = default);
}

public sealed record SearchHit(string Title, string Url, string SourceId, string? Snippet = null, double Score = 0);

public sealed record SearchResults(
    string Query,
    IReadOnlyList<SearchHit> Hits,
    int TotalCount,
    int Page,
    int PageSize);
