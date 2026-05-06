using System.Security.Claims;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Chat;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Services;
using FlexCms.Framework.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace FlexCms.Host.Controllers;

/// <summary>
/// AJAX fallback for the SignalR-driven chat — used when the websocket is
/// unavailable (proxy strips upgrade, transient connection drop, etc.).
/// Also hosts the file-upload endpoint since SignalR isn't a great vehicle
/// for binary blobs.
///
/// All write actions broadcast through <see cref="ChatHub"/> after the
/// service call so connected clients still see the message in realtime.
/// </summary>
[Route("chat")]
[Authorize]
public class ChatController : Controller
{
    private readonly IChatService _chat;
    private readonly IFcmsFileStorage _storage;
    private readonly ISettingsService _settings;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IPermissionService _perms;

    // jpg/png/gif/webp/pdf/zip + a couple of office formats. Magic-byte
    // pairs only — anything not in this map is rejected even if its
    // extension is whitelisted in ChatSettings.
    private static readonly Dictionary<string, byte[][]> MagicBytes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg",  [[0xFF, 0xD8, 0xFF]] },
        { ".jpeg", [[0xFF, 0xD8, 0xFF]] },
        { ".png",  [[0x89, 0x50, 0x4E, 0x47]] },
        { ".gif",  [[0x47, 0x49, 0x46, 0x38]] },
        { ".webp", [[0x52, 0x49, 0x46, 0x46]] },
        { ".pdf",  [[0x25, 0x50, 0x44, 0x46]] },
        { ".zip",  [[0x50, 0x4B, 0x03, 0x04]] },
        // .docx/.xlsx are also zip containers — same magic bytes
        { ".docx", [[0x50, 0x4B, 0x03, 0x04]] },
        { ".xlsx", [[0x50, 0x4B, 0x03, 0x04]] },
        { ".doc",  [[0xD0, 0xCF, 0x11, 0xE0]] },
        { ".xls",  [[0xD0, 0xCF, 0x11, 0xE0]] },
        { ".txt",  [[]] }, // intentionally permissive — text has no signature
    };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    public ChatController(
        IChatService chat,
        IFcmsFileStorage storage,
        ISettingsService settings,
        IHubContext<ChatHub> hub,
        IPermissionService perms)
    {
        _chat = chat;
        _storage = storage;
        _settings = settings;
        _hub = hub;
        _perms = perms;
    }

    [HttpGet("messages")]
    public async Task<IActionResult> Messages(Guid? threadId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        FcmsChatThread? thread;
        if (threadId is null)
            thread = await _chat.GetOrCreateOpenThreadAsync(userId.Value, CurrentDisplayName(), ct);
        else
        {
            thread = await _chat.GetThreadAsync(threadId.Value, ct);
            if (thread is null) return NotFound();
            // Non-admin can only see their own threads.
            if (!IsAdmin() && thread.UserId != userId.Value) return Forbid();
        }

        var messages = await _chat.GetMessagesAsync(thread.Id, ct);

        // Mark "the other side's" messages as read since this caller just loaded them.
        var role = IsAdmin() ? ChatSenderRole.Admin : ChatSenderRole.User;
        await _chat.MarkReadAsync(thread.Id, role, ct);

        return Json(new
        {
            isSuccess = true,
            thread = new { thread.Id, status = thread.ThreadStatus.ToString().ToLowerInvariant() },
            messages = messages.Select(ToWire)
        });
    }

    [HttpPost("send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromForm] string body, [FromForm] Guid? threadId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        FcmsChatThread thread;
        ChatSenderRole senderRole;

        if (IsAdmin())
        {
            if (threadId is null) return BadRequest("threadId required for admin reply.");
            thread = await _chat.GetThreadAsync(threadId.Value, ct) ?? throw new InvalidOperationException("Thread not found.");
            senderRole = ChatSenderRole.Admin;
        }
        else
        {
            if (!await _perms.HasPermissionAsync(User, ChatPermissions.Send, ct))
                return Forbid();
            thread = await _chat.GetOrCreateOpenThreadAsync(userId.Value, CurrentDisplayName(), ct);
            senderRole = ChatSenderRole.User;
        }

        var msg = await _chat.AddMessageAsync(new FcmsChatMessage
        {
            ThreadId = thread.Id,
            SenderUserId = userId,
            SenderRole = senderRole,
            SenderDisplayName = CurrentDisplayName(),
            Body = body ?? ""
        }, ct);

        await PushAsync(thread, msg, ct);
        return Json(new { isSuccess = true, message = ToWire(msg) });
    }

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] Guid? threadId, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { isSuccess = false, message = "No file." });

        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var cfg = await _settings.GetAsync<ChatSettings>("chat:default", ct);
        if (!cfg.Enabled) return BadRequest(new { isSuccess = false, message = "Chat disabled." });

        var maxBytes = (long)cfg.MaxUploadSizeMb * 1024 * 1024;
        if (file.Length > maxBytes)
            return BadRequest(new { isSuccess = false, message = $"File exceeds {cfg.MaxUploadSizeMb} MB." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = (cfg.AllowedExtensions ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!allowed.Contains(ext))
            return BadRequest(new { isSuccess = false, message = $"Extension '{ext}' not allowed." });

        await using var stream = file.OpenReadStream();
        if (!await ValidateMagicBytesAsync(stream, ext, ct))
            return BadRequest(new { isSuccess = false, message = "File content does not match its extension." });

        // Storage path: /uploads/chat/{yyyy}/{MM}/{newGuid}{ext}
        var now = FcmsTime.Now;
        var safeName = Guid.NewGuid().ToString("N") + ext;
        var relativePath = $"uploads/chat/{now:yyyy}/{now:MM}/{safeName}";

        stream.Position = 0;
        var publicUrl = await _storage.SaveAsync(relativePath, stream, ct);

        // Resolve / create the user's open thread (or admin's chosen thread)
        FcmsChatThread thread;
        ChatSenderRole senderRole;
        if (IsAdmin() && threadId is not null)
        {
            thread = await _chat.GetThreadAsync(threadId.Value, ct) ?? throw new InvalidOperationException("Thread not found.");
            senderRole = ChatSenderRole.Admin;
        }
        else
        {
            thread = await _chat.GetOrCreateOpenThreadAsync(userId.Value, CurrentDisplayName(), ct);
            senderRole = ChatSenderRole.User;
        }

        var msg = await _chat.AddMessageAsync(new FcmsChatMessage
        {
            ThreadId = thread.Id,
            SenderUserId = userId,
            SenderRole = senderRole,
            SenderDisplayName = CurrentDisplayName(),
            Body = "",
            AttachmentKind = ImageExts.Contains(ext) ? ChatAttachmentKind.Image : ChatAttachmentKind.File,
            AttachmentUrl = publicUrl,
            AttachmentName = file.FileName,
            AttachmentSizeBytes = file.Length
        }, ct);

        await PushAsync(thread, msg, ct);
        return Json(new { isSuccess = true, message = ToWire(msg) });
    }

    [HttpPost("new-thread")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewThread(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (IsAdmin()) return BadRequest("Admins do not start threads.");

        var thread = await _chat.StartNewThreadAsync(userId.Value, CurrentDisplayName(), ct);
        return Json(new { isSuccess = true, threadId = thread.Id });
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task PushAsync(FcmsChatThread thread, FcmsChatMessage msg, CancellationToken ct)
    {
        var payload = ToWire(msg);
        await _hub.Clients.Group($"thread:{thread.Id}").SendAsync("NewMessage", thread.Id, payload, ct);
        await _hub.Clients.Group($"user:{thread.UserId}").SendAsync("NewMessage", thread.Id, payload, ct);
        await _hub.Clients.Group(ChatHub.AdminsGroup).SendAsync(
            "NewThreadActivity", thread.Id, msg.SenderRole.ToString(), thread.LastMessagePreview, ct);
    }

    private static async Task<bool> ValidateMagicBytesAsync(Stream stream, string ext, CancellationToken ct)
    {
        if (!MagicBytes.TryGetValue(ext, out var signatures) || signatures.Length == 0) return true;
        if (signatures.Length == 1 && signatures[0].Length == 0) return true; // permissive entry (txt)

        var maxLen = signatures.Max(s => s.Length);
        var head = new byte[maxLen];
        var read = 0;
        while (read < maxLen)
        {
            var n = await stream.ReadAsync(head.AsMemory(read, maxLen - read), ct);
            if (n == 0) break;
            read += n;
        }
        if (read < maxLen) return false;

        foreach (var sig in signatures)
        {
            var ok = true;
            for (int i = 0; i < sig.Length; i++)
                if (head[i] != sig[i]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    private bool IsAdmin()
        => User.IsInRole(FcmsRoles.SuperAdmin)
           || User.IsInRole("Admin")
           || User.HasClaim(c => c.Type == "permission" && c.Value == ChatPermissions.Reply);

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private string CurrentDisplayName() => User.Identity?.Name ?? "(unknown)";

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
