using FlexCms.Framework.Themes;
using Xunit;

namespace FlexCms.Tests.Unit.Phase6;

public class HexColorHelperTests
{
    // ── Happy path ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("#0d6efd", "13, 110, 253")]
    [InlineData("#ffffff", "255, 255, 255")]
    [InlineData("#000000", "0, 0, 0")]
    [InlineData("#dc3545", "220, 53, 69")]
    [InlineData("#198754", "25, 135, 84")]
    [InlineData("#ffc107", "255, 193, 7")]
    public void HexToRgb_converts_standard_hex_correctly(string hex, string expected)
        => Assert.Equal(expected, HexColorHelper.HexToRgb(hex));

    [Fact]
    public void HexToRgb_accepts_hex_without_hash_prefix()
        => Assert.Equal("13, 110, 253", HexColorHelper.HexToRgb("0d6efd"));

    [Theory]
    [InlineData("#abc", "170, 187, 204")]   // #abc → #aabbcc
    [InlineData("#fff", "255, 255, 255")]
    [InlineData("#000", "0, 0, 0")]
    [InlineData("#f00", "255, 0, 0")]
    public void HexToRgb_expands_3_char_shorthand(string hex, string expected)
        => Assert.Equal(expected, HexColorHelper.HexToRgb(hex));

    [Theory]
    [InlineData("#FFFFFF", "255, 255, 255")]
    [InlineData("#0D6EFD", "13, 110, 253")]
    [InlineData("#ABC", "170, 187, 204")]
    public void HexToRgb_is_case_insensitive(string hex, string expected)
        => Assert.Equal(expected, HexColorHelper.HexToRgb(hex));

    // ── Invalid / edge input ──────────────────────────────────────────────────

    [Fact]
    public void HexToRgb_returns_black_for_null()
        => Assert.Equal("0, 0, 0", HexColorHelper.HexToRgb(null));

    [Fact]
    public void HexToRgb_returns_black_for_empty_string()
        => Assert.Equal("0, 0, 0", HexColorHelper.HexToRgb(""));

    [Theory]
    [InlineData("#gg0000")]      // invalid hex chars
    [InlineData("#12345")]       // length 5 (not 3 or 6)
    [InlineData("#1234567")]     // length 7
    [InlineData("#zz")]          // length 2 after trim
    public void HexToRgb_returns_black_for_invalid_input(string hex)
        => Assert.Equal("0, 0, 0", HexColorHelper.HexToRgb(hex));

    [Fact]
    public void HexToRgb_with_double_hash_strips_both_and_parses_valid_color()
    {
        // TrimStart('#') removes ALL leading '#', so "##ff0000" → "ff0000" (valid red)
        Assert.Equal("255, 0, 0", HexColorHelper.HexToRgb("##ff0000"));
    }

    [Fact]
    public void HexToRgb_returns_black_for_whitespace_only()
        => Assert.Equal("0, 0, 0", HexColorHelper.HexToRgb("   "));

    // ── Bootstrap default colors roundtrip ────────────────────────────────────

    [Theory]
    [InlineData("#6ea8fe", "110, 168, 254")]   // dark primary
    [InlineData("#75b798", "117, 183, 152")]   // dark success
    [InlineData("#ea868f", "234, 134, 143")]   // dark danger
    [InlineData("#ffda6a", "255, 218, 106")]   // dark warning
    [InlineData("#1e2a3a", "30, 42, 58")]      // dark body bg / sidebar
    [InlineData("#dee2e6", "222, 226, 230")]   // border color
    public void HexToRgb_converts_bootstrap_theme_colors(string hex, string expected)
        => Assert.Equal(expected, HexColorHelper.HexToRgb(hex));
}
