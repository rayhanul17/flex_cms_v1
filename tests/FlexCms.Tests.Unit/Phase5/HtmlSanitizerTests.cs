using FlexCms.Framework.Cms;

namespace FlexCms.Tests.Unit.Phase5;

public class HtmlSanitizerTests
{
    // ── Dangerous tag removal ─────────────────────────────────────────────────

    [Theory]
    [InlineData("<script>alert('xss')</script>", "alert")]
    [InlineData("<SCRIPT>alert('xss')</SCRIPT>", "alert")]
    [InlineData("<style>body{display:none}</style>", "display:none")]
    [InlineData("<iframe src='evil.com'></iframe>", "<iframe")]
    [InlineData("<form action='/hack'><input/></form>", "<form")]
    [InlineData("<object data='evil'></object>", "<object")]
    public void Dangerous_tags_are_stripped_with_content(string input, string shouldNotContain)
    {
        var result = HtmlSanitizer.Sanitize(input);
        Assert.DoesNotContain(shouldNotContain, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Safe_html_is_preserved()
    {
        const string input = "<p>Hello <strong>World</strong></p><a href='/about'>About</a>";
        var result = HtmlSanitizer.Sanitize(input);
        Assert.Contains("<p>", result);
        Assert.Contains("<strong>", result);
        Assert.Contains("<a href=", result);
    }

    // ── Event attribute stripping ─────────────────────────────────────────────

    [Theory]
    [InlineData("<img src='x' onerror='alert(1)'>", "onerror")]
    [InlineData("<div onclick='evil()'>click</div>", "onclick")]
    [InlineData("<body onload='steal()'>", "onload")]
    public void Event_attributes_are_stripped(string input, string eventAttr)
    {
        var result = HtmlSanitizer.Sanitize(input);
        Assert.DoesNotContain(eventAttr, result, StringComparison.OrdinalIgnoreCase);
    }

    // ── JavaScript protocol stripping ─────────────────────────────────────────

    [Fact]
    public void Javascript_protocol_in_href_is_replaced()
    {
        const string input = "<a href=\"javascript:alert(1)\">click</a>";
        var result = HtmlSanitizer.Sanitize(input);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Javascript_protocol_in_src_is_replaced()
    {
        const string input = "<img src=\"javascript:alert(1)\">";
        var result = HtmlSanitizer.Sanitize(input);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Empty_string_returns_empty()
    {
        Assert.Equal("", HtmlSanitizer.Sanitize(""));
    }

    [Fact]
    public void Plain_text_is_unchanged()
    {
        const string text = "Hello World 123";
        Assert.Equal(text, HtmlSanitizer.Sanitize(text));
    }

    [Fact]
    public void Script_tag_inside_paragraph_is_stripped()
    {
        const string input = "<p>Safe text <script>bad()</script> more text</p>";
        var result = HtmlSanitizer.Sanitize(input);
        Assert.Contains("<p>", result);
        Assert.DoesNotContain("bad()", result);
    }
}
