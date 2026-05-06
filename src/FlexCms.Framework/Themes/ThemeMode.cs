using Microsoft.AspNetCore.Http;

namespace FlexCms.Framework.Themes;

/// <summary>
/// Dark / light / auto mode toggle. Stored in the <c>fcms_theme_mode</c>
/// cookie; auto means "respect <c>prefers-color-scheme</c>" (handled
/// client-side by emitting <c>data-theme-mode="auto"</c> on <c>&lt;html&gt;</c>
/// and letting CSS handle it).
/// </summary>
public static class ThemeMode
{
    public const string CookieName = "fcms_theme_mode";

    public const string Light = "light";
    public const string Dark = "dark";
    public const string Auto = "auto";

    public static IReadOnlyList<string> All { get; } = [Light, Dark, Auto];

    /// <summary>Resolve the active mode for this request — falls back to <see cref="Auto"/>.</summary>
    public static string Resolve(HttpContext ctx)
    {
        if (ctx?.Request?.Cookies is null) return Auto;
        if (!ctx.Request.Cookies.TryGetValue(CookieName, out var v) || string.IsNullOrWhiteSpace(v)) return Auto;
        var lower = v.ToLowerInvariant();
        return lower is Light or Dark or Auto ? lower : Auto;
    }

    /// <summary>Persist <paramref name="mode"/> as the visitor's preference. Invalid values are coerced to <see cref="Auto"/>.</summary>
    public static void Set(HttpContext ctx, string mode)
    {
        var normalized = (mode ?? "").ToLowerInvariant();
        if (normalized is not (Light or Dark or Auto)) normalized = Auto;
        ctx.Response.Cookies.Append(CookieName, normalized, new CookieOptions
        {
            HttpOnly = false,           // CSS toggle reads it client-side
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });
    }
}
