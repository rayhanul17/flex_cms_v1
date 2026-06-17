using Microsoft.AspNetCore.Http;

namespace FlexCms.Framework.Storage;

public sealed class ImageCompressionOptions
{
    public int MaxDimension { get; init; } = 1920;
    public int JpegQuality { get; init; } = 85;
    public long SkipBelowBytes { get; init; } = 500 * 1024;
}

public sealed record UploadResult(
    string RelativePath,
    string PublicUrl,
    string FileName,
    string? ContentType,
    long FileSize,
    bool Compressed,
    int? Width,
    int? Height
);

/// <summary>
/// Module-friendly upload helper. <paramref name="moduleId"/> routes the
/// file into that module's own <c>wwwroot/uploads/</c> folder (resolved by
/// <see cref="IFcmsModuleStorageResolver"/>). Pass <c>null</c> to land in
/// the host's own <c>wwwroot/uploads/</c> (e.g. the media library).
/// </summary>
public interface IFcmsFileUploadService
{
    Task<UploadResult> SaveAsync(
        IFormFile file,
        string? moduleId,
        string subfolder,
        ImageCompressionOptions? compress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a previously-saved file. <paramref name="moduleId"/> must
    /// match the one passed to SaveAsync so the resolver picks the right
    /// storage root.
    /// </summary>
    Task DeleteAsync(string? moduleId, string publicUrl, CancellationToken ct = default);
}
