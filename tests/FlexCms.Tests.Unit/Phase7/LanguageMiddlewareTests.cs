using FlexCms.Framework.I18n;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase7;

/// <summary>
/// Tests the request-language resolution chain (url-prefix → cookie → site
/// default) and the side effect of stripping <c>/{lang}/</c> from the path so
/// downstream routing keeps working.
/// </summary>
public class LanguageMiddlewareTests
{
    private static (LanguageMiddleware mw, FcmsTranslator t, ISettingsService settings) Build(
        string defaultLang = "en", string mode = "cookie", bool nextRan = false)
    {
        var t = new FcmsTranslator();
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<LanguageMiddleware.SiteLanguageSnapshot>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new LanguageMiddleware.SiteLanguageSnapshot
            {
                DefaultLanguage = defaultLang,
                LanguageMode = mode
            }));
        var mw = new LanguageMiddleware(_ => Task.CompletedTask, t);
        return (mw, t, settings);
    }

    private static DefaultHttpContext Ctx(string path = "/", string? cookieLang = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        if (!string.IsNullOrEmpty(cookieLang))
            ctx.Request.Headers["Cookie"] = $"{SupportedLanguages.CookieName}={cookieLang}";
        return ctx;
    }

    [Fact]
    public async Task Cookie_mode_uses_cookie_lang_when_present()
    {
        var (mw, _, settings) = Build(defaultLang: "en", mode: "cookie");
        var ctx = Ctx("/about", cookieLang: "bn");

        await mw.InvokeAsync(ctx, settings);

        Assert.Equal("bn", ctx.Items[SupportedLanguages.ContextItemKey]);
        Assert.Equal("en", ctx.Items[SupportedLanguages.DefaultLangContextKey]);
        Assert.Equal("/about", ctx.Request.Path); // cookie mode never strips path
    }

    [Fact]
    public async Task Cookie_mode_falls_back_to_site_default_when_no_cookie()
    {
        var (mw, _, settings) = Build(defaultLang: "bn", mode: "cookie");
        var ctx = Ctx("/about");

        await mw.InvokeAsync(ctx, settings);

        Assert.Equal("bn", ctx.Items[SupportedLanguages.ContextItemKey]);
    }

    [Fact]
    public async Task Cookie_mode_ignores_unsupported_cookie_value()
    {
        var (mw, _, settings) = Build(defaultLang: "en", mode: "cookie");
        var ctx = Ctx("/about", cookieLang: "klingon");

        await mw.InvokeAsync(ctx, settings);

        Assert.Equal("en", ctx.Items[SupportedLanguages.ContextItemKey]);
    }

    [Fact]
    public async Task Url_prefix_mode_strips_lang_segment_and_sets_culture()
    {
        var (mw, _, settings) = Build(defaultLang: "en", mode: "url-prefix");
        var ctx = Ctx("/bn/about");

        await mw.InvokeAsync(ctx, settings);

        Assert.Equal("bn", ctx.Items[SupportedLanguages.ContextItemKey]);
        Assert.Equal("/about", ctx.Request.Path.Value);
    }

    [Fact]
    public async Task Url_prefix_mode_root_lang_segment_strips_to_slash()
    {
        var (mw, _, settings) = Build(defaultLang: "en", mode: "url-prefix");
        var ctx = Ctx("/bn");

        await mw.InvokeAsync(ctx, settings);

        Assert.Equal("bn", ctx.Items[SupportedLanguages.ContextItemKey]);
        Assert.Equal("/", ctx.Request.Path.Value);
    }

    [Fact]
    public async Task Url_prefix_mode_no_lang_in_path_uses_default()
    {
        var (mw, _, settings) = Build(defaultLang: "en", mode: "url-prefix");
        var ctx = Ctx("/about");

        await mw.InvokeAsync(ctx, settings);

        Assert.Equal("en", ctx.Items[SupportedLanguages.ContextItemKey]);
        Assert.Equal("/about", ctx.Request.Path.Value); // no segment to strip
    }

    [Fact]
    public async Task Url_prefix_mode_unknown_first_segment_is_left_alone()
    {
        var (mw, _, settings) = Build(defaultLang: "en", mode: "url-prefix");
        var ctx = Ctx("/admin/posts");   // "admin" is NOT a language → treated as content path

        await mw.InvokeAsync(ctx, settings);

        Assert.Equal("en", ctx.Items[SupportedLanguages.ContextItemKey]);
        Assert.Equal("/admin/posts", ctx.Request.Path.Value);
    }

    [Fact]
    public async Task Default_lang_context_item_is_always_set()
    {
        var (mw, _, settings) = Build(defaultLang: "bn", mode: "cookie");
        var ctx = Ctx("/x");

        await mw.InvokeAsync(ctx, settings);

        Assert.Equal("bn", ctx.Items[SupportedLanguages.DefaultLangContextKey]);
    }

    [Fact]
    public async Task Settings_failure_does_not_break_pipeline()
    {
        var t = new FcmsTranslator();
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<LanguageMiddleware.SiteLanguageSnapshot>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<LanguageMiddleware.SiteLanguageSnapshot>>(_ => throw new InvalidOperationException("DB down"));

        var ranNext = false;
        var mw = new LanguageMiddleware(_ => { ranNext = true; return Task.CompletedTask; }, t);
        var ctx = Ctx("/x");

        await mw.InvokeAsync(ctx, settings);

        Assert.True(ranNext);
        Assert.Equal("en", ctx.Items[SupportedLanguages.ContextItemKey]);   // safe fallback
    }
}
