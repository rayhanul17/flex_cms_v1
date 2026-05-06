using FlexCms.Framework.Db;

namespace FlexCms.Framework.Auth.History;

public sealed class LoginHistoryService : ILoginHistoryService
{
    private readonly IRepository<FcmsLoginHistory> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public LoginHistoryService(IRepository<FcmsLoginHistory> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task RecordAsync(string attemptedUserName, Guid? userId, LoginOutcome outcome, string ip, string userAgent, string? failReason = null, CancellationToken ct = default)
    {
        await _repo.AddAsync(new FcmsLoginHistory
        {
            AttemptedUserName = attemptedUserName ?? "",
            UserId = userId,
            Outcome = outcome,
            IpAddress = ip ?? "",
            UserAgent = userAgent ?? "",
            FailReason = failReason
        }, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<FcmsLoginHistory>> GetRecentAsync(int max = 100, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(_ => true, ct);
        return rows.OrderByDescending(r => r.CreatedAt).Take(max).ToList();
    }

    public async Task<int> GetFailedCountSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(r => r.Outcome != LoginOutcome.Success && r.CreatedAt >= sinceUtc, ct);
        return rows.Count;
    }
}
