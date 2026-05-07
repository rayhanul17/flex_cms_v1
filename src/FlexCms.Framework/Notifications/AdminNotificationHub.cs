using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FlexCms.Framework.Notifications;

/// <summary>
/// SignalR hub that pushes admin bell-icon notifications in real time
/// (Phase 16 — Issue 107). Replaces the 60s polling fallback for admins
/// who are signed in.
///
/// <para>
/// Connections land in two groups:
/// </para>
/// <list type="bullet">
///   <item><c>user:{userId}</c> — for notifications targeting a specific admin.</item>
///   <item><c>admins</c> — for broadcast notifications to all admins.</item>
/// </list>
///
/// <para>
/// Server pushes <c>NewNotification</c> with payload <c>{ id, message, url, icon, createdAt, unreadCount }</c>
/// — JS bumps the bell badge + optionally renders a toast.
/// </para>
///
/// <para>
/// Polling is kept as a graceful-degradation fallback (browsers behind
/// SignalR-blocking proxies, or hub down): the existing 60s
/// <see cref="INotificationService.GetUnreadCountAsync"/> JS poll keeps
/// running but at a much reduced rate when SignalR is connected.
/// </para>
/// </summary>
[Authorize]
public sealed class AdminNotificationHub : Hub
{
    public const string AdminsGroup = "admin:notifications";

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId();
        if (userId is null) { Context.Abort(); return; }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        // Any authenticated user gets the per-user channel; broadcast group
        // membership filters by role (so non-admins don't get admin
        // broadcasts even if they connect).
        if (Context.User?.IsInRole("SuperAdmin") == true || Context.User?.IsInRole("Admin") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);

        await base.OnConnectedAsync();
    }

    private Guid? CurrentUserId()
    {
        var sid = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sid, out var id) ? id : null;
    }
}

/// <summary>
/// Server-side push helper — controllers call this when they create a
/// notification record. Hides the SignalR client-proxy plumbing so the
/// rest of the framework doesn't take a hard dep on the hub type.
/// </summary>
public interface IAdminNotificationPusher
{
    Task PushToUserAsync(Guid userId, object payload, CancellationToken ct = default);
    Task PushToAllAdminsAsync(object payload, CancellationToken ct = default);
}

public sealed class AdminNotificationPusher : IAdminNotificationPusher
{
    private readonly IHubContext<AdminNotificationHub> _hub;
    public AdminNotificationPusher(IHubContext<AdminNotificationHub> hub) => _hub = hub;

    public Task PushToUserAsync(Guid userId, object payload, CancellationToken ct = default)
        => _hub.Clients.Group($"user:{userId}").SendAsync("NewNotification", payload, ct);

    public Task PushToAllAdminsAsync(object payload, CancellationToken ct = default)
        => _hub.Clients.Group(AdminNotificationHub.AdminsGroup).SendAsync("NewNotification", payload, ct);
}
