namespace FlexCms.Framework.Themes;

/// <summary>
/// Deserialized <c>theme.json</c>. Each theme directory under
/// <c>{appData}/../themes/{ThemeId}/</c> ships one of these alongside its
/// Razor views and static assets. Mirrors how modules ship a manifest.
///
/// <para>
/// <b>Lookup precedence</b>: when a view is requested, Razor tries the
/// theme's <c>Views/</c> directory first (via
/// <see cref="ThemeViewLocationExpander"/>) and falls back to the host's
/// default <c>Views/</c> tree if missing. So a theme can override only
/// the layouts/partials it cares about.
/// </para>
/// </summary>
public class ThemeManifest
{
    /// <summary>Stable id — folder name + value of <c>SiteSettings.PublicThemeId</c>. Convention: PascalCase.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }

    /// <summary>True for themes shipped with the framework. The admin UI prevents deletion.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>True if the theme exposes a public-site layout. Themes that only style admin should set false.</summary>
    public bool SupportsPublic { get; set; } = true;

    /// <summary>True if the theme overrides the admin layout. Default false — admin always falls back to AdminLte/host layout.</summary>
    public bool SupportsAdmin { get; set; }

    /// <summary>Modes this theme implements via CSS variables. Empty = light only.</summary>
    public List<string> SupportedModes { get; set; } = ["light"];
}
