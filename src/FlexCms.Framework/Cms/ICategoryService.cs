namespace FlexCms.Framework.Cms;

public interface ICategoryService
{
    Task<FcmsCategory?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FcmsCategory?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<FcmsCategory>> GetAllAsync(CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default);
    Task<FcmsCategory> CreateAsync(FcmsCategory category, CancellationToken ct = default);
    Task UpdateAsync(FcmsCategory category, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns the number of published posts in a category without navigation-property loading.</summary>
    Task<int> GetPostCountAsync(Guid categoryId, CancellationToken ct = default);
}
