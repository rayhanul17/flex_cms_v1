using FlexCms.Framework.Middleware;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase14Cleanup;

public class CorsFromSettingsMiddlewareTests
{
    private static ISettingsService SettingsWith(bool enabled, string allowed)
    {
        var snap = new CorsFromSettingsMiddleware.CorsSnapshot
        {
            CorsEnabled = enabled,
            CorsAllowedOrigins = allowed
        };
        var m = Substitute.For<ISettingsService>();
        m.GetAsync<CorsFromSettingsMiddleware.CorsSnapshot>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(snap);
        return m;
    }

    private static DefaultHttpContext CtxWithOrigin(string method, string? origin)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        if (origin is not null) ctx.Request.Headers.Origin = origin;
        return ctx;
    }

    [Fact]
    public async Task No_origin_header_is_a_passthrough()
    {
        var settings = SettingsWith(enabled: true, allowed: "https://app.example.com");
        var ctx = CtxWithOrigin("GET", origin: null);
        var nextRan = false;
        var mw = new CorsFromSettingsMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, settings);

        Assert.True(nextRan);
        Assert.True(ctx.Response.Headers.AccessControlAllowOrigin.Count == 0);
    }

    [Fact]
    public async Task Disabled_settings_are_passthrough_even_with_origin_header()
    {
        var settings = SettingsWith(enabled: false, allowed: "https://app.example.com");
        var ctx = CtxWithOrigin("GET", "https://app.example.com");
        var nextRan = false;
        var mw = new CorsFromSettingsMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, settings);

        Assert.True(nextRan);
        Assert.True(ctx.Response.Headers.AccessControlAllowOrigin.Count == 0);
    }

    [Fact]
    public async Task Origin_not_in_allow_list_is_passthrough_no_headers()
    {
        var settings = SettingsWith(enabled: true, allowed: "https://app.example.com");
        var ctx = CtxWithOrigin("GET", "https://evil.com");
        var nextRan = false;
        var mw = new CorsFromSettingsMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, settings);

        Assert.True(nextRan);
        Assert.True(ctx.Response.Headers.AccessControlAllowOrigin.Count == 0);
    }

    [Fact]
    public async Task Allowed_origin_GET_adds_cors_headers_and_continues()
    {
        var settings = SettingsWith(enabled: true, allowed: "https://app.example.com,https://admin.example.com");
        var ctx = CtxWithOrigin("GET", "https://app.example.com");
        var nextRan = false;
        var mw = new CorsFromSettingsMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, settings);

        Assert.True(nextRan);
        Assert.Equal("https://app.example.com", ctx.Response.Headers.AccessControlAllowOrigin.ToString());
        Assert.Equal("true", ctx.Response.Headers.AccessControlAllowCredentials.ToString());
        Assert.Contains("Origin", ctx.Response.Headers.Vary.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allowed_origin_OPTIONS_short_circuits_with_204()
    {
        var settings = SettingsWith(enabled: true, allowed: "https://app.example.com");
        var ctx = CtxWithOrigin("OPTIONS", "https://app.example.com");
        ctx.Request.Headers.AccessControlRequestHeaders = "X-Custom";
        var nextRan = false;
        var mw = new CorsFromSettingsMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, settings);

        Assert.False(nextRan, "Preflight must short-circuit before downstream pipeline runs.");
        Assert.Equal(StatusCodes.Status204NoContent, ctx.Response.StatusCode);
        Assert.Equal("X-Custom", ctx.Response.Headers.AccessControlAllowHeaders.ToString());
        Assert.Contains("GET", ctx.Response.Headers.AccessControlAllowMethods.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Origin_match_is_case_insensitive()
    {
        var settings = SettingsWith(enabled: true, allowed: "https://APP.example.com");
        var ctx = CtxWithOrigin("GET", "https://app.example.com");
        var mw = new CorsFromSettingsMiddleware(_ => Task.CompletedTask);

        await mw.InvokeAsync(ctx, settings);

        Assert.Equal("https://app.example.com", ctx.Response.Headers.AccessControlAllowOrigin.ToString());
    }
}
