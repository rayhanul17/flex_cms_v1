using FlexCms.Framework.Db;

namespace FlexCms.Framework.Sessions;

public sealed class SessionService : ISessionService
{
    private readonly IRepository<FcmsUserSession> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public SessionService(IRepository<FcmsUserSession> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<FcmsUserSession> RecordLoginAsync(Guid userId, string sessionId, string ip, string userAgent, string deviceLabel, CancellationToken ct = default)
    {
        var s = new FcmsUserSession
        {
            UserId = userId,
            SessionId = sessionId,
            IpAddress = ip ?? "",
            UserAgent = userAgent ?? "",
            DeviceLabel = deviceLabel ?? "",
            LastSeenAt = Clock.FcmsTime.Now
        };
        await _repo.AddAsync(s, ct);
        await _uow.SaveChangesAsync(ct);
        return s;
    }

    public async Task<List<FcmsUserSession>> GetActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(s => s.UserId == userId && !s.IsRevoked, ct);
        return rows.OrderByDescending(s => s.LastSeenAt).ToList();
    }

    public async Task TouchAsync(string sessionId, CancellationToken ct = default)
    {
        var row = await _repo.FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
        if (row is null || row.IsRevoked) return;
        row.LastSeenAt = Clock.FcmsTime.Now;
        await _repo.UpdateAsync(row, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<bool> IsValidAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        var row = await _repo.FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
        return row is not null && !row.IsRevoked;
    }

    public async Task RevokeAsync(string sessionId, Guid? revokedByUserId, string? reason, CancellationToken ct = default)
    {
        var row = await _repo.FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
        if (row is null || row.IsRevoked) return;
        row.IsRevoked = true;
        row.RevokedAt = Clock.FcmsTime.Now;
        row.RevokedByUserId = revokedByUserId;
        row.RevokeReason = reason;
        await _repo.UpdateAsync(row, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<int> RevokeAllForUserAsync(Guid userId, Guid? revokedByUserId, string? reason, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(s => s.UserId == userId && !s.IsRevoked, ct);
        if (rows.Count == 0) return 0;
        var now = Clock.FcmsTime.Now;
        foreach (var r in rows)
        {
            r.IsRevoked = true;
            r.RevokedAt = now;
            r.RevokedByUserId = revokedByUserId;
            r.RevokeReason = reason;
            await _repo.UpdateAsync(r, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return rows.Count;
    }
}
