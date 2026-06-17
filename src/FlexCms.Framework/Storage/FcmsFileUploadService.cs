using FlexCms.Framework.Modules.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace FlexCms.Framework.Storage;

[FcmsScoped]
public sealed class FcmsFileUploadService : IFcmsFileUploadService
{
    private readonly IFcmsFileStorage _storage;
    private readonly ILogger<FcmsFileUploadService> _logger;

    public FcmsFileUploadService(IFcmsFileStorage storage, ILogger<FcmsFileUploadService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public async Task<UploadResult> SaveAsync(IFormFile file, string folder, ImageCompressionOptions? compress = null, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new ArgumentException("Empty file.", nameof(file));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var safeBase = MakeSafeName(Path.GetFileNameWithoutExtension(file.FileName));
        var year = DateTime.UtcNow.Year;
        var month = DateTime.UtcNow.Month.ToString("D2");
        var unique = $"{Guid.NewGuid():N}_{safeBase}{ext}";
        var rel = $"{folder.Trim('/')}/{year}/{month}/{unique}";

        bool didCompress = false;
        int? width = null, height = null;
        long writtenSize = file.Length;
        string? contentType = file.ContentType;
        Stream payload;

        if (compress is not null
            && ImageExtensions.Contains(ext)
            && file.Length >= compress.SkipBelowBytes)
        {
            // Read into memory once, decode, resize, re-encode.
            using var src = new MemoryStream();
            await file.CopyToAsync(src, ct);
            src.Position = 0;

            try
            {
                using var bitmap = SKBitmap.Decode(src.ToArray());
                if (bitmap is not null)
                {
                    var scale = Math.Min(
                        (float)compress.MaxDimension / bitmap.Width,
                        (float)compress.MaxDimension / bitmap.Height);
                    if (scale > 1f) scale = 1f;

                    int targetW = (int)(bitmap.Width * scale);
                    int targetH = (int)(bitmap.Height * scale);

                    using var resized = scale < 1f
                        ? bitmap.Resize(new SKImageInfo(targetW, targetH), new SKSamplingOptions(SKFilterMode.Linear))
                        : bitmap;
                    if (resized is not null)
                    {
                        width = resized.Width;
                        height = resized.Height;

                        using var image = SKImage.FromBitmap(resized);
                        // JPEG yields the best ratio for photos; we keep the
                        // original extension so callers see the same URL shape.
                        using var data = image.Encode(SKEncodedImageFormat.Jpeg, compress.JpegQuality);
                        var ms = new MemoryStream();
                        data.SaveTo(ms);
                        ms.Position = 0;
                        payload = ms;
                        writtenSize = ms.Length;
                        contentType = "image/jpeg";
                        didCompress = true;
                    }
                    else
                    {
                        src.Position = 0;
                        payload = src;
                    }
                }
                else
                {
                    src.Position = 0;
                    payload = src;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Compression failed for {File}; saving original.", file.FileName);
                src.Position = 0;
                payload = src;
            }
        }
        else
        {
            payload = file.OpenReadStream();
        }

        var url = await _storage.SaveAsync(rel, payload, ct);
        await payload.DisposeAsync();

        return new UploadResult(
            RelativePath: rel,
            PublicUrl: url,
            FileName: file.FileName,
            ContentType: contentType,
            FileSize: writtenSize,
            Compressed: didCompress,
            Width: width,
            Height: height);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        => _storage.DeleteAsync(relativePath, ct);

    private static string MakeSafeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "file";
        var clean = new string(raw
            .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-')
            .ToArray());
        clean = clean.Trim('-');
        if (clean.Length > 60) clean = clean[..60];
        return string.IsNullOrEmpty(clean) ? "file" : clean.ToLowerInvariant();
    }
}
