using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Cms.Comments;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

/// <summary>
/// Comment moderation admin: list / filter by status, approve, mark spam,
/// trash. The frontend submission form lives elsewhere — this controller is
/// purely for moderators.
/// </summary>
[Route("blog/admin/comments")]
public class CommentController : BaseAdminController
{
    private readonly IRepository<FcmsComment> _repo;
    private readonly IRepository<FcmsPost> _posts;
    private readonly IRepository<FcmsPage> _pages;
    private readonly ICommentService _comments;
    private readonly IFcmsContextService _ctx;
    private readonly ISettingsService _settings;

    public CommentController(
        IRepository<FcmsComment> repo,
        IRepository<FcmsPost> posts,
        IRepository<FcmsPage> pages,
        ICommentService comments,
        IFcmsContextService ctx,
        ISettingsService settings)
    {
        _repo = repo;
        _posts = posts;
        _pages = pages;
        _comments = comments;
        _ctx = ctx;
        _settings = settings;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.CommentsModerate)]
    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        var requestedStatus = ParseStatus(status);
        var all = await _repo.GetAllAsync(ct);
        var rows = (requestedStatus is null
                ? all
                : all.Where(c => c.CommentStatus == requestedStatus.Value))
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        // Resolve parent entities once for the rendered slice — pull titles
        // and public URLs so moderators see "Reply to: <Post Title> →"
        // instead of an opaque 8-character GUID stump.
        var postIds = rows.Where(c => c.EntityType == nameof(FcmsPost)).Select(c => c.EntityId).Distinct().ToList();
        var pageIds = rows.Where(c => c.EntityType == nameof(FcmsPage)).Select(c => c.EntityId).Distinct().ToList();

        var postLookup = postIds.Count == 0
            ? new Dictionary<Guid, (string Title, string Slug)>()
            : (await _posts.GetByIdsAsync(postIds, ct))
                .ToDictionary(p => p.Id, p => (p.Title, p.Slug));
        var pageLookup = pageIds.Count == 0
            ? new Dictionary<Guid, (string Title, string Slug)>()
            : (await _pages.GetByIdsAsync(pageIds, ct))
                .ToDictionary(p => p.Id, p => (p.Title, p.Slug));

        string baseUrl = "";
        try
        {
            var snap = await _settings.GetAsync<SiteIdentitySnapshot>("site:general", ct: ct);
            baseUrl = (snap?.BaseUrl ?? "").TrimEnd('/');
        }
        catch { /* settings unavailable — leave baseUrl empty, links become relative */ }

        ViewBag.PostLookup = postLookup;
        ViewBag.PageLookup = pageLookup;
        ViewBag.BaseUrl = baseUrl;
        ViewBag.SelectedStatus = requestedStatus;
        ViewBag.Counts = new Dictionary<CommentStatus, int>
        {
            [CommentStatus.Pending] = all.Count(c => c.CommentStatus == CommentStatus.Pending),
            [CommentStatus.Approved] = all.Count(c => c.CommentStatus == CommentStatus.Approved),
            [CommentStatus.Spam] = all.Count(c => c.CommentStatus == CommentStatus.Spam),
            [CommentStatus.Trashed] = all.Count(c => c.CommentStatus == CommentStatus.Trashed),
        };
        return View(rows);
    }

    [HttpPost("{id:guid}/approve")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.CommentsModerate)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await _comments.SetStatusAsync(id, CommentStatus.Approved, _ctx.UserId, ct);
        return FcmsOk("Approved.");
    }

    [HttpPost("{id:guid}/spam")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.CommentsModerate)]
    public async Task<IActionResult> MarkSpam(Guid id, CancellationToken ct)
    {
        await _comments.SetStatusAsync(id, CommentStatus.Spam, _ctx.UserId, ct);
        return FcmsOk("Marked as spam.");
    }

    [HttpPost("{id:guid}/trash")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.CommentsModerate)]
    public async Task<IActionResult> Trash(Guid id, CancellationToken ct)
    {
        await _comments.SetStatusAsync(id, CommentStatus.Trashed, _ctx.UserId, ct);
        return FcmsOk("Moved to trash.");
    }

    [HttpPost("{id:guid}/restore")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.CommentsModerate)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await _comments.SetStatusAsync(id, CommentStatus.Pending, _ctx.UserId, ct);
        return FcmsOk("Restored to pending.");
    }

    private static CommentStatus? ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return CommentStatus.Pending; // default tab
        return Enum.TryParse<CommentStatus>(raw, ignoreCase: true, out var v) ? v : null;
    }
}
