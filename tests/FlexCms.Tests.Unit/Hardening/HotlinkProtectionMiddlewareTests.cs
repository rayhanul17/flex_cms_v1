using FlexCms.Framework.Middleware;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Hardening;

/// <summary>
/// Hotlink middleware reads <c>SiteSettings.PreventHotlinking</c> +
/// <c>HotlinkWhitelist</c> per request via the framework's internal
/// <c>HotlinkSnapshot</c> DTO (made <c>internal</c> for InternalsVisibleTo).
/// Tests cover the bypass branches (non-uploads paths, disabled setting,
/// same-origin, whitelisted, malformed referer).
/// </summary>
public class HotlinkProtectionMiddlewareTests
{
    private static ISettingsService Settings(bool prevent, string whitelist = "")
    {
        var svc = Substitute.For<ISettingsService>();
        svc.GetAsync<HotlinkProtectionMiddleware.HotlinkSnapshot>("site:general", Arg.Any<CancellationToken>())
            .Returns(new HotlinkProtectionMiddleware.HotlinkSnapshot
            {
                PreventHotlinking = prevent,
                HotlinkWhitelist = whitelist
            });
        return svc;
    }

    private static async Task<(int status, bool nextCalled)> InvokeAsync(
        string path,
        string referer,
        string host,
        bool preventHotlinking,
        string whitelist = "")
    {
        var nextCalled = false;
        Task next(HttpContext _) { nextCalled = true; return Task.CompletedTask; }

        var middleware = new HotlinkProtectionMiddleware(next);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Host = new HostString(host);
        if (!string.IsNullOrEmpty(referer))
            ctx.Request.Headers["Referer"] = referer;
        // 403 path WriteAsync needs a writable response body — DefaultHttpContext
        // gives us a Stream.Null by default which throws. Provide a real one.
        ctx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(ctx, Settings(preventHotlinking, whitelist));
        return (ctx.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task Non_uploads_path_skips_filter_entirely()
    {
        // Pre-filter optimization — settings lookup not even hit for non-uploads paths.
        var (_, nextCalled) = await InvokeAsync("/blog/hello", "https://evil.com",
            "yoursite.com", preventHotlinking: true);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Disabled_setting_lets_uploads_pass_through()
    {
        var (_, nextCalled) = await InvokeAsync("/uploads/hero.jpg", "https://evil.com",
            "yoursite.com", preventHotlinking: false);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Empty_referer_passes_through_direct_hits()
    {
        // User opens an image link directly, browser sends no Referer →
        // legitimate; the bouncer can't distinguish from a hotlink anyway.
        var (_, nextCalled) = await InvokeAsync("/uploads/hero.jpg", referer: "",
            host: "yoursite.com", preventHotlinking: true);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Same_origin_passes()
    {
        var (_, nextCalled) = await InvokeAsync("/uploads/hero.jpg",
            "https://yoursite.com/blog/hello", "yoursite.com", preventHotlinking: true);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Cross_origin_returns_403()
    {
        var (status, nextCalled) = await InvokeAsync("/uploads/hero.jpg",
            "https://evil.com/page", "yoursite.com", preventHotlinking: true);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task Whitelisted_host_passes()
    {
        var (_, nextCalled) = await InvokeAsync("/uploads/hero.jpg",
            "https://partner.com/article", "yoursite.com",
            preventHotlinking: true, whitelist: "partner.com,cdn.partner.com");
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Whitelist_match_is_case_insensitive()
    {
        var (_, nextCalled) = await InvokeAsync("/uploads/hero.jpg",
            "https://Partner.COM/article", "yoursite.com",
            preventHotlinking: true, whitelist: "partner.com");
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Malformed_referer_treated_as_disallowed()
    {
        // Garbage in the Referer header (rare but possible from broken proxies):
        // can't parse host → fail closed.
        var (status, nextCalled) = await InvokeAsync("/uploads/hero.jpg",
            "not-a-url", "yoursite.com", preventHotlinking: true);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }
}
