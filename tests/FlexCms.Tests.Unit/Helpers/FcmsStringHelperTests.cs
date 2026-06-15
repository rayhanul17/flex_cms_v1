using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Helpers;

public class FcmsStringHelperTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("short", "short")]
    public void Truncate_short_inputs_pass_through(string? input, string expected)
        => Assert.Equal(expected, FcmsStringHelper.Truncate(input, 10));

    [Fact]
    public void Truncate_appends_ellipsis_when_cut()
        => Assert.Equal("Hello W…", FcmsStringHelper.Truncate("Hello World", 8));

    [Fact]
    public void StripHtml_removes_tags_and_collapses_whitespace()
        => Assert.Equal("Hello world", FcmsStringHelper.StripHtml("<p>Hello   <b>world</b></p>"));

    [Fact]
    public void StripHtml_decodes_html_entities()
        => Assert.Equal("Tom & Jerry", FcmsStringHelper.StripHtml("Tom &amp; Jerry"));

    [Fact]
    public void FirstWords_appends_ellipsis_when_more_words_exist()
        => Assert.Equal("one two…", FcmsStringHelper.FirstWords("one two three four", 2));

    [Fact]
    public void FirstWords_returns_full_text_when_under_limit()
        => Assert.Equal("one two", FcmsStringHelper.FirstWords("one two", 5));

    [Fact]
    public void Capitalize_uppercases_first_letter_only()
        => Assert.Equal("Hello", FcmsStringHelper.Capitalize("hello"));

    [Fact]
    public void SmartUrlEncode_preserves_path_separators()
        => Assert.Equal("a/b/c", FcmsStringHelper.SmartUrlEncode("a/b/c"));

    [Fact]
    public void SmartUrlEncode_escapes_unsafe_chars()
        => Assert.Contains("%20", FcmsStringHelper.SmartUrlEncode("hello world"));

    [Fact]
    public void NormalizeWhitespace_collapses_multiple_spaces()
        => Assert.Equal("a b c", FcmsStringHelper.NormalizeWhitespace("a   b\tc"));

    [Fact]
    public void SanitizeControl_strips_bell_but_keeps_newline()
        => Assert.Equal("line1\nline2", FcmsStringHelper.SanitizeControl("line1\u0007\nline2"));

    [Fact]
    public void Mask_keeps_start_and_end_chars()
        => Assert.Equal("01••••••89", FcmsStringHelper.Mask("0123456789", 2, 2));

    [Fact]
    public void Mask_short_value_fully_masked()
        => Assert.Equal("•••", FcmsStringHelper.Mask("abc", 2, 2));

    [Fact]
    public void SplitLines_handles_mixed_line_endings()
    {
        var lines = FcmsStringHelper.SplitLines("a\nb\r\nc\n");
        Assert.Equal(new[] { "a", "b", "c" }, lines);
    }
}
