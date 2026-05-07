using FlexCms.Framework.Db;

namespace FlexCms.Framework.Search;

public sealed class FcmsSearchAnalytics : IFcmsSearchAnalytics
{
    private readonly IRepository<FcmsSearchQuery> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public FcmsSearchAnalytics(IRepository<FcmsSearchQuery> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task RecordAsync(string query, int resultCount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        await _repo.AddAsync(new FcmsSearchQuery
        {
            Query = query.Trim().ToLowerInvariant(),  // normalize for grouping
            ResultCount = resultCount,
        }, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NoResultEntry>> GetNoResultQueriesAsync(int days = 30, int max = 50, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, days));
        var rows = await _repo.FindAsync(r => r.ResultCount == 0 && r.CreatedAt >= cutoff, ct);
        return rows
            .GroupBy(r => r.Query, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NoResultEntry(g.Key, g.Count(), g.Max(r => r.CreatedAt)))
            .OrderByDescending(e => e.Attempts)
            .Take(Math.Max(1, max))
            .ToList();
    }
}
