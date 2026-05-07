namespace FlexCms.Framework.ImageOptimization;

/// <summary>
/// Image optimization pipeline (Phase 16 — Issue 105). Generates a WebP
/// version of the original + responsive widths (640 / 1024 / 1920 by
/// default). Output filenames follow the convention
/// <c>{base}.webp</c> + <c>{base}-{width}w.webp</c> so the
/// <c>&lt;picture&gt;</c> Razor helper can build a srcset without a
/// separate manifest.
///
/// <para>
/// Backend is SkiaSharp (already in the framework deps for the avatar
/// pipeline) — pure managed, no native dependencies on Linux.
/// </para>
/// </summary>
public interface IImageOptimizer
{
    /// <summary>
    /// Read the source bytes (any common raster format), produce a WebP
    /// version + the configured responsive widths, return their byte
    /// payloads keyed by the relative output filename.
    /// </summary>
    /// <param name="originalFileName">Filename WITHOUT extension. e.g. <c>"hero"</c> → outputs <c>hero.webp</c>, <c>hero-640w.webp</c>, ....</param>
    /// <param name="source">Raw source bytes.</param>
    /// <param name="widths">Target widths in pixels. Default <c>[640, 1024, 1920]</c>; widths larger than the source are skipped.</param>
    Task<IReadOnlyDictionary<string, byte[]>> OptimizeAsync(
        string originalFileName,
        byte[] source,
        IReadOnlyList<int>? widths = null,
        CancellationToken ct = default);
}
