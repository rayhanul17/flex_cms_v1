using System.Text.Encodings.Web;
using System.Text.Json;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.History;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Cms.Comments;
using FlexCms.Framework.Db;
using FlexCms.Framework.Sessions;
using Microsoft.AspNetCore.Identity;

namespace FlexCms.Framework.Gdpr;

public sealed class FcmsGdprService : IFcmsGdprService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
    };

    private readonly UserManager<FcmsUser> _users;
    private readonly IRepository<FcmsPage> _pages;
    private readonly IRepository<FcmsPost> _posts;
    private readonly IRepository<FcmsComment> _comments;
    private readonly IRepository<FcmsUserSession> _sessions;
    private readonly IRepository<FcmsLoginHistory> _loginHistory;
    private readonly IFcmsUnitOfWork _uow;

    public FcmsGdprService(
        UserManager<FcmsUser> users,
        IRepository<FcmsPage> pages,
        IRepository<FcmsPost> posts,
        IRepository<FcmsComment> comments,
        IRepository<FcmsUserSession> sessions,
        IRepository<FcmsLoginHistory> loginHistory,
        IFcmsUnitOfWork uow)
    {
        _users = users;
        _pages = pages;
        _posts = posts;
        _comments = comments;
        _sessions = sessions;
        _loginHistory = loginHistory;
        _uow = uow;
    }

    public async Task<byte[]> ExportUserDataAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        var pages = await _pages.FindAsync(p => p.AuthorId == userId, ct);
        var posts = await _posts.FindAsync(p => p.AuthorId == userId, ct);
        var comments = await _comments.FindAsync(c => c.AuthorUserId == userId, ct);
        var sessions = await _sessions.FindAsync(s => s.UserId == userId, ct);
        var history = await _loginHistory.FindAsync(h => h.UserId == userId, ct);

        var bundle = new
        {
            exportedAt = DateTime.UtcNow,
            user = user is null ? null : new
            {
                user.Id,
                user.UserName,
                user.Email,
                user.PhoneNumber,
                user.EmailConfirmed,
                user.PhoneNumberConfirmed,
                user.TwoFactorEnabled,
                user.LockoutEnd,
                user.AccessFailedCount,
                user.CreatedAt,
                user.UpdatedAt,
            },
            pages = pages.Select(p => new { p.Id, p.Title, p.Slug, p.IsPublished, p.PublishedAt, p.CreatedAt, p.UpdatedAt }),
            posts = posts.Select(p => new { p.Id, p.Title, p.Slug, p.IsPublished, p.PublishedAt, p.CreatedAt, p.UpdatedAt }),
            comments = comments.Select(c => new { c.Id, c.EntityType, c.EntityId, c.Body, c.CommentStatus, c.CreatedAt }),
            sessions = sessions.Select(s => new { s.Id, s.SessionId, s.CreatedAt, s.LastSeenAt, s.IpAddress, s.UserAgent, s.IsRevoked }),
            loginHistory = history.Select(h => new { h.Id, h.CreatedAt, h.Outcome, h.IpAddress, h.UserAgent }),
        };

        return JsonSerializer.SerializeToUtf8Bytes(bundle, JsonOpts);
    }

    public async Task<DeleteAccountResult> DeleteAccountAsync(Guid userId, bool deleteOwnedContent, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return new DeleteAccountResult(false, 0, 0, "User not found.");

        // Anonymize PII — keep the row so FK references (post.AuthorId etc.)
        // remain valid. The "deleted-{guid}@example.invalid" pattern uses the
        // RFC-2606 reserved TLD so it can never resolve to a real address.
        user.UserName = $"deleted-{userId:N}";
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.Email = $"deleted-{userId:N}@example.invalid";
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        user.PhoneNumber = null;
        user.EmailConfirmed = false;
        user.PhoneNumberConfirmed = false;
        user.LockoutEnd = DateTimeOffset.MaxValue;   // no further login
        user.PasswordHash = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.Status = Db.EntityStatus.Deleted;
        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);

        // Revoke every active session — the security stamp change above also
        // invalidates cookie auth on next request, but explicit revocation
        // keeps the session table accurate.
        var sessions = await _sessions.FindAsync(s => s.UserId == userId && !s.IsRevoked, ct);
        var revoked = 0;
        foreach (var s in sessions)
        {
            s.IsRevoked = true;
            s.RevokedAt = DateTime.UtcNow;
            await _sessions.UpdateAsync(s, ct);
            revoked++;
        }

        var deletedContent = 0;
        if (deleteOwnedContent)
        {
            var pages = await _pages.FindAsync(p => p.AuthorId == userId, ct);
            foreach (var p in pages) { await _pages.SoftDeleteAsync(p, ct); deletedContent++; }
            var posts = await _posts.FindAsync(p => p.AuthorId == userId, ct);
            foreach (var p in posts) { await _posts.SoftDeleteAsync(p, ct); deletedContent++; }
            var comments = await _comments.FindAsync(c => c.AuthorUserId == userId, ct);
            foreach (var c in comments) { await _comments.SoftDeleteAsync(c, ct); deletedContent++; }
        }

        await _uow.SaveChangesAsync(ct);
        return new DeleteAccountResult(true, revoked, deletedContent);
    }
}
