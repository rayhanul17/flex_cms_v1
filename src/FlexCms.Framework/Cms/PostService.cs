using FlexCms.Framework.Clock;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Cms;

public class PostService : IPostService
{
    private readonly FcmsDbContext _db;

    public PostService(FcmsDbContext db) => _db = db;

    public Task<FcmsPost?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Posts
            .Include(p => p.Category)
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);

    public Task<FcmsPost?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => _db.Posts
            .Include(p => p.Category)
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .FirstOrDefaultAsync(p => p.Slug == slug && !p.IsDeleted, ct);

    public Task<List<FcmsPost>> GetAllAsync(CancellationToken ct = default)
        => _db.Posts
            .Where(p => !p.IsDeleted)
            .Include(p => p.Category)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public Task<List<FcmsPost>> GetPublishedAsync(CancellationToken ct = default)
        => _db.Posts
            .Where(p => !p.IsDeleted && p.IsPublished)
            .Include(p => p.Category)
            .OrderByDescending(p => p.PublishedAt)
            .ToListAsync(ct);

    public Task<List<FcmsPost>> GetByCategoryAsync(Guid categoryId, CancellationToken ct = default)
        => _db.Posts
            .Where(p => !p.IsDeleted && p.IsPublished && p.CategoryId == categoryId)
            .OrderByDescending(p => p.PublishedAt)
            .ToListAsync(ct);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default)
        => _db.Posts.AnyAsync(p => !p.IsDeleted && p.Slug == slug && p.Id != (excludeId ?? Guid.Empty), ct);

    public async Task<FcmsPost> CreateAsync(FcmsPost post, IEnumerable<string> tagSlugs, CancellationToken ct = default)
    {
        post.Content = HtmlSanitizer.Sanitize(post.Content);
        post.CreatedAt = FcmsTime.Now;
        post.UpdatedAt = FcmsTime.Now;
        _db.Posts.Add(post);
        await _db.SaveChangesAsync(ct);
        await SyncTagsAsync(post.Id, tagSlugs, ct);
        return post;
    }

    public async Task UpdateAsync(FcmsPost post, IEnumerable<string> tagSlugs, CancellationToken ct = default)
    {
        post.Content = HtmlSanitizer.Sanitize(post.Content);
        post.UpdatedAt = FcmsTime.Now;
        _db.Posts.Update(post);
        await _db.SaveChangesAsync(ct);
        await SyncTagsAsync(post.Id, tagSlugs, ct);
    }

    public Task<List<FcmsPost>> GetDeletedAsync(CancellationToken ct = default)
        => _db.Posts.IgnoreQueryFilters()
            .Where(p => p.IsDeleted)
            .Include(p => p.Category)
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync(ct);

    public async Task RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _db.Posts.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id && p.IsDeleted, ct);
        if (post is null) return;
        post.IsDeleted = false;
        post.DeletedAt = null;
        post.IsPublished = false;
        post.UpdatedAt = FcmsTime.Now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task HardDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _db.Posts.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return;
        // Remove PostTags first (no cascade from Post → PostTag in our config)
        var tags = await _db.PostTags.Where(pt => pt.PostId == id).ToListAsync(ct);
        _db.PostTags.RemoveRange(tags);
        _db.Posts.Remove(post);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (post is null) return;
        post.IsDeleted = true;
        post.DeletedAt = FcmsTime.Now;
        post.UpdatedAt = FcmsTime.Now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task IncrementViewCountAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return;
        post.ViewCount++;
        await _db.SaveChangesAsync(ct);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task SyncTagsAsync(Guid postId, IEnumerable<string> tagSlugs, CancellationToken ct)
    {
        var slugList = tagSlugs.Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList();

        // Remove existing PostTags
        var existing = await _db.PostTags.Where(pt => pt.PostId == postId).ToListAsync(ct);
        _db.PostTags.RemoveRange(existing);

        if (slugList.Count == 0)
        {
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Resolve or create tags
        var existingTags = await _db.Tags
            .Where(t => slugList.Contains(t.Slug) && !t.IsDeleted)
            .ToListAsync(ct);

        var missingSlugs = slugList.Except(existingTags.Select(t => t.Slug)).ToList();
        foreach (var slug in missingSlugs)
        {
            var tag = new FcmsTag
            {
                Slug = slug,
                Name = ToTitleCase(slug),
                CreatedAt = FcmsTime.Now,
                UpdatedAt = FcmsTime.Now
            };
            _db.Tags.Add(tag);
            existingTags.Add(tag);
        }

        await _db.SaveChangesAsync(ct);

        // Create new PostTags
        var postTags = existingTags.Select(t => new FcmsPostTag { PostId = postId, TagId = t.Id });
        _db.PostTags.AddRange(postTags);
        await _db.SaveChangesAsync(ct);
    }

    private static string ToTitleCase(string slug)
        => string.Join(' ', slug.Split('-').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
}
