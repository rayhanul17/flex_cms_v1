namespace FlexCms.Framework.Cms;

public interface IPostService
{
    Task<FcmsPost?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FcmsPost?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<FcmsPost>> GetAllAsync(CancellationToken ct = default);
    Task<List<FcmsPost>> GetPublishedAsync(CancellationToken ct = default);
    Task<List<FcmsPost>> GetByCategoryAsync(Guid categoryId, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default);
    Task<FcmsPost> CreateAsync(FcmsPost post, IEnumerable<string> tagSlugs, CancellationToken ct = default);
    Task UpdateAsync(FcmsPost post, IEnumerable<string> tagSlugs, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task IncrementViewCountAsync(Guid id, CancellationToken ct = default);
}
