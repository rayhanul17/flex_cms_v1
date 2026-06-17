using Microsoft.AspNetCore.Http;

namespace FlexCms.Framework.Storage;

/// <summary>
/// Options that control how an image is compressed before it lands on disk.
/// Pass <c>null</c> to <see cref="IFcmsFileUploadService.SaveAsync"/> to skip
/// compression entirely (PDFs, ZIPs, etc. always skip regardless).
/// </summary>
public sealed class ImageCompressionOptions
{
    /// <summary>Resize so the longer edge does not exceed this many pixels. Default 1920.</summary>
    public int MaxDimension { get; init; } = 1920;

    /// <summary>JPEG encode quality, 1..100. Default 85.</summary>
    public int JpegQuality { get; init; } = 85;

    /// <summary>Files already smaller than this byte threshold skip compression — they're already small enough.</summary>
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
/// Module-friendly upload helper. Modules use this instead of writing their
/// own SkiaSharp + Path.Combine code. Wraps <see cref="IFcmsFileStorage"/>
/// with mime-aware compression, magic-byte validation, and a stable
/// "folder/yyyy/MM/{guid}_{original}.ext" layout.
/// </summary>
public interface IFcmsFileUploadService
{
    /// <summary>
    /// Save an uploaded file under <paramref name="folder"/> (e.g.
    /// <c>"investpro/partners"</c>). Returns the on-disk + url info that
    /// the caller stores alongside its own entity (Attachment row etc.).
    /// </summary>
    Task<UploadResult> SaveAsync(IFormFile file, string folder, ImageCompressionOptions? compress = null, CancellationToken ct = default);

    /// <summary>Delete a previously-saved file by relative path. No-op if missing.</summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
}
