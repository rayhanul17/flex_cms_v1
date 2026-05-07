using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace FlexCms.Framework.ImageOptimization;

public sealed class SkiaImageOptimizer : IImageOptimizer
{
    public static readonly IReadOnlyList<int> DefaultWidths = [640, 1024, 1920];
    public const int WebPQuality = 80;

    private readonly ILogger<SkiaImageOptimizer> _logger;
    public SkiaImageOptimizer(ILogger<SkiaImageOptimizer> logger) => _logger = logger;

    public Task<IReadOnlyDictionary<string, byte[]>> OptimizeAsync(
        string originalFileName,
        byte[] source,
        IReadOnlyList<int>? widths = null,
        CancellationToken ct = default)
    {
        if (source is null || source.Length == 0)
            return Task.FromResult<IReadOnlyDictionary<string, byte[]>>(new Dictionary<string, byte[]>());

        widths ??= DefaultWidths;
        var baseName = string.IsNullOrWhiteSpace(originalFileName) ? "image" : Path.GetFileNameWithoutExtension(originalFileName);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        try
        {
            using var input = SKBitmap.Decode(source);
            if (input is null)
            {
                _logger.LogWarning("Image decode failed for {File} ({Size} bytes); skipping optimization.", originalFileName, source.Length);
                return Task.FromResult<IReadOnlyDictionary<string, byte[]>>(result);
            }

            // 1. Full-size WebP — same dimensions as source, just re-encoded.
            using (var imageFull = SKImage.FromBitmap(input))
            using (var data = imageFull.Encode(SKEncodedImageFormat.Webp, WebPQuality))
                result[$"{baseName}.webp"] = data.ToArray();

            // 2. Responsive variants — skip widths >= the source width so
            // we don't upscale (loss of quality, larger file).
            var srcWidth = input.Width;
            var srcHeight = input.Height;

            foreach (var w in widths.OrderBy(x => x))
            {
                if (w >= srcWidth) continue;
                var ratio = (double)w / srcWidth;
                var h = Math.Max(1, (int)Math.Round(srcHeight * ratio));

                using var resized = input.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (resized is null) continue;
                using var img = SKImage.FromBitmap(resized);
                using var data = img.Encode(SKEncodedImageFormat.Webp, WebPQuality);
                result[$"{baseName}-{w}w.webp"] = data.ToArray();
            }

            return Task.FromResult<IReadOnlyDictionary<string, byte[]>>(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image optimization failed for {File}.", originalFileName);
            // Caller treats empty result as "skip optimization, serve original".
            return Task.FromResult<IReadOnlyDictionary<string, byte[]>>(result);
        }
    }
}
