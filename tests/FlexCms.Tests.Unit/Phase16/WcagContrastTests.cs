using FlexCms.Framework.Accessibility;
using Xunit;

namespace FlexCms.Tests.Unit.Phase16;

public class WcagContrastTests
{
    [Fact]
    public void Black_on_white_is_max_ratio_21()
    {
        // The maximum possible contrast — by spec ~21:1.
        var r = WcagContrast.Ratio("#000000", "#ffffff");
        Assert.InRange(r, 20.9, 21.1);
    }

    [Fact]
    public void Identical_colors_yield_ratio_1()
    {
        Assert.Equal(1.0, WcagContrast.Ratio("#888888", "#888888"), precision: 5);
    }

    [Fact]
    public void Three_char_hex_expands_to_six()
    {
        // #fff should expand to #ffffff and produce the same ratio as #000.
        var a = WcagContrast.Ratio("#000", "#fff");
        var b = WcagContrast.Ratio("#000000", "#ffffff");
        Assert.Equal(b, a, precision: 4);
    }

    [Fact]
    public void Light_grey_text_on_white_fails_AA_for_normal_size()
    {
        // #ccc on white is the textbook example of failing 4.5:1.
        Assert.False(WcagContrast.MeetsAa("#cccccc", "#ffffff"));
    }

    [Fact]
    public void Dark_grey_on_white_passes_AA()
    {
        // #555 on white clears 4.5:1.
        Assert.True(WcagContrast.MeetsAa("#555555", "#ffffff"));
    }

    [Fact]
    public void Order_does_not_matter()
    {
        // Symmetric: ratio(fg, bg) == ratio(bg, fg).
        Assert.Equal(
            WcagContrast.Ratio("#000", "#fff"),
            WcagContrast.Ratio("#fff", "#000"),
            precision: 5);
    }

    [Fact]
    public void Malformed_hex_returns_zero()
    {
        Assert.Equal(0, WcagContrast.Ratio("not-a-color", "#fff"));
        Assert.Equal(0, WcagContrast.Ratio("#fff", ""));
        Assert.Equal(0, WcagContrast.Ratio("#xyz", "#fff"));
    }

    [Fact]
    public void Evaluate_reports_AAA_normal_passing_at_high_contrast()
    {
        var levels = WcagContrast.Evaluate(WcagContrast.Ratio("#000", "#fff"));
        Assert.True(levels.AaNormal);
        Assert.True(levels.AaaNormal);
        Assert.True(levels.AaLarge);
        Assert.True(levels.AaaLarge);
    }
}
