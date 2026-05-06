using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Themes;

/// <summary>
/// Inserts the active theme's <c>Themes/{ThemeId}/Views/</c> folders at the
/// front of Razor's view-search path. The active theme id is read from
/// <c>SiteSettings.PublicThemeId</c> per request — themes can be swapped at
/// runtime without recycling the host.
///
/// <para>
/// View resolution order (after this expander is registered):
/// <list type="number">
///   <item><c>~/Themes/{Theme}/Views/{Controller}/{Action}.cshtml</c></item>
///   <item><c>~/Themes/{Theme}/Views/Shared/{Action}.cshtml</c></item>
///   <item>The host's default <c>~/Views/...</c> tree (fallback).</item>
/// </list>
/// </summary>
public sealed class ThemeViewLocationExpander : IViewLocationExpander
{
    private const string ThemeKey = "fcms-theme";

    public void PopulateValues(ViewLocationExpanderContext context)
    {
        // PopulateValues runs once per request before the view-cache lookup.
        // Whatever we put in Values becomes part of the cache key, so we MUST
        // include the active theme id — otherwise themes would get stale
        // views from the cache after a switch.
        var settings = context.ActionContext.HttpContext.RequestServices.GetService<ISettingsService>();
        var themeId = ResolveThemeId(settings);
        context.Values[ThemeKey] = themeId;
    }

    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        if (!context.Values.TryGetValue(ThemeKey, out var themeId) || string.IsNullOrEmpty(themeId))
            return viewLocations;

        // Themes are searched first — host defaults are the fallback.
        var prefixed = new[]
        {
            $"/Themes/{themeId}/Views/{{1}}/{{0}}.cshtml",
            $"/Themes/{themeId}/Views/Shared/{{0}}.cshtml"
        };
        return prefixed.Concat(viewLocations);
    }

    private static string ResolveThemeId(ISettingsService? settings)
    {
        if (settings is null) return ThemeManager.DefaultId;
        try
        {
            var snap = settings.GetAsync<ThemeSnapshot>("site:general").GetAwaiter().GetResult();
            return string.IsNullOrWhiteSpace(snap.PublicThemeId) ? ThemeManager.DefaultId : snap.PublicThemeId;
        }
        catch
        {
            return ThemeManager.DefaultId;
        }
    }

    /// <summary>Local DTO matching the relevant subset of SiteSettings — Framework can't reference Core.</summary>
    private sealed class ThemeSnapshot
    {
        public string PublicThemeId { get; set; } = "";
    }
}
