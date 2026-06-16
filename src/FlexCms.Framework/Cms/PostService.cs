using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms;

public class PostService : IPostService
{
    private readonly IRepository<FcmsPost> _postRepo;
    private readonly IRepository<FcmsTag> _tagRepo;
    private readonly IRepository<FcmsPostTag> _postTagRepo;
    private readonly IRepository<FcmsPostTranslation> _trRepo;
    private readonly IFcmsUnitOfWork _uow;

    public PostService(
        IRepository<FcmsPost> postRepo,
        IRepository<FcmsTag> tagRepo,
        IRepository<FcmsPostTag> postTagRepo,
        IRepository<FcmsPostTranslation> trRepo,
        IFcmsUnitOfWork uow)
    {
        _postRepo = postRepo;
        _tagRepo = tagRepo;
        _postTagRepo = postTagRepo;
        _trRepo = trRepo;
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
        => _postRepo.FindAsync(p => p.Status == EntityStatus.Deleted, ct, includeDeleted: true);

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
        post.Status = EntityStatus.Active;
        post.DeletedAt = null;
        post.IsPublished = false;
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


    public async Task<(FcmsPost Post, FcmsPostTranslation? Translation)?> ResolveBySlugAsync(
        string slug, string lang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var langNorm = (lang ?? "").ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(langNorm))
        {
            var tr = await _trRepo.FirstOrDefaultAsync(
                t => t.Slug == slug && t.LanguageCode == langNorm, ct);
            if (tr is not null)
            {
                var post = await _postRepo.GetByIdAsync(tr.PostId, ct);
                if (post is not null) return (post, tr);
            }
        }

        var basePost = await _postRepo.FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (basePost is null) return null;

        FcmsPostTranslation? maybeTr = null;
        if (!string.IsNullOrWhiteSpace(langNorm))
            maybeTr = await _trRepo.FirstOrDefaultAsync(
                t => t.PostId == basePost.Id && t.LanguageCode == langNorm, ct);

        return (basePost, maybeTr);
    }

    public Task<List<FcmsPostTranslation>> GetTranslationsAsync(Guid postId, CancellationToken ct = default)
        => _trRepo.FindAsync(t => t.PostId == postId, ct);

    public Task<FcmsPostTranslation?> GetTranslationAsync(Guid postId, string lang, CancellationToken ct = default)
    {
        var langNorm = (lang ?? "").ToLowerInvariant();
        return _trRepo.FirstOrDefaultAsync(t => t.PostId == postId && t.LanguageCode == langNorm, ct);
    }

    public async Task<FcmsPostTranslation> SaveTranslationAsync(FcmsPostTranslation tr, CancellationToken ct = default)
    {
        tr.LanguageCode = (tr.LanguageCode ?? "").ToLowerInvariant();
        tr.Content = HtmlSanitizer.Sanitize(tr.Content);

        var existing = await _trRepo.FirstOrDefaultAsync(
            t => t.PostId == tr.PostId && t.LanguageCode == tr.LanguageCode, ct);

        if (existing is null)
        {
            await _trRepo.AddAsync(tr, ct);
        }
        else
        {
            existing.Title = tr.Title;
            existing.Slug = tr.Slug;
            existing.Excerpt = tr.Excerpt;
            existing.Content = tr.Content;
            existing.MetaTitle = tr.MetaTitle;
            existing.MetaDescription = tr.MetaDescription;
            await _trRepo.UpdateAsync(existing, ct);
            tr = existing;
        }

        await _uow.SaveChangesAsync(ct);
        return tr;
    }

    public async Task DeleteTranslationAsync(Guid translationId, CancellationToken ct = default)
    {
        var tr = await _trRepo.GetByIdAsync(translationId, ct);
        if (tr is null) return;
        await _trRepo.DeleteAsync(tr, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
