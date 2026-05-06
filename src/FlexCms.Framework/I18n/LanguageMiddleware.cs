using System.Globalization;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;

namespace FlexCms.Framework.I18n;

/// <summary>
/// Resolves the request's language code and exposes it via
/// <see cref="HttpContext.Items"/> + <see cref="CultureInfo.CurrentUICulture"/>.
///
/// Resolution order (first non-empty wins):
/// <list type="number">
///   <item>URL prefix segment <c>/{lang}/</c> when site is in url-prefix mode.</item>
///   <item>Cookie <c>fcms_ui_lang</c>.</item>
///   <item>SiteSettings.DefaultLanguage.</item>
///   <item>"en".</item>
/// </list>
///
/// In url-prefix mode the language segment is stripped from <c>Request.Path</c>
/// before downstream routing runs, so existing route templates keep working
/// (e.g. <c>/bn/about</c> dispatches to the same controller as <c>/about</c>).
/// </summary>
public sealed class LanguageMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IFcmsTranslator _translator;

    public LanguageMiddleware(RequestDelegate next, IFcmsTranslator translator)
    {
        _next = next;
        _translator = translator;
    }

    public async Task InvokeAsync(HttpContext ctx, ISettingsService settings)
    {
        var site = await SafeGetSiteAsync(settings);
        var supported = _translator.SupportedLanguages;
        var defaultLang = string.IsNullOrWhiteSpace(site.DefaultLanguage)
            ? SupportedLanguages.English
            : site.DefaultLanguage.ToLowerInvariant();
        var mode = string.Equals(site.LanguageMode, "url-prefix", StringComparison.OrdinalIgnoreCase)
            ? "url-prefix" : "cookie";

        // Always expose the site default — translator's fallback chain reads it.
        ctx.Items[SupportedLanguages.DefaultLangContextKey] = defaultLang;

        string? resolved = null;

        // 1. URL prefix mode: try /{lang}/...
        if (mode == "url-prefix" && ctx.Request.Path.HasValue)
        {
            var path = ctx.Request.Path.Value!;
            var slash = path.IndexOf('/', 1);
            var seg = slash > 0 ? path.Substring(1, slash - 1) : path.TrimStart('/');
            if (IsKnownLang(seg, supported))
            {
                resolved = seg.ToLowerInvariant();
                // Strip prefix so downstream routing sees the original path.
                var rest = slash > 0 ? path[slash..] : "/";
                ctx.Request.Path = rest.Length == 0 ? "/" : rest;
            }
        }

        // 2. Cookie
        if (resolved is null
            && ctx.Request.Cookies.TryGetValue(SupportedLanguages.CookieName, out var cookieLang)
            && IsKnownLang(cookieLang, supported))
        {
            resolved = cookieLang!.ToLowerInvariant();
        }

        // 3. Site default
        resolved ??= defaultLang;

        ctx.Items[SupportedLanguages.ContextItemKey] = resolved;
        TrySetCulture(resolved);

        await _next(ctx);
    }

    private static bool IsKnownLang(string? code, IReadOnlyList<string> supported)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        for (int i = 0; i < supported.Count; i++)
            if (string.Equals(supported[i], code, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void TrySetCulture(string lang)
    {
        try
        {
            var ci = CultureInfo.GetCultureInfo(lang);
            CultureInfo.CurrentCulture = ci;
            CultureInfo.CurrentUICulture = ci;
        }
        catch (CultureNotFoundException) { /* unsupported by OS — fine */ }
    }

    private static async Task<SiteLanguageSnapshot> SafeGetSiteAsync(ISettingsService settings)
    {
        try { return await settings.GetAsync<SiteLanguageSnapshot>("site:general"); }
        catch { return new SiteLanguageSnapshot(); }
    }

    /// <summary>
    /// Subset of <c>SiteSettings</c> sufficient for language resolution. Lives
    /// in Framework so the assembly doesn't take a project reference on Core
    /// just for two strings — JSON deserialization is field-name-based, the
    /// same column round-trips via <see cref="ISettingsService"/>.
    /// </summary>
    public sealed class SiteLanguageSnapshot
    {
        public string DefaultLanguage { get; set; } = "en";
        public string LanguageMode { get; set; } = "cookie";
    }
}
