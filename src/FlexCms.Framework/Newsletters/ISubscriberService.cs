using System.Security.Cryptography;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Newsletters;

public interface ISubscriberService
{
    /// <summary>
    /// Idempotent — if the email is already an Active subscriber, returns the
    /// existing row unchanged. New rows land in <see cref="SubscriberStatus.PendingVerification"/>
    /// with a fresh token.
    /// </summary>
    Task<FcmsSubscriber> SubscribeAsync(string email, string? name = null, CancellationToken ct = default);

    /// <summary>Activate a row by token (verify-link click).</summary>
    Task<bool> VerifyAsync(string token, CancellationToken ct = default);

    /// <summary>Flip a row to Unsubscribed by token (one-click link).</summary>
    Task<bool> UnsubscribeAsync(string token, CancellationToken ct = default);

    Task<List<FcmsSubscriber>> GetActiveAsync(CancellationToken ct = default);
}

public sealed class SubscriberService : ISubscriberService
{
    private readonly IRepository<FcmsSubscriber> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public SubscriberService(IRepository<FcmsSubscriber> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<FcmsSubscriber> SubscribeAsync(string email, string? name = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email required.", nameof(email));
        var normalized = email.Trim().ToLowerInvariant();

        var existing = await _repo.FirstOrDefaultAsync(s => s.Email == normalized, ct);
        if (existing is not null)
        {
            // Re-subscribing after unsubscribe should re-issue a verify link.
            if (existing.SubscriberStatus == SubscriberStatus.Unsubscribed)
            {
                existing.SubscriberStatus = SubscriberStatus.PendingVerification;
                existing.Token = NewToken();
                existing.UnsubscribedAt = null;
                await _repo.UpdateAsync(existing, ct);
                await _uow.SaveChangesAsync(ct);
            }
            return existing;
        }

        var sub = new FcmsSubscriber
        {
            Email = normalized,
            Name = name?.Trim(),
            Token = NewToken()
        };
        await _repo.AddAsync(sub, ct);
        await _uow.SaveChangesAsync(ct);
        return sub;
    }

    public async Task<bool> VerifyAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var sub = await _repo.FirstOrDefaultAsync(s => s.Token == token, ct);
        if (sub is null || sub.SubscriberStatus == SubscriberStatus.Active) return sub is not null;

        sub.SubscriberStatus = SubscriberStatus.Active;
        sub.VerifiedAt = Clock.FcmsTime.Now;
        await _repo.UpdateAsync(sub, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UnsubscribeAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var sub = await _repo.FirstOrDefaultAsync(s => s.Token == token, ct);
        if (sub is null) return false;

        sub.SubscriberStatus = SubscriberStatus.Unsubscribed;
        sub.UnsubscribedAt = Clock.FcmsTime.Now;
        await _repo.UpdateAsync(sub, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<FcmsSubscriber>> GetActiveAsync(CancellationToken ct = default)
        => (await _repo.FindAsync(s => s.SubscriberStatus == SubscriberStatus.Active, ct))
            .OrderBy(s => s.Email).ToList();

    private static string NewToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
