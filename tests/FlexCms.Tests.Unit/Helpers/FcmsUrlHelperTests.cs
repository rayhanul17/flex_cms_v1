using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Helpers;

public class FcmsUrlHelperTests
{
    [Fact]
    public void Parse_returns_null_on_empty_or_root()
    {
        Assert.Null(FcmsUrlHelper.Parse(""));
        Assert.Null(FcmsUrlHelper.Parse("/"));
    }

    [Fact]
    public void Parse_extracts_controller_and_action()
    {
        var p = FcmsUrlHelper.Parse("/Pages/Edit");
        Assert.NotNull(p);
        Assert.Equal("Pages", p!.Controller);
        Assert.Equal("Edit", p.Action);
        Assert.Null(p.Area);
    }

    [Fact]
    public void Parse_defaults_action_to_Index_when_only_controller()
    {
        var p = FcmsUrlHelper.Parse("/Dashboard");
        Assert.Equal("Index", p!.Action);
    }

    [Fact]
    public void Parse_detects_known_area_prefix()
    {
        var areas = new HashSet<string> { "Admin" };
        var p = FcmsUrlHelper.Parse("/Admin/Pages/Edit", areas);
        Assert.Equal("Admin", p!.Area);
        Assert.Equal("Pages", p.Controller);
        Assert.Equal("Edit", p.Action);
    }

    [Fact]
    public void Parse_ignores_unknown_first_segment_as_area()
    {
        var areas = new HashSet<string> { "Admin" };
        var p = FcmsUrlHelper.Parse("/Blog/Posts/List", areas);
        Assert.Null(p!.Area);
        Assert.Equal("Blog", p.Controller);
    }

    [Fact]
    public void Parse_collects_extra_segments()
    {
        var p = FcmsUrlHelper.Parse("/Pages/Edit/42/preview");
        Assert.Equal(new[] { "42", "preview" }, p!.ExtraSegments);
    }

    [Fact]
    public void Parse_strips_query_and_fragment()
    {
        var p = FcmsUrlHelper.Parse("/Pages/Edit?id=5&lang=bn#title");
        Assert.Equal("Pages", p!.Controller);
        Assert.Equal("Edit", p.Action);
    }

    [Fact]
    public void Parse_handles_absolute_url()
    {
        var p = FcmsUrlHelper.Parse("https://example.com/Pages/Edit");
        Assert.Equal("Pages", p!.Controller);
    }

    [Fact]
    public void GetControllerAction_shortcut_returns_tuple()
    {
        var (controller, action) = FcmsUrlHelper.GetControllerAction("/Pages/Edit");
        Assert.Equal("Pages", controller);
        Assert.Equal("Edit", action);
    }

    [Fact]
    public void Combine_handles_trailing_and_leading_slashes()
    {
        Assert.Equal("https://x.com/a/b", FcmsUrlHelper.Combine("https://x.com/", "/a/b"));
        Assert.Equal("https://x.com/a", FcmsUrlHelper.Combine("https://x.com", "a"));
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("/relative", false)]
    [InlineData(null, false)]
    public void IsAbsoluteHttpUrl_validates_scheme(string? url, bool expected)
        => Assert.Equal(expected, FcmsUrlHelper.IsAbsoluteHttpUrl(url));
}
