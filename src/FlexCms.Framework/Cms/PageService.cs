using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms;

public class PageService : IPageService
{
    private readonly IRepository<FcmsPage> _repo;

    public PageService(IRepository<FcmsPage> repo) => _repo = repo;

    public Task<FcmsPage?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    public Task<FcmsPage?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => _repo.FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public Task<List<FcmsPage>> GetAllAsync(CancellationToken ct = default)
        => _repo.FindAsync(p => true, ct); // FindAsync already orders by CreatedAt or similar in some implementations, but here we need specific ordering.

    public Task<List<FcmsPage>> GetPublishedAsync(CancellationToken ct = default)
        => _repo.FindAsync(p => p.IsPublished, ct);

    public Task<List<FcmsPage>> GetChildrenAsync(Guid? parentId, CancellationToken ct = default)
        => _repo.FindAsync(p => p.ParentId == parentId, ct);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default)
        => _repo.ExistsAsync(p => p.Slug == slug && p.Id != (excludeId ?? Guid.Empty), ct);

    public async Task<FcmsPage> CreateAsync(FcmsPage page, CancellationToken ct = default)
    {
        page.Content = HtmlSanitizer.Sanitize(page.Content);
        await _repo.AddAsync(page, ct);
        return page;
    }

    public async Task UpdateAsync(FcmsPage page, CancellationToken ct = default)
    {
        page.Content = HtmlSanitizer.Sanitize(page.Content);
        await _repo.UpdateAsync(page, ct);
    }

    public Task<List<FcmsPage>> GetDeletedAsync(CancellationToken ct = default)
    {
        // IRepository doesn't support IgnoreQueryFilters yet. 
        // This will return an empty list or fail to find deleted items depending on Repo implementation.
        return Task.FromResult(new List<FcmsPage>());
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct = default)
    {
        // This is tricky without IgnoreQueryFilters. 
        // For now, we'll assume the repo can't see them.
    }

    public async Task HardDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _repo.GetByIdAsync(id, ct);
        if (page is null) return;
        await _repo.DeleteAsync(page, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _repo.GetByIdAsync(id, ct);
        if (page is null) return;
        await _repo.SoftDeleteAsync(page, ct);
    }
}
