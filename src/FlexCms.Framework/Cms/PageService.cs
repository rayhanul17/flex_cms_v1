using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms;

public class PageService : IPageService
{
    private readonly IRepository<FcmsPage> _repo;
    private readonly IRepository<FcmsPageTranslation> _trRepo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IFcmsLogService _audit;

    public PageService(
        IRepository<FcmsPage> repo,
        IRepository<FcmsPageTranslation> trRepo,
        IFcmsUnitOfWork uow,
        IFcmsLogService audit)
    {
        _repo = repo;
        _trRepo = trRepo;
        _uow = uow;
        _audit = audit;
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
        await _audit.LogAsync(FcmsAuditActions.PageCreated, nameof(FcmsPage), page.Id.ToString(),
            value: page, ct: ct);
        return page;
    }

    public async Task UpdateAsync(FcmsPage page, CancellationToken ct = default)
    {
        page.Content = HtmlSanitizer.Sanitize(page.Content);
        await _repo.UpdateAsync(page, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(FcmsAuditActions.PageUpdated, nameof(FcmsPage), page.Id.ToString(),
            value: page, ct: ct);
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
        await _audit.LogAsync(FcmsAuditActions.PageRestored, nameof(FcmsPage), id.ToString(), ct: ct);
    }

    public async Task HardDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _repo.FirstOrDefaultAsync(p => p.Id == id, ct, includeDeleted: true);
        if (page is null) return;
        // Log before delete — entity must exist in DB when log is written
        await _audit.LogAsync(FcmsAuditActions.PageHardDeleted, nameof(FcmsPage), id.ToString(),
            value: page, severity: FcmsLogSeverity.Warning, ct: ct);
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
        await _audit.LogAsync(FcmsAuditActions.PageDeleted, nameof(FcmsPage), id.ToString(),
            value: page, ct: ct);
    }

    // ── Translations (Phase 7) ───────────────────────────────────────────────

    public async Task<(FcmsPage Page, FcmsPageTranslation? Translation)?> ResolveBySlugAsync(
        string slug, string lang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var langNorm = (lang ?? "").ToLowerInvariant();

        // 1. Translation slug match — preferred path. Pull the translation, then
        //    its base page (no auto-include — Mongo + EfRepository.Find don't share
        //    Include semantics, so do two cheap lookups).
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

        // 2. Base slug match — return base content (translator falls back to default lang).
        var basePage = await _repo.FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (basePage is null) return null;

        // If a translation exists for the requested language, hand it back too —
        // controller may swap fields without losing routing context.
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
        await _audit.LogAsync(FcmsAuditActions.PageUpdated, nameof(FcmsPageTranslation),
            tr.Id.ToString(), value: tr, ct: ct);
        return tr;
    }

    public async Task DeleteTranslationAsync(Guid translationId, CancellationToken ct = default)
    {
        var tr = await _trRepo.GetByIdAsync(translationId, ct);
        if (tr is null) return;
        await _trRepo.DeleteAsync(tr, ct);   // hard delete — translations are not soft-deletable per UX (cheap to re-add)
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(FcmsAuditActions.PageDeleted, nameof(FcmsPageTranslation),
            translationId.ToString(), ct: ct);
    }
}
