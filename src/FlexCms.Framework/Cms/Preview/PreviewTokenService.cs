using System.Security.Cryptography;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms.Preview;

public sealed class PreviewTokenService : IPreviewTokenService
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    private readonly IRepository<FcmsPage> _pages;
    private readonly IRepository<FcmsPost> _posts;
    private readonly IFcmsUnitOfWork _uow;

    public PreviewTokenService(
        IRepository<FcmsPage> pages,
        IRepository<FcmsPost> posts,
        IFcmsUnitOfWork uow)
    {
        _pages = pages;
        _posts = posts;
        _uow = uow;
    }

    public async Task<string> IssueAsync(string entityType, Guid entityId, TimeSpan? lifetime = null, CancellationToken ct = default)
    {
        var token = GenerateToken();
        var expiresAt = FcmsTime.Now.Add(lifetime ?? DefaultLifetime);

        switch (entityType)
        {
            case nameof(FcmsPage):
                var page = await _pages.GetByIdAsync(entityId, ct);
                if (page is null) throw new InvalidOperationException("Page not found.");
                page.PreviewToken = token;
                page.PreviewTokenExpiresAt = expiresAt;
                await _pages.UpdateAsync(page, ct);
                break;
            case nameof(FcmsPost):
                var post = await _posts.GetByIdAsync(entityId, ct);
                if (post is null) throw new InvalidOperationException("Post not found.");
                post.PreviewToken = token;
                post.PreviewTokenExpiresAt = expiresAt;
                await _posts.UpdateAsync(post, ct);
                break;
            default:
                throw new NotSupportedException($"Preview tokens not supported for '{entityType}'.");
        }

        await _uow.SaveChangesAsync(ct);
        return token;
    }

    public async Task<bool> ValidateAsync(string entityType, Guid entityId, string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var (storedToken, expiresAt) = entityType switch
        {
            nameof(FcmsPage) => await GetPageTokenAsync(entityId, ct),
            nameof(FcmsPost) => await GetPostTokenAsync(entityId, ct),
            _ => (null, (DateTime?)null),
        };

        if (string.IsNullOrEmpty(storedToken)) return false;
        if (expiresAt.HasValue && expiresAt.Value <= FcmsTime.Now) return false;

        // Constant-time comparison so token brute-forcing can't lean on
        // string-comparison early-exit timing.
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(storedToken),
            System.Text.Encoding.UTF8.GetBytes(token));
    }

    public async Task RevokeAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        switch (entityType)
        {
            case nameof(FcmsPage):
                var page = await _pages.GetByIdAsync(entityId, ct);
                if (page is null) return;
                page.PreviewToken = null;
                page.PreviewTokenExpiresAt = null;
                await _pages.UpdateAsync(page, ct);
                break;
            case nameof(FcmsPost):
                var post = await _posts.GetByIdAsync(entityId, ct);
                if (post is null) return;
                post.PreviewToken = null;
                post.PreviewTokenExpiresAt = null;
                await _posts.UpdateAsync(post, ct);
                break;
        }
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<(string? Token, DateTime? ExpiresAt)> GetPageTokenAsync(Guid id, CancellationToken ct)
    {
        var page = await _pages.GetByIdAsync(id, ct);
        return (page?.PreviewToken, page?.PreviewTokenExpiresAt);
    }

    private async Task<(string? Token, DateTime? ExpiresAt)> GetPostTokenAsync(Guid id, CancellationToken ct)
    {
        var post = await _posts.GetByIdAsync(id, ct);
        return (post?.PreviewToken, post?.PreviewTokenExpiresAt);
    }

    /// <summary>
    /// 32 bytes = 256 bits of entropy → URL-safe base64 (no padding) ≈ 43 chars.
    /// Comfortable margin against brute force; URL-safe so it can drop into a
    /// query string without escaping.
    /// </summary>
    public static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
