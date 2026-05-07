using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms;

public class PageService : IPageService
{
    private readonly IRepository<FcmsPage> _repo;
    private readonly IRepository<FcmsPageTranslation> _trRepo;
    private readonly IFcmsUnitOfWork _uow;

    public PageService(
        IRepository<FcmsPage> repo,
        IRepository<FcmsPageTranslation> trRepo,
        IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _trRepo = trRepo;
        _uow = uow;
    }

    public Task<FcmsPage?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    public Task<FcmsPage?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => _repo.FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public Task<List<FcmsPage>> GetAllAsync(CancellationToken ct = default)
        => _repo.FindAsync(p => true, ct);

    public Task<List<FcmsPage>> GetPublishedAsync(CancellationToken ct = default)
        => _repo.FindAsync(p => p.IsPublished, ct);

    public Task<List<FcmsPage>> GetChildrenAsync(Guid? parentId, CancellationToken ct = default)
        => _repo.FindAsync(p => p.ParentId == parentId, ct);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default)
        => _repo.ExistsAsync(p => p.Slug == slug && p.Id != (excludeId ?? Guid.Empty), ct);

    public Task<List<FcmsPage>> GetDeletedAsync(CancellationToken ct = default)
        => _repo.FindAsync(p => p.Status == EntityStatus.Deleted, ct, includeDeleted: true);

    public async Task<FcmsPage> CreateAsync(FcmsPage page, CancellationToken ct = default)
    {
        page.Content = HtmlSanitizer.Sanitize(page.Content);
        await _repo.AddAsync(page, ct);
        await _uow.SaveChangesAsync(ct);
        return page;
    }

    public async Task UpdateAsync(FcmsPage page, CancellationToken ct = default)
    {
        page.Content = HtmlSanitizer.Sanitize(page.Content);
        await _repo.UpdateAsync(page, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _repo.FirstOrDefaultAsync(p => p.Id == id, ct, includeDeleted: true);
        if (page is null) return;
        page.Status = EntityStatus.Active;
        page.DeletedAt = null;
        page.IsPublished = false; // always restore as draft
        await _repo.UpdateAsync(page, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task HardDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _repo.FirstOrDefaultAsync(p => p.Id == id, ct, includeDeleted: true);
        if (page is null) return;
        await _repo.DeleteAsync(page, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _repo.GetByIdAsync(id, ct);
        if (page is null) return;
        page.DeletedAt = Clock.FcmsTime.Now;
        await _repo.SoftDeleteAsync(page, ct);
        await _uow.SaveChangesAsync(ct);
    }

    // ── Translations (Phase 7) ───────────────────────────────────────────────

    public async Task<(FcmsPage Page, FcmsPageTranslation? Translation)?> ResolveBySlugAsync(
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
                var page = await _repo.GetByIdAsync(tr.PageId, ct);
                if (page is not null) return (page, tr);
            }
        }

        var basePage = await _repo.FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (basePage is null) return null;

        FcmsPageTranslation? maybeTr = null;
        if (!string.IsNullOrWhiteSpace(langNorm))
            maybeTr = await _trRepo.FirstOrDefaultAsync(
                t => t.PageId == basePage.Id && t.LanguageCode == langNorm, ct);

        return (basePage, maybeTr);
    }

    public Task<List<FcmsPageTranslation>> GetTranslationsAsync(Guid pageId, CancellationToken ct = default)
        => _trRepo.FindAsync(t => t.PageId == pageId, ct);

    public Task<FcmsPageTranslation?> GetTranslationAsync(Guid pageId, string lang, CancellationToken ct = default)
    {
        var langNorm = (lang ?? "").ToLowerInvariant();
        return _trRepo.FirstOrDefaultAsync(t => t.PageId == pageId && t.LanguageCode == langNorm, ct);
    }

    public async Task<FcmsPageTranslation> SaveTranslationAsync(FcmsPageTranslation tr, CancellationToken ct = default)
    {
        tr.LanguageCode = (tr.LanguageCode ?? "").ToLowerInvariant();
        tr.Content = HtmlSanitizer.Sanitize(tr.Content);

        var existing = await _trRepo.FirstOrDefaultAsync(
            t => t.PageId == tr.PageId && t.LanguageCode == tr.LanguageCode, ct);

        if (existing is null)
        {
            await _trRepo.AddAsync(tr, ct);
        }
        else
        {
            existing.Title = tr.Title;
            existing.Slug = tr.Slug;
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
