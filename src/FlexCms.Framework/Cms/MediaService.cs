using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;
using FlexCms.Framework.Storage;
using Microsoft.AspNetCore.Http;
using SkiaSharp;

namespace FlexCms.Framework.Cms;

public class MediaService : IMediaService
{
    private static readonly Dictionary<string, byte[][]> MagicBytes = new()
    {
        { ".jpg",  [[0xFF, 0xD8, 0xFF]] },
        { ".jpeg", [[0xFF, 0xD8, 0xFF]] },
        { ".png",  [[0x89, 0x50, 0x4E, 0x47]] },
        { ".gif",  [[0x47, 0x49, 0x46, 0x38]] },
        { ".webp", [[0x52, 0x49, 0x46, 0x46]] },
        // SVG intentionally excluded — XSS risk without a dedicated sanitizer
        { ".pdf",  [[0x25, 0x50, 0x44, 0x46]] },
        { ".mp4",  [[0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70], [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70]] },
        { ".mp3",  [[0x49, 0x44, 0x33], [0xFF, 0xFB]] },
        { ".zip",  [[0x50, 0x4B, 0x03, 0x04]] },
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".pdf", ".mp4", ".mp3", ".zip"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    private const int ThumbnailMaxSize = 300;

    /// <summary>
    /// Hard ceiling for any single upload, regardless of <c>SiteSettings.MaxUploadSizeMb</c>.
    /// Keeps a misconfigured setting from blowing the heap. 256 MB matches
    /// what most managed runtimes comfortably stream into a single MemoryStream.
    /// </summary>
    public const long AbsoluteMaxBytes = 256L * 1024 * 1024;

    private readonly IRepository<FcmsMedia> _mediaRepo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IFcmsFileStorage _storage;
    private readonly IFcmsLogService _audit;
    private readonly ISettingsService _settings;

    public MediaService(
        IRepository<FcmsMedia> mediaRepo,
        IFcmsUnitOfWork uow,
        IFcmsFileStorage storage,
        IFcmsLogService audit,
        ISettingsService settings)
    {
        _mediaRepo = mediaRepo;
        _uow = uow;
        _storage = storage;
        _audit = audit;
        _settings = settings;
    }

    public async Task<FcmsMedia> UploadAsync(IFormFile file, Guid? folderId, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"File type '{ext}' is not allowed.");

        // Size cap enforced BEFORE streaming to MemoryStream — otherwise a
        // 5 GB upload exhausts the heap before we ever get to validation.
        // Two layers: admin-configurable SiteSettings.MaxUploadSizeMb +
        // absolute floor in case settings is misconfigured/zero.
        UploadLimitSnapshot? snap = null;
        try { snap = await _settings.GetAsync<UploadLimitSnapshot>("site:general", ct); }
        catch { /* settings unavailable → fall back to absolute cap */ }
        var configuredMaxBytes = snap?.MaxUploadSizeMb > 0
            ? (long)snap.MaxUploadSizeMb * 1024 * 1024
            : AbsoluteMaxBytes;
        var maxBytes = Math.Min(configuredMaxBytes, AbsoluteMaxBytes);
        if (file.Length > maxBytes)
            throw new InvalidOperationException(
                $"File exceeds maximum upload size of {maxBytes / (1024 * 1024)} MB.");

        var safeOriginal = Path.GetFileNameWithoutExtension(SanitizeFileName(file.FileName));
        var uniqueName = $"{safeOriginal}_{Guid.NewGuid():N}{ext}";
        var now = FcmsTime.Now;
        var relativePath = $"uploads/media/{now:yyyy/MM}/{uniqueName}";

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;

        if (!ValidateMagicBytes(ms, ext))
            throw new InvalidOperationException("File content does not match the declared extension.");

        ms.Position = 0;
        var url = await _storage.SaveAsync(relativePath, ms, ct);

        var media = new FcmsMedia
        {
            FileName = uniqueName,
            OriginalFileName = file.FileName,
            MimeType = file.ContentType,
            Extension = ext,
            FileSize = file.Length,
            Url = url,
            FolderId = folderId,
        };

        if (ImageExtensions.Contains(ext))
        {
            ms.Position = 0;
            (media.Width, media.Height) = GetImageDimensions(ms);
            ms.Position = 0;
            var thumbPath = $"uploads/thumbs/{now:yyyy/MM}/{uniqueName}";
            media.ThumbnailUrl = await GenerateAndSaveThumbnailAsync(ms, thumbPath, ct);
        }

        await _mediaRepo.AddAsync(media, ct);
        await _uow.SaveChangesAsync(ct);
        return media;
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var media = await _mediaRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Media not found.");

        var relativePath = "uploads/media/" + media.Url.Split("/uploads/media/").Last();
        await _storage.DeleteAsync(relativePath, ct);

        if (media.ThumbnailUrl is not null)
        {
            var thumbRelative = "uploads/thumbs/" + media.ThumbnailUrl.Split("/uploads/thumbs/").Last();
            await _storage.DeleteAsync(thumbRelative, ct);
        }

        await _audit.LogAsync(FcmsAuditActions.MediaDeleted, nameof(FcmsMedia), id.ToString(),
            value: media, ct: ct);
        await _mediaRepo.SoftDeleteAsync(media, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task MoveToFolderAsync(Guid mediaId, Guid? targetFolderId, CancellationToken ct = default)
    {
        var media = await _mediaRepo.GetByIdAsync(mediaId, ct)
            ?? throw new InvalidOperationException("Media not found.");
        media.FolderId = targetFolderId;
        await _mediaRepo.UpdateAsync(media, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(FcmsAuditActions.MediaMoved, nameof(FcmsMedia), mediaId.ToString(),
            new { media.OriginalFileName, TargetFolderId = targetFolderId }, ct: ct);
    }

    public Task<FcmsMedia?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _mediaRepo.GetByIdAsync(id, ct);

    public async Task<IReadOnlyList<FcmsMedia>> GetByFolderAsync(Guid? folderId, CancellationToken ct = default)
        => await _mediaRepo.FindAsync(m => m.FolderId == folderId, ct);

    public async Task<int> BulkUpdateAltTextAsync(IReadOnlyDictionary<Guid, string?> idToAlt, CancellationToken ct = default)
    {
        if (idToAlt is null || idToAlt.Count == 0) return 0;

        // One round-trip to grab everything we need (instead of per-id GetById
        // in a loop). The expression-based predicate translates to a single
        // WHERE Id IN (...) on the relational side.
        var ids = idToAlt.Keys.ToList();
        var rows = await _mediaRepo.FindAsync(m => ids.Contains(m.Id), ct);
        if (rows.Count == 0) return 0;

        var updated = 0;
        foreach (var row in rows)
        {
            if (!idToAlt.TryGetValue(row.Id, out var newAlt)) continue;
            // Trim+coalesce to "" → null so the DB doesn't carry whitespace
            // strings that the validator + screen-readers all treat as missing.
            var normalized = string.IsNullOrWhiteSpace(newAlt) ? null : newAlt.Trim();
            if (row.AltText == normalized) continue;
            row.AltText = normalized;
            await _mediaRepo.UpdateAsync(row, ct);
            updated++;
        }
        if (updated > 0) await _uow.SaveChangesAsync(ct);
        return updated;
    }

    // -- helpers ----------------------------------------------------------------

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static bool ValidateMagicBytes(Stream stream, string ext)
    {
        if (!MagicBytes.TryGetValue(ext, out var signatures) || signatures.Length == 0)
            return true;

        var header = new byte[12];
        var read = stream.Read(header, 0, header.Length);

        return signatures.Any(sig =>
            sig.Length <= read && sig.SequenceEqual(header.Take(sig.Length)));
    }

    private static (int? width, int? height) GetImageDimensions(Stream stream)
    {
        using var bitmap = SKBitmap.Decode(stream);
        return bitmap is null ? (null, null) : (bitmap.Width, bitmap.Height);
    }

    private async Task<string?> GenerateAndSaveThumbnailAsync(Stream imageStream, string thumbPath, CancellationToken ct)
    {
        using var original = SKBitmap.Decode(imageStream);
        if (original is null) return null;

        var scale = Math.Min((float)ThumbnailMaxSize / original.Width, (float)ThumbnailMaxSize / original.Height);
        if (scale >= 1f) scale = 1f;

        var w = (int)(original.Width * scale);
        var h = (int)(original.Height * scale);

        using var resized = original.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear));
        if (resized is null) return null;

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;

        return await _storage.SaveAsync(thumbPath, ms, ct);
    }

    /// <summary>Local DTO matching the relevant subset of SiteSettings — Framework can't reference Core.</summary>
    private sealed class UploadLimitSnapshot
    {
        public int MaxUploadSizeMb { get; set; }
    }
}
