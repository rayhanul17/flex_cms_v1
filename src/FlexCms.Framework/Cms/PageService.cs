using FlexCms.Framework.Clock;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Cms;

public class PageService : IPageService
{
    private readonly FcmsDbContext _db;

    public PageService(FcmsDbContext db) => _db = db;

    public Task<FcmsPage?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Pages.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);

    public Task<FcmsPage?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => _db.Pages.FirstOrDefaultAsync(p => p.Slug == slug && !p.IsDeleted, ct);

    public Task<List<FcmsPage>> GetAllAsync(CancellationToken ct = default)
        => _db.Pages.Where(p => !p.IsDeleted).OrderBy(p => p.SortOrder).ThenBy(p => p.Title).ToListAsync(ct);

    public Task<List<FcmsPage>> GetPublishedAsync(CancellationToken ct = default)
        => _db.Pages.Where(p => !p.IsDeleted && p.IsPublished).OrderBy(p => p.SortOrder).ThenBy(p => p.Title).ToListAsync(ct);

    public Task<List<FcmsPage>> GetChildrenAsync(Guid? parentId, CancellationToken ct = default)
        => _db.Pages.Where(p => !p.IsDeleted && p.ParentId == parentId).OrderBy(p => p.SortOrder).ThenBy(p => p.Title).ToListAsync(ct);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default)
        => _db.Pages.AnyAsync(p => !p.IsDeleted && p.Slug == slug && p.Id != (excludeId ?? Guid.Empty), ct);

    public async Task<FcmsPage> CreateAsync(FcmsPage page, CancellationToken ct = default)
    {
        page.Content = HtmlSanitizer.Sanitize(page.Content);
        page.CreatedAt = FcmsTime.Now;
        page.UpdatedAt = FcmsTime.Now;
        _db.Pages.Add(page);
        await _db.SaveChangesAsync(ct);
        return page;
    }

    public async Task UpdateAsync(FcmsPage page, CancellationToken ct = default)
    {
        page.Content = HtmlSanitizer.Sanitize(page.Content);
        page.UpdatedAt = FcmsTime.Now;
        _db.Pages.Update(page);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<FcmsPage>> GetDeletedAsync(CancellationToken ct = default)
        => _db.Pages.IgnoreQueryFilters().Where(p => p.IsDeleted).OrderByDescending(p => p.DeletedAt).ToListAsync(ct);

    public async Task RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id && p.IsDeleted, ct);
        if (page is null) return;
        page.IsDeleted = false;
        page.DeletedAt = null;
        page.IsPublished = false;
        page.UpdatedAt = FcmsTime.Now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task HardDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return;
        _db.Pages.Remove(page);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (page is null) return;
        page.IsDeleted = true;
        page.DeletedAt = FcmsTime.Now;
        page.UpdatedAt = FcmsTime.Now;
        await _db.SaveChangesAsync(ct);
    }
}
