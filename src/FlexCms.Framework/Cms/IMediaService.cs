using Microsoft.AspNetCore.Http;

namespace FlexCms.Framework.Cms;

public interface IMediaService
{
    Task<FcmsMedia> UploadAsync(IFormFile file, Guid? folderId, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
    Task<FcmsMedia?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FcmsMedia>> GetByFolderAsync(Guid? folderId, CancellationToken ct = default);
    Task MoveToFolderAsync(Guid mediaId, Guid? targetFolderId, CancellationToken ct = default);
}
