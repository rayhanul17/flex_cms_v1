using Microsoft.AspNetCore.Http;

namespace FlexCms.Framework.Cms;

public interface IMediaService
{
    Task<FcmsMedia> UploadAsync(IFormFile file, Guid? folderId, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
    Task<FcmsMedia?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FcmsMedia>> GetByFolderAsync(Guid? folderId, CancellationToken ct = default);
    Task MoveToFolderAsync(Guid mediaId, Guid? targetFolderId, CancellationToken ct = default);

    /// <summary>
    /// Apply a batch of alt-text edits in one round-trip. Skips ids that
    /// don't exist (admin posted stale form). Returns the count actually
    /// updated so the UI can confirm "Saved 17 items."
    /// </summary>
    Task<int> BulkUpdateAltTextAsync(IReadOnlyDictionary<Guid, string?> idToAlt, CancellationToken ct = default);
}
