namespace FlexCms.Framework.Cms;

public interface IPageService
{
    Task<FcmsPage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FcmsPage?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<FcmsPage>> GetAllAsync(CancellationToken ct = default);
    Task<List<FcmsPage>> GetPublishedAsync(CancellationToken ct = default);
    Task<List<FcmsPage>> GetChildrenAsync(Guid? parentId, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default);
    Task<FcmsPage> CreateAsync(FcmsPage page, CancellationToken ct = default);
    Task UpdateAsync(FcmsPage page, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<List<FcmsPage>> GetDeletedAsync(CancellationToken ct = default);
    Task RestoreAsync(Guid id, CancellationToken ct = default);
    Task HardDeleteAsync(Guid id, CancellationToken ct = default);
}
