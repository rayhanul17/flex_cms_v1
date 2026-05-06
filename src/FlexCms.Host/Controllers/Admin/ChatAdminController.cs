using FlexCms.Framework.Auth;
using FlexCms.Framework.Chat;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

/// <summary>
/// Admin-side chat panel. Renders the thread list shell + boots a SignalR
/// connection client-side; every push from <see cref="ChatHub"/> updates the
/// list/preview without a full reload. Detail loading uses
/// <c>/chat/messages?threadId=...</c> from <c>ChatController</c>.
/// </summary>
[Route("admin/chat")]
public class ChatAdminController : BaseAdminController
{
    private readonly IChatService _chat;

    public ChatAdminController(IChatService chat) => _chat = chat;

    [HttpGet("")]
    [FcmsAuthorize(ChatPermissions.Reply)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var threads = await _chat.GetRecentThreadsAsync(50, ct);
        return View(threads);
    }

    [HttpGet("threads")]
    [FcmsAuthorize(ChatPermissions.Reply)]
    public async Task<IActionResult> Threads(CancellationToken ct)
    {
        var threads = await _chat.GetRecentThreadsAsync(50, ct);
        return Json(new
        {
            isSuccess = true,
            threads = threads.Select(t => new
            {
                id = t.Id,
                user = t.UserDisplayName,
                status = t.ThreadStatus.ToString().ToLowerInvariant(),
                lastAt = t.LastMessageAt,
                preview = t.LastMessagePreview
            })
        });
    }
}
