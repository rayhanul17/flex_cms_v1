using FlexCms.Framework.Themes;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FlexCms.Tests.Unit.Phase11;

public class ThemeModeTests
{
    [Fact]
    public void Resolve_returns_auto_when_no_cookie()
    {
        var ctx = new DefaultHttpContext();
        Assert.Equal(ThemeMode.Auto, ThemeMode.Resolve(ctx));
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    [InlineData(ThemeMode.Auto)]
    public void Resolve_returns_cookie_value_for_known_modes(string mode)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Cookie"] = $"{ThemeMode.CookieName}={mode}";
        Assert.Equal(mode, ThemeMode.Resolve(ctx));
    }

    [Fact]
    public void Resolve_falls_back_to_auto_for_unknown_cookie_value()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Cookie"] = $"{ThemeMode.CookieName}=plaid";
        Assert.Equal(ThemeMode.Auto, ThemeMode.Resolve(ctx));
    }

    [Fact]
    public void Set_persists_known_mode_as_cookie()
    {
        var ctx = new DefaultHttpContext();
        ThemeMode.Set(ctx, ThemeMode.Dark);

        Assert.Contains("fcms_theme_mode=dark", ctx.Response.Headers["Set-Cookie"].ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Set_coerces_unknown_mode_to_auto()
    {
        var ctx = new DefaultHttpContext();
        ThemeMode.Set(ctx, "klingon");

        Assert.Contains("fcms_theme_mode=auto", ctx.Response.Headers["Set-Cookie"].ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void All_lists_three_supported_modes()
    {
        Assert.Equal(3, ThemeMode.All.Count);
        Assert.Contains(ThemeMode.Light, ThemeMode.All);
        Assert.Contains(ThemeMode.Dark, ThemeMode.All);
        Assert.Contains(ThemeMode.Auto, ThemeMode.All);
    }
}
