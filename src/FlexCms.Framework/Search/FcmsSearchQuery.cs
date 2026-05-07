using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Search;

/// <summary>
/// One row per executed search query — drives the admin analytics:
/// "no-result queries" report (gap analysis) + popular queries.
/// Append-only; old rows pruned via <see cref="Cms.LogArchiveService"/>-
/// style retention if the table grows.
/// </summary>
public class FcmsSearchQuery : BaseEfEntity
{
    public string Query { get; set; } = "";
    public int ResultCount { get; set; }
    public Guid? UserId { get; set; }
}

public interface IFcmsSearchAnalytics
{
    Task RecordAsync(string query, int resultCount, CancellationToken ct = default);

    /// <summary>Top queries that returned zero results (default last 30 days, top 50).</summary>
    Task<IReadOnlyList<NoResultEntry>> GetNoResultQueriesAsync(int days = 30, int max = 50, CancellationToken ct = default);
}

public sealed record NoResultEntry(string Query, int Attempts, DateTime LastAttemptAt);
