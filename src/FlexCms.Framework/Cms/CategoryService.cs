using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms;

public class CategoryService : ICategoryService
{
    private readonly IRepository<FcmsCategory> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public CategoryService(IRepository<FcmsCategory> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public Task<FcmsCategory?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    public Task<FcmsCategory?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => _repo.FirstOrDefaultAsync(c => c.Slug == slug, ct);

    public Task<List<FcmsCategory>> GetAllAsync(CancellationToken ct = default)
        => _repo.FindAsync(c => true, ct);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default)
        => _repo.ExistsAsync(c => c.Slug == slug && c.Id != (excludeId ?? Guid.Empty), ct);

    public async Task<FcmsCategory> CreateAsync(FcmsCategory category, CancellationToken ct = default)
    {
        await _repo.AddAsync(category, ct);
        await _uow.SaveChangesAsync(ct);
        return category;
    }

    public async Task UpdateAsync(FcmsCategory category, CancellationToken ct = default)
    {
        await _repo.UpdateAsync(category, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var category = await _repo.GetByIdAsync(id, ct);
        if (category is null) return;
        await _repo.SoftDeleteAsync(category, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
