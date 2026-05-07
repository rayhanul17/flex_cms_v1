using FlexCms.Framework.ImageOptimization;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace FlexCms.Tests.Unit.Phase16;

public class ImageOptimizerTests
{
    private static readonly SkiaImageOptimizer Optimizer = new(NullLogger<SkiaImageOptimizer>.Instance);

    /// <summary>Generate a real PNG of the given dimensions so SkiaSharp can decode it.</summary>
    private static byte[] MakePng(int width, int height)
    {
        using var bmp = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bmp))
            canvas.Clear(SKColors.CornflowerBlue);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    [Fact]
    public async Task Optimize_emits_full_webp_plus_smaller_variants()
    {
        var src = MakePng(2000, 1500);
        var output = await Optimizer.OptimizeAsync("hero.jpg", src, widths: [640, 1024, 1920]);

        // Full size + 3 variants.
        Assert.Contains("hero.webp", output.Keys);
        Assert.Contains("hero-640w.webp", output.Keys);
        Assert.Contains("hero-1024w.webp", output.Keys);
        Assert.Contains("hero-1920w.webp", output.Keys);
        Assert.Equal(4, output.Count);
    }

    [Fact]
    public async Task Optimize_skips_widths_larger_than_source()
    {
        var src = MakePng(800, 600);
        var output = await Optimizer.OptimizeAsync("small.jpg", src, widths: [640, 1024, 1920]);

        // Only the 640w variant fits; 1024 + 1920 would upscale → skipped.
        Assert.Contains("small.webp", output.Keys);
        Assert.Contains("small-640w.webp", output.Keys);
        Assert.DoesNotContain("small-1024w.webp", output.Keys);
        Assert.DoesNotContain("small-1920w.webp", output.Keys);
    }

    [Fact]
    public async Task Empty_source_returns_empty_dict()
    {
        var output = await Optimizer.OptimizeAsync("x.jpg", []);
        Assert.Empty(output);
    }

    [Fact]
    public async Task Garbage_input_returns_empty_dict_does_not_throw()
    {
        var output = await Optimizer.OptimizeAsync("x.jpg", [0xFF, 0xFE, 0xFD, 0x00]);
        // Decode failure → caller serves the original; we just don't optimize.
        Assert.Empty(output);
    }

    [Fact]
    public async Task Output_filenames_strip_input_extension()
    {
        var src = MakePng(2000, 1500);
        var output = await Optimizer.OptimizeAsync("photos/holiday-2026.jpeg", src, widths: [640]);
        Assert.Contains("holiday-2026.webp", output.Keys);
        Assert.Contains("holiday-2026-640w.webp", output.Keys);
    }
}
