using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Cms.Drafts;
using FlexCms.Framework.Cms.Preview;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

/// <summary>
/// Editor-companion endpoints used by the page/post edit forms — issued
/// AJAX from the client every ~30s (autosave) or on a "Get share link"
/// button click (preview token). Both routes are POST + antiforgery so
/// they can't be triggered cross-origin.
/// </summary>
[Route("admin/authoring")]
public class ContentAuthoringController : BaseAdminController
{
    private readonly IDraftSnapshotService _drafts;
    private readonly IPreviewTokenService _previewTokens;

    public ContentAuthoringController(IDraftSnapshotService drafts, IPreviewTokenService previewTokens)
    {
        _drafts = drafts;
        _previewTokens = previewTokens;
    }

    /// <summary>Editor JS POSTs every 30s while the user is typing. Upserts the snapshot.</summary>
    [HttpPost("autosave")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PostsEdit)]
    public async Task<IActionResult> Autosave([FromForm] AutosaveRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.EntityType) || req.EntityId == Guid.Empty)
            return FcmsFail("Missing entity reference.");
        var userId = FcmsContext.UserId;
        if (userId is null) return FcmsFail("Not signed in.");

        await _drafts.SaveAsync(req.EntityType, req.EntityId, userId.Value,
            new DraftSnapshotPayload(req.Title, req.Content, req.Excerpt), ct);
        return FcmsOk("Saved.", new { savedAt = DateTime.UtcNow });
    }

    /// <summary>Editor calls on page load — returns the latest snapshot if newer than the entity itself.</summary>
    [HttpGet("autosave/peek")]
    [FcmsAuthorize(FcmsPermissions.PostsEdit)]
    public async Task<IActionResult> PeekAutosave([FromQuery] string entityType, [FromQuery] Guid entityId, CancellationToken ct)
    {
        var userId = FcmsContext.UserId;
        if (userId is null) return FcmsFail("Not signed in.");
        var snap = await _drafts.GetAsync(entityType, entityId, userId.Value, ct);
        if (snap is null) return FcmsOk("None.", null);
        return FcmsOk("OK", new { snap.Title, snap.Content, snap.Excerpt, snap.CapturedAt });
    }

    /// <summary>Discard the autosave (call after a successful explicit save).</summary>
    [HttpPost("autosave/discard")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PostsEdit)]
    public async Task<IActionResult> DiscardAutosave([FromForm] string entityType, [FromForm] Guid entityId, CancellationToken ct)
    {
        var userId = FcmsContext.UserId;
        if (userId is null) return FcmsFail("Not signed in.");
        await _drafts.DiscardAsync(entityType, entityId, userId.Value, ct);
        return FcmsOk("Discarded.");
    }

    /// <summary>Issue a fresh preview token for an entity. Returns the share URL.</summary>
    [HttpPost("preview-token/issue")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PostsEdit)]
    public async Task<IActionResult> IssuePreviewToken([FromForm] string entityType, [FromForm] Guid entityId, [FromForm] string? slug, CancellationToken ct)
    {
        if (entityType is not (nameof(FcmsPage) or nameof(FcmsPost)))
            return FcmsFail("Unsupported entity type.");

        var token = await _previewTokens.IssueAsync(entityType, entityId, ct: ct);

        // Convenience — return the full sharable URL alongside the token
        // so the editor UI can copy-to-clipboard without a second concat.
        var basePath = entityType == nameof(FcmsPost)
            ? $"/blog/{Uri.EscapeDataString(slug ?? "")}"
            : $"/{Uri.EscapeDataString(slug ?? "")}";
        var shareUrl = $"{Request.Scheme}://{Request.Host}{basePath}?preview={token}";

        return FcmsOk("Token issued.", new { token, shareUrl });
    }

    [HttpPost("preview-token/revoke")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PostsEdit)]
    public async Task<IActionResult> RevokePreviewToken([FromForm] string entityType, [FromForm] Guid entityId, CancellationToken ct)
    {
        await _previewTokens.RevokeAsync(entityType, entityId, ct);
        return FcmsOk("Token revoked.");
    }

    public sealed class AutosaveRequest
    {
        public string EntityType { get; set; } = "";
        public Guid EntityId { get; set; }
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? Excerpt { get; set; }
    }
}
