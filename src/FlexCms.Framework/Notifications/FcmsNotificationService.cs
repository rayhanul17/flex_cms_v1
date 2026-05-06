using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using Microsoft.AspNetCore.Identity;

namespace FlexCms.Framework.Notifications;

public sealed class FcmsNotificationService : IFcmsNotificationService
{
    private readonly IRepository<FcmsNotification> _repo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly UserManager<FcmsUser> _users;

    public FcmsNotificationService(
        IRepository<FcmsNotification> repo,
        IFcmsUnitOfWork uow,
        UserManager<FcmsUser> users)
    {
        _repo = repo;
        _uow = uow;
        _users = users;
    }

    public async Task<FcmsNotification> NotifyUserAsync(
        Guid userId, string title, string? body = null,
        NotificationLevel level = NotificationLevel.Info,
        string? url = null, string? icon = null,
        CancellationToken ct = default)
    {
        var n = new FcmsNotification
        {
            UserId = userId,
            Title = title,
            Body = body,
            Level = level,
            Url = url,
            Icon = icon
        };
        await _repo.AddAsync(n, ct);
        await _uow.SaveChangesAsync(ct);
        return n;
    }

    public async Task<int> NotifyAllAsync(
        string title, string? body = null,
        NotificationLevel level = NotificationLevel.Info,
        string? url = null, string? icon = null,
        CancellationToken ct = default)
    {
        // Snapshot at insert time so per-user read state is uniform.
        var ids = _users.Users.Select(u => u.Id).ToList();
        if (ids.Count == 0) return 0;

        foreach (var uid in ids)
        {
            await _repo.AddAsync(new FcmsNotification
            {
                UserId = uid,
                Title = title,
                Body = body,
                Level = level,
                Url = url,
                Icon = icon
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return ids.Count;
    }

    public async Task<List<FcmsNotification>> GetRecentAsync(Guid userId, int max = 20, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(n => n.UserId == userId, ct);
        return rows.OrderByDescending(n => n.CreatedAt).Take(max).ToList();
    }

    public Task<long> CountAllAsync(Guid userId, CancellationToken ct = default)
        => _repo.CountAsync(n => n.UserId == userId, ct);

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
        => (int)await _repo.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var n = await _repo.FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, ct);
        if (n is null || n.IsRead) return;
        n.IsRead = true;
        n.ReadAt = Clock.FcmsTime.Now;
        await _repo.UpdateAsync(n, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var unread = await _repo.FindAsync(n => n.UserId == userId && !n.IsRead, ct);
        if (unread.Count == 0) return 0;
        var now = Clock.FcmsTime.Now;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
            await _repo.UpdateAsync(n, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return unread.Count;
    }
}
