using System.Security.Claims;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FlexCms.Framework.Chat;

/// <summary>
/// SignalR hub for realtime chat. Connections from the user widget land in
/// the <c>thread:{threadId}</c> + <c>user:{userId}</c> groups; admin
/// connections land in <c>admins</c> + each <c>thread:{id}</c> they're
/// actively viewing. Server pushes:
/// <list type="bullet">
///   <item><c>NewMessage(threadId, message)</c> — to the recipient(s) of the thread.</item>
///   <item><c>ThreadResolved(threadId)</c> — to the user.</item>
///   <item><c>NewThreadActivity(threadId, preview)</c> — to all admins (drives the unread dot in the admin list).</item>
/// </list>
///
/// All payloads are anonymous-typed so the JS client doesn't need a strong
/// schema; the server controls what fields it sends.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IChatService _chat;
    private readonly IPermissionService _perms;

    public const string AdminsGroup = "chat:admins";

    public ChatHub(IChatService chat, IPermissionService perms)
    {
        _chat = chat;
        _perms = perms;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId();
        if (userId is null) { Context.Abort(); return; }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        if (Context.User?.IsInRole(FcmsRoles.SuperAdmin) == true
            || await _perms.HasPermissionAsync(Context.User!, ChatPermissions.Reply))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Subscribe this connection to a specific thread's broadcasts.</summary>
    public Task JoinThread(Guid threadId)
        => Groups.AddToGroupAsync(Context.ConnectionId, ThreadGroup(threadId));

    public Task LeaveThread(Guid threadId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, ThreadGroup(threadId));

    /// <summary>Called by the user widget to send a message.</summary>
    public async Task<object> SendMessage(string body)
    {
        var userId = CurrentUserId() ?? throw new HubException("Not signed in.");
        if (Context.User is null
            || !(Context.User.IsInRole(FcmsRoles.SuperAdmin)
                 || await _perms.HasPermissionAsync(Context.User, ChatPermissions.Send)))
            throw new HubException("Forbidden.");

        var thread = await _chat.GetOrCreateOpenThreadAsync(userId, CurrentDisplayName());

        var msg = await _chat.AddMessageAsync(new FcmsChatMessage
        {
            ThreadId = thread.Id,
            SenderUserId = userId,
            SenderRole = ChatSenderRole.User,
            SenderDisplayName = CurrentDisplayName(),
            Body = body ?? ""
        });

        await BroadcastNewMessageAsync(thread, msg);
        return ToWire(msg);
    }

    /// <summary>Called by the admin panel to reply to a thread.</summary>
    public async Task<object> SendReply(Guid threadId, string body)
    {
        var adminId = CurrentUserId() ?? throw new HubException("Not signed in.");
        if (Context.User is null
            || !(Context.User.IsInRole(FcmsRoles.SuperAdmin)
                 || await _perms.HasPermissionAsync(Context.User, ChatPermissions.Reply)))
            throw new HubException("Forbidden.");

        var thread = await _chat.GetThreadAsync(threadId) ?? throw new HubException("Thread not found.");

        var msg = await _chat.AddMessageAsync(new FcmsChatMessage
        {
            ThreadId = thread.Id,
            SenderUserId = adminId,
            SenderRole = ChatSenderRole.Admin,
            SenderDisplayName = CurrentDisplayName(),
            Body = body ?? ""
        });

        await BroadcastNewMessageAsync(thread, msg);
        return ToWire(msg);
    }

    public async Task ResolveThread(Guid threadId)
    {
        var adminId = CurrentUserId() ?? throw new HubException("Not signed in.");
        if (Context.User is null
            || !(Context.User.IsInRole(FcmsRoles.SuperAdmin)
                 || await _perms.HasPermissionAsync(Context.User, ChatPermissions.Reply)))
            throw new HubException("Forbidden.");

        await _chat.ResolveThreadAsync(threadId, adminId);
        var thread = await _chat.GetThreadAsync(threadId);
        if (thread is null) return;

        await Clients.Group($"user:{thread.UserId}").SendAsync("ThreadResolved", threadId);
        await Clients.Group(ThreadGroup(threadId)).SendAsync("ThreadResolved", threadId);
    }

    private async Task BroadcastNewMessageAsync(FcmsChatThread thread, FcmsChatMessage msg)
    {
        var payload = ToWire(msg);
        // Push to the thread room (whoever is actively viewing it)
        await Clients.Group(ThreadGroup(thread.Id)).SendAsync("NewMessage", thread.Id, payload);
        // Plus push to the thread's user — even if they don't have the widget open, the next-poll badge sees it
        await Clients.Group($"user:{thread.UserId}").SendAsync("NewMessage", thread.Id, payload);
        // Plus admin list refresh — short snippet only, full message available via /chat/messages
        await Clients.Group(AdminsGroup).SendAsync("NewThreadActivity", thread.Id, msg.SenderRole.ToString(), thread.LastMessagePreview);
    }

    private static string ThreadGroup(Guid id) => $"thread:{id}";

    private Guid? CurrentUserId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private string CurrentDisplayName()
        => Context.User?.Identity?.Name ?? "(unknown)";

    private static object ToWire(FcmsChatMessage m) => new
    {
        id = m.Id,
        threadId = m.ThreadId,
        senderRole = m.SenderRole.ToString().ToLowerInvariant(),
        senderName = m.SenderDisplayName,
        body = m.Body,
        attachmentKind = m.AttachmentKind.ToString().ToLowerInvariant(),
        attachmentUrl = m.AttachmentUrl,
        attachmentName = m.AttachmentName,
        createdAt = m.CreatedAt
    };
}
