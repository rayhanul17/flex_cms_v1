using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using Microsoft.AspNetCore.Identity;

namespace FlexCms.Framework.Messaging;

public sealed class BroadcastService : IBroadcastService
{
    private readonly IRepository<FcmsPendingMessage> _repo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly UserManager<FcmsUser> _users;

    public BroadcastService(
        IRepository<FcmsPendingMessage> repo,
        IFcmsUnitOfWork uow,
        UserManager<FcmsUser> users)
    {
        _repo = repo;
        _uow = uow;
        _users = users;
    }

    public async Task<BroadcastResult> SendAsync(BroadcastRequest req, CancellationToken ct = default)
    {
        if (req is null) return BroadcastResult.Empty;

        var recipients = await ResolveRecipientsAsync(req, ct);
        if (recipients.Count == 0) return BroadcastResult.Empty;

        var broadcastId = Guid.NewGuid();
        foreach (var addr in recipients)
        {
            await _repo.AddAsync(new FcmsPendingMessage
            {
                Channel = req.Channel,
                To = addr,
                Subject = req.Subject ?? "",
                Body = req.Body ?? "",
                IsHtml = req.IsHtml,
                BroadcastId = broadcastId
            }, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return new BroadcastResult(broadcastId, recipients.Count);
    }

    private async Task<List<string>> ResolveRecipientsAsync(BroadcastRequest req, CancellationToken ct)
    {
        var users = req.Target switch
        {
            BroadcastTarget.AllUsers => _users.Users.ToList(),
            BroadcastTarget.ByRole when !string.IsNullOrWhiteSpace(req.RoleName)
                => (await _users.GetUsersInRoleAsync(req.RoleName!)).ToList(),
            BroadcastTarget.Selected when req.UserIds is { Count: > 0 }
                => _users.Users.Where(u => req.UserIds!.Contains(u.Id)).ToList(),
            _ => []
        };

        // Pick the right field per channel + drop empties so the worker doesn't waste retries on garbage rows.
        return req.Channel switch
        {
            MessageChannel.Email => users
                .Select(u => u.Email)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MessageChannel.Sms => users
                .Select(u => u.PhoneNumber)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            _ => []
        };
    }
}
