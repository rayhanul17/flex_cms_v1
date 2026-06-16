using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Notifications;
using Microsoft.AspNetCore.Identity;

namespace FlexCms.Framework.Cms.Comments;

public interface ICommentService
{
    /// <summary>
    /// Submit a new comment. Runs the built-in spam filter (link count, common
    /// spam keywords) — comments scoring 5+ go straight to <see cref="CommentStatus.Spam"/>;
    /// otherwise they land in <see cref="CommentStatus.Pending"/> for moderation.
    /// </summary>
    Task<FcmsComment> SubmitAsync(FcmsComment comment, CancellationToken ct = default);

    /// <summary>List approved comments for the entity (frontend rendering). Top-level + nested via ParentId.</summary>
    Task<List<FcmsComment>> GetApprovedAsync(string entityType, Guid entityId, CancellationToken ct = default);

    /// <summary>Pending comments for the moderation queue, newest first.</summary>
    Task<List<FcmsComment>> GetPendingAsync(int max = 100, CancellationToken ct = default);

    Task SetStatusAsync(Guid commentId, CommentStatus newStatus, Guid? moderatorUserId, CancellationToken ct = default);
}

public sealed class CommentService : ICommentService
{
    private static readonly string[] SpamKeywords =
        ["viagra", "casino", "loans", "porn", "crypto", "lottery", "winner"];

    private readonly IRepository<FcmsComment> _repo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IFcmsNotificationService? _notifications;
    private readonly RoleManager<FcmsRole>? _roles;
    private readonly UserManager<FcmsUser>? _users;

    /// <summary>Test-friendly constructor — notifications optional.</summary>
    public CommentService(IRepository<FcmsComment> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    /// <summary>Production constructor — wires admin-comment notifications.</summary>
    public CommentService(
        IRepository<FcmsComment> repo,
        IFcmsUnitOfWork uow,
        IFcmsNotificationService notifications,
        RoleManager<FcmsRole> roles,
        UserManager<FcmsUser> users)
        : this(repo, uow)
    {
        _notifications = notifications;
        _roles = roles;
        _users = users;
    }

    public async Task<FcmsComment> SubmitAsync(FcmsComment comment, CancellationToken ct = default)
    {
        var (score, status) = ScoreSpam(comment);
        comment.SpamScore = score;
        comment.CommentStatus = status;

        await _repo.AddAsync(comment, ct);
        await _uow.SaveChangesAsync(ct);

        // Notify moderators on Pending — Spam-flagged comments stay quiet so
        // we don't drown admins in alerts every time a bot pings the form.
        if (comment.CommentStatus == CommentStatus.Pending)
            await NotifyModeratorsAsync(comment, ct);

        return comment;
    }

    private async Task NotifyModeratorsAsync(FcmsComment comment, CancellationToken ct)
    {
        if (_notifications is null || _users is null) return;
        try
        {
            // Notify SuperAdmin + Admin role members. comments.moderate
            // permission would be more precise but the role check covers the
            // typical out-of-the-box install.
            var admins = new HashSet<Guid>();
            foreach (var role in new[] { FcmsRoles.SuperAdmin, "Admin" })
            {
                foreach (var u in await _users.GetUsersInRoleAsync(role))
                    admins.Add(u.Id);
            }

            var preview = comment.Body.Length > 80 ? comment.Body[..80] + "…" : comment.Body;
            foreach (var adminId in admins)
            {
                await _notifications.NotifyUserAsync(
                    adminId,
                    title: "New comment awaiting moderation",
                    body: $"{(string.IsNullOrEmpty(comment.AuthorName) ? "Anonymous" : comment.AuthorName)}: {preview}",
                    level: NotificationLevel.Info,
                    url: "/blog/admin/comments",
                    icon: "bi bi-chat-left-text",
                    ct: ct);
            }
        }
        catch
        {
            // Notification failure must not roll back the comment submission —
            // the comment is already persisted by this point.
        }
    }

    public async Task<List<FcmsComment>> GetApprovedAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(c => c.EntityType == entityType && c.EntityId == entityId
            && c.CommentStatus == CommentStatus.Approved, ct);
        return rows.OrderBy(c => c.CreatedAt).ToList();
    }

    public async Task<List<FcmsComment>> GetPendingAsync(int max = 100, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(c => c.CommentStatus == CommentStatus.Pending, ct);
        return rows.OrderByDescending(c => c.CreatedAt).Take(max).ToList();
    }

    public async Task SetStatusAsync(Guid commentId, CommentStatus newStatus, Guid? moderatorUserId, CancellationToken ct = default)
    {
        var c = await _repo.GetByIdAsync(commentId, ct);
        if (c is null) return;
        c.CommentStatus = newStatus;
        c.ModeratedByUserId = moderatorUserId;
        c.ModeratedAt = Clock.FcmsTime.Now;
        await _repo.UpdateAsync(c, ct);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Cheap heuristic spam filter. Each rule contributes to a score; total
    /// >=5 → auto-spam. Tuned conservatively — borderline comments still land
    /// in Pending so moderators retain final say.
    /// </summary>
    public static (int Score, CommentStatus Status) ScoreSpam(FcmsComment c)
    {
        var body = c.Body ?? "";
        var score = 0;

        // Many links is the classic spam signal.
        var linkCount = System.Text.RegularExpressions.Regex.Matches(body, @"https?://", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        if (linkCount >= 6) score += 5;
        else if (linkCount >= 3) score += 2;

        // Spam keywords.
        var lower = body.ToLowerInvariant();
        foreach (var kw in SpamKeywords)
            if (lower.Contains(kw, StringComparison.Ordinal)) score += 2;

        // Excessive caps.
        var lettersOnly = new string(body.Where(char.IsLetter).ToArray());
        if (lettersOnly.Length > 30 && lettersOnly.Count(char.IsUpper) / (double)lettersOnly.Length > 0.7)
            score += 2;

        return (score, score >= 5 ? CommentStatus.Spam : CommentStatus.Pending);
    }
}
