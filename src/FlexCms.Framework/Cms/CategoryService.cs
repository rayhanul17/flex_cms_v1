using FlexCms.Framework.Clock;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Cms;

public class CategoryService : ICategoryService
{
    private readonly FcmsDbContext _db;

    public CategoryService(FcmsDbContext db) => _db = db;

    public Task<FcmsCategory?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Categories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

    public Task<FcmsCategory?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => _db.Categories.FirstOrDefaultAsync(c => c.Slug == slug && !c.IsDeleted, ct);

    public Task<List<FcmsCategory>> GetAllAsync(CancellationToken ct = default)
        => _db.Categories
            .Where(c => !c.IsDeleted)
            .Include(c => c.Posts.Where(p => !p.IsDeleted))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default)
        => _db.Categories.AnyAsync(c => !c.IsDeleted && c.Slug == slug && c.Id != (excludeId ?? Guid.Empty), ct);

    public async Task<FcmsCategory> CreateAsync(FcmsCategory category, CancellationToken ct = default)
    {
        category.CreatedAt = FcmsTime.Now;
        category.UpdatedAt = FcmsTime.Now;
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);
        return category;
    }

    public async Task UpdateAsync(FcmsCategory category, CancellationToken ct = default)
    {
        category.UpdatedAt = FcmsTime.Now;
        _db.Categories.Update(category);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
        if (category is null) return;
        category.IsDeleted = true;
        category.UpdatedAt = FcmsTime.Now;
        await _db.SaveChangesAsync(ct);
    }
}
