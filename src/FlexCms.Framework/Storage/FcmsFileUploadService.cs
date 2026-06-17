using FlexCms.Framework.Modules.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace FlexCms.Framework.Storage;

[FcmsScoped(typeof(IFcmsFileUploadService))]
public sealed class FcmsFileUploadService : IFcmsFileUploadService
{
    private readonly IFcmsModuleStorageResolver _resolver;
    private readonly ILogger<FcmsFileUploadService> _logger;

    public FcmsFileUploadService(IFcmsModuleStorageResolver resolver, ILogger<FcmsFileUploadService> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public async Task<UploadResult> SaveAsync(
        IFormFile file,
        string? moduleId,
        string subfolder,
        ImageCompressionOptions? compress = null,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new ArgumentException("Empty file.", nameof(file));

        var target = _resolver.Resolve(moduleId);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var safeBase = MakeSafeName(Path.GetFileNameWithoutExtension(file.FileName));
        var year = DateTime.UtcNow.Year;
        var month = DateTime.UtcNow.Month.ToString("D2");
        var unique = $"{Guid.NewGuid():N}_{safeBase}{ext}";

        var cleanSub = (subfolder ?? string.Empty).Trim('/');
        var relativeUnderUploads = string.IsNullOrEmpty(cleanSub)
            ? $"{year}/{month}/{unique}"
            : $"{cleanSub}/{year}/{month}/{unique}";

        var physicalFolder = string.IsNullOrEmpty(cleanSub)
            ? Path.Combine(target.PhysicalDirectory, year.ToString(), month)
            : Path.Combine(target.PhysicalDirectory, cleanSub, year.ToString(), month);
        Directory.CreateDirectory(physicalFolder);
        var physicalPath = Path.Combine(physicalFolder, unique);
        var publicUrl = $"{target.PublicUrlBase.TrimEnd('/')}/{relativeUnderUploads}";

        bool didCompress = false;
        int? width = null, height = null;
        long writtenSize = file.Length;
        string? contentType = file.ContentType;

        if (compress is not null
            && ImageExtensions.Contains(ext)
            && file.Length >= compress.SkipBelowBytes)
        {
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
                        using var data = image.Encode(SKEncodedImageFormat.Jpeg, compress.JpegQuality);
                        await using var fs = File.Create(physicalPath);
                        data.SaveTo(fs);
                        writtenSize = fs.Length;
                        contentType = "image/jpeg";
                        didCompress = true;
                    }
                    else
                    {
                        await SaveStreamAsync(src, physicalPath, ct);
                    }
                }
                else
                {
                    await SaveStreamAsync(src, physicalPath, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Compression failed for {File}; saving original.", file.FileName);
                src.Position = 0;
                await SaveStreamAsync(src, physicalPath, ct);
            }
        }
        else
        {
            await using var stream = file.OpenReadStream();
            await SaveStreamAsync(stream, physicalPath, ct);
        }

        return new UploadResult(
            RelativePath: relativeUnderUploads,
            PublicUrl: publicUrl,
            FileName: file.FileName,
            ContentType: contentType,
            FileSize: writtenSize,
            Compressed: didCompress,
            Width: width,
            Height: height);
    }

    public Task DeleteAsync(string? moduleId, string publicUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(publicUrl)) return Task.CompletedTask;

        var target = _resolver.Resolve(moduleId);
        var prefix = target.PublicUrlBase.TrimEnd('/') + "/";
        if (!publicUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            // URL doesn't belong to this module's storage — silently skip so
            // legacy /investpro/... URLs aren't accidentally probed under
            // /modules/investpro/uploads/.
            _logger.LogDebug("Skipping delete for {Url} — outside module {Mod} storage root.", publicUrl, moduleId);
            return Task.CompletedTask;
        }
        var relative = publicUrl[prefix.Length..].TrimStart('/');
        var physical = Path.Combine(target.PhysicalDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (File.Exists(physical)) File.Delete(physical);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete {Path}.", physical);
        }
        return Task.CompletedTask;
    }

    private static async Task SaveStreamAsync(Stream source, string physicalPath, CancellationToken ct)
    {
        source.Position = 0;
        await using var fs = File.Create(physicalPath);
        await source.CopyToAsync(fs, ct);
    }

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
