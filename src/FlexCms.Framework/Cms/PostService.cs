using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms;

public class PostService : IPostService
{
    private readonly IRepository<FcmsPost> _postRepo;
    private readonly IRepository<FcmsTag> _tagRepo;
    private readonly IRepository<FcmsPostTag> _postTagRepo;
    private readonly IFcmsUnitOfWork _uow;

    public PostService(
        IRepository<FcmsPost> postRepo,
        IRepository<FcmsTag> tagRepo,
        IRepository<FcmsPostTag> postTagRepo,
        IFcmsUnitOfWork uow)
    {
        _postRepo = postRepo;
        _tagRepo = tagRepo;
        _postTagRepo = postTagRepo;
        _uow = uow;
    }

    public Task<FcmsPost?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _postRepo.GetByIdAsync(id, ct);

    public Task<FcmsPost?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => _postRepo.FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public Task<List<FcmsPost>> GetAllAsync(CancellationToken ct = default)
        => _postRepo.FindAsync(p => true, ct);

    public Task<List<FcmsPost>> GetPublishedAsync(CancellationToken ct = default)
        => _postRepo.FindAsync(p => p.IsPublished, ct);

    public Task<List<FcmsPost>> GetByCategoryAsync(Guid categoryId, CancellationToken ct = default)
        => _postRepo.FindAsync(p => p.IsPublished && p.CategoryId == categoryId, ct);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default)
        => _postRepo.ExistsAsync(p => p.Slug == slug && p.Id != (excludeId ?? Guid.Empty), ct);

    public Task<List<FcmsPost>> GetDeletedAsync(CancellationToken ct = default)
        => _postRepo.FindAsync(p => p.IsDeleted, ct, includeDeleted: true);

    public async Task<FcmsPost> CreateAsync(FcmsPost post, IEnumerable<string> tagSlugs, CancellationToken ct = default)
    {
        post.Content = HtmlSanitizer.Sanitize(post.Content);
        await _postRepo.AddAsync(post, ct);
        await _uow.SaveChangesAsync(ct);
        await SyncTagsAsync(post.Id, tagSlugs, ct);
        return post;
    }

    public async Task UpdateAsync(FcmsPost post, IEnumerable<string> tagSlugs, CancellationToken ct = default)
    {
        post.Content = HtmlSanitizer.Sanitize(post.Content);
        await _postRepo.UpdateAsync(post, ct);
        await _uow.SaveChangesAsync(ct);
        await SyncTagsAsync(post.Id, tagSlugs, ct);
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _postRepo.FirstOrDefaultAsync(p => p.Id == id, ct, includeDeleted: true);
        if (post is null) return;
        post.IsDeleted = false;
        post.DeletedAt = null;
        post.IsPublished = false; // always restore as draft
        await _postRepo.UpdateAsync(post, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task HardDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _postRepo.FirstOrDefaultAsync(p => p.Id == id, ct, includeDeleted: true);
        if (post is null) return;

        var tags = await _postTagRepo.FindAsync(pt => pt.PostId == id, ct, includeDeleted: true);
        foreach (var tag in tags) await _postTagRepo.DeleteAsync(tag, ct);

        await _postRepo.DeleteAsync(post, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _postRepo.GetByIdAsync(id, ct);
        if (post is null) return;
        post.DeletedAt = Clock.FcmsTime.Now;
        await _postRepo.SoftDeleteAsync(post, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<string>> GetTagSlugsAsync(Guid postId, CancellationToken ct = default)
    {
        var postTags = await _postTagRepo.FindAsync(pt => pt.PostId == postId, ct);
        if (postTags.Count == 0) return [];
        var tagIds = postTags.Select(pt => pt.TagId).ToList();
        var tags = await _tagRepo.GetByIdsAsync(tagIds, ct);
        return tags.Select(t => t.Slug).ToList();
    }

    public async Task IncrementViewCountAsync(Guid id, CancellationToken ct = default)
    {
        var post = await _postRepo.GetByIdAsync(id, ct);
        if (post is null) return;
        post.ViewCount++;
        await _postRepo.UpdateAsync(post, ct);
        await _uow.SaveChangesAsync(ct);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task SyncTagsAsync(Guid postId, IEnumerable<string> tagSlugs, CancellationToken ct)
    {
        var slugList = tagSlugs.Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList();

        var existing = await _postTagRepo.FindAsync(pt => pt.PostId == postId, ct);
        foreach (var e in existing) await _postTagRepo.DeleteAsync(e, ct);

        if (slugList.Count == 0)
        {
            await _uow.SaveChangesAsync(ct);
            return;
        }

        var existingTags = await _tagRepo.FindAsync(t => slugList.Contains(t.Slug), ct);

        var missingSlugs = slugList.Except(existingTags.Select(t => t.Slug)).ToList();
        foreach (var slug in missingSlugs)
        {
            var tag = new FcmsTag { Slug = slug, Name = ToTitleCase(slug) };
            await _tagRepo.AddAsync(tag, ct);
            existingTags.Add(tag);
        }

        foreach (var tag in existingTags)
            await _postTagRepo.AddAsync(new FcmsPostTag { PostId = postId, TagId = tag.Id }, ct);

        await _uow.SaveChangesAsync(ct);
    }

    private static string ToTitleCase(string slug)
        => string.Join(' ', slug.Split('-').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
}
