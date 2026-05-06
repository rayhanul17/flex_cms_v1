using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Messaging;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

/// <summary>
/// Admin UI for broadcasting an email or SMS to all users / a role / a hand-
/// picked subset. The actual delivery happens asynchronously via
/// <see cref="MessageProcessorService"/> draining
/// <see cref="FcmsPendingMessage"/> rows — the controller call only inserts
/// rows + returns the broadcast id and recipient count.
/// </summary>
[Route("admin/broadcast")]
public class BroadcastController : BaseAdminController
{
    private readonly IBroadcastService _broadcast;
    private readonly IRepository<FcmsPendingMessage> _msgRepo;
    private readonly RoleManager<FcmsRole> _roles;

    public BroadcastController(
        IBroadcastService broadcast,
        IRepository<FcmsPendingMessage> msgRepo,
        RoleManager<FcmsRole> roles)
    {
        _broadcast = broadcast;
        _msgRepo = msgRepo;
        _roles = roles;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.MessagingView)]
    public IActionResult Index() => View(new BroadcastViewModel
    {
        AvailableRoles = _roles.Roles.Select(r => r.Name ?? "").Where(n => n.Length > 0).ToList()
    });

    [HttpPost("send")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.MessagingBroadcast)]
    [FcmsLog("messaging.broadcast", "Broadcast")]
    public async Task<IActionResult> Send(BroadcastViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            vm.AvailableRoles = _roles.Roles.Select(r => r.Name ?? "").Where(n => n.Length > 0).ToList();
            return View(nameof(Index), vm);
        }

        var req = new BroadcastRequest(
            Channel: vm.Channel,
            Target: vm.Target,
            Subject: vm.Subject ?? "",
            Body: vm.Body ?? "",
            IsHtml: vm.IsHtml,
            RoleName: vm.RoleName,
            UserIds: vm.SelectedUserIds);

        var result = await _broadcast.SendAsync(req, ct);
        FcmsLogContext.SetValue(HttpContext, new { result.BroadcastId, result.Enqueued, vm.Channel, vm.Target });

        if (result.Enqueued == 0)
            ShowWarning("No recipients matched — nothing to send.");
        else
            ShowSuccess($"{result.Enqueued} message(s) queued. Broadcast ID: {result.BroadcastId}.");

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("history")]
    [FcmsAuthorize(FcmsPermissions.MessagingView)]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        // Limit history to most-recent 100 broadcasts so the page doesn't fall
        // over once thousands of rows accumulate. A full audit lives in audit logs.
        var rows = (await _msgRepo.FindAsync(m => m.BroadcastId != null, ct))
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .ToList();
        return View(rows);
    }
}
