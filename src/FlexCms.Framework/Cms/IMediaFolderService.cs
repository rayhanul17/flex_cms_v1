namespace FlexCms.Framework.Cms;

public interface IMediaFolderService
{
    Task<FcmsMediaFolder> CreateAsync(string name, Guid? parentId, CancellationToken ct = default);
    Task<FcmsMediaFolder> RenameAsync(Guid id, string newName, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FcmsMediaFolder>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FcmsMediaFolder>> GetBreadcrumbAsync(Guid folderId, CancellationToken ct = default);
}
