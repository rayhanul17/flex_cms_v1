using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms;

public class PageService : IPageService
{
    private readonly IRepository<FcmsPage> _repo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IFcmsLogService _audit;

    public PageService(IRepository<FcmsPage> repo, IFcmsUnitOfWork uow, IFcmsLogService audit)
    {
        _repo = repo;
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
}
