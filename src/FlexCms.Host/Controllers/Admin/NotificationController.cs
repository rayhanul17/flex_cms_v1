using FlexCms.Framework.Auth;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

/// <summary>
/// AJAX-only controller backing the admin top-bar bell icon. The view layer
/// polls <c>/admin/notifications/recent</c> every 60s for the unread badge +
/// dropdown content; mark-read endpoints return JSON for in-place UI updates.
/// </summary>
[Route("admin/notifications")]
public class NotificationController : BaseAdminController
{
    private readonly IFcmsNotificationService _notifications;
    private readonly UserManager<FcmsUser> _users;

    public NotificationController(IFcmsNotificationService notifications, UserManager<FcmsUser> users)
    {
        _notifications = notifications;
        _users = users;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.NotificationsView)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync();
        if (userId == Guid.Empty) return RedirectToAction("Login", "Auth");

        // Pull a generous slice (50) for the dedicated page; the bell dropdown
        // uses /recent with max:10 for the popover.
        var items = await _notifications.GetRecentAsync(userId, max: 50, ct);
        var unread = await _notifications.GetUnreadCountAsync(userId, ct);

        ViewBag.UnreadCount = unread;
        return View(items);
    }

    [HttpGet("recent")]
    [FcmsAuthorize(FcmsPermissions.NotificationsView)]
    public async Task<IActionResult> Recent(CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync();
        if (userId == Guid.Empty) return FcmsFail("Not signed in.");

        var rows = await _notifications.GetRecentAsync(userId, max: 10, ct);
        var unread = await _notifications.GetUnreadCountAsync(userId, ct);

        return Json(new
        {
            isSuccess = true,
            unread,
            items = rows.Select(n => new
            {
                id = n.Id,
                title = n.Title,
                body = n.Body,
                level = n.Level.ToString().ToLowerInvariant(),
                url = n.Url,
                icon = n.Icon ?? "bi bi-bell",
                isRead = n.IsRead,
                createdAt = FcmsTime.Format(n.CreatedAt)
            })
        });
    }

    [HttpPost("mark-read/{id:guid}")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.NotificationsManage)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync();
        if (userId == Guid.Empty) return FcmsFail("Not signed in.");
        await _notifications.MarkReadAsync(userId, id, ct);
        return FcmsOk();
    }

    [HttpPost("mark-all-read")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.NotificationsManage)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync();
        if (userId == Guid.Empty) return FcmsFail("Not signed in.");
        var n = await _notifications.MarkAllReadAsync(userId, ct);
        return FcmsOk($"{n} notifications marked read.");
    }

    private async Task<Guid> ResolveUserIdAsync()
    {
        var user = await _users.GetUserAsync(User);
        return user?.Id ?? Guid.Empty;
    }
}
