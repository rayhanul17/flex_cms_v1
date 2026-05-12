using FlexCms.Framework.Auth;
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
[Route("admin/comments")]
public class CommentController : BaseAdminController
{
    private readonly IRepository<FcmsComment> _repo;
    private readonly ICommentService _comments;
    private readonly IFcmsContextService _ctx;

    public CommentController(
        IRepository<FcmsComment> repo,
        ICommentService comments,
        IFcmsContextService ctx)
    {
        _repo = repo;
        _comments = comments;
        _ctx = ctx;
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
