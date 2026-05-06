namespace FlexCms.Framework.Themes;

/// <summary>
/// Loads + serves theme manifests. Discovery is filesystem-driven: any
/// directory under the configured themes root that contains a valid
/// <c>theme.json</c> becomes an installable theme. The default
/// <c>FlexCms.Default</c> built-in entry is always present even when the
/// disk is empty so the host has something to render with.
/// </summary>
public interface IThemeManager
{
    /// <summary>All themes discovered on disk plus built-in fallbacks.</summary>
    IReadOnlyList<ThemeManifest> All { get; }

    ThemeManifest? Get(string themeId);

    /// <summary>The built-in default — never null, never deletable. Used as the fallback when a configured theme can't be resolved.</summary>
    ThemeManifest Default { get; }

    /// <summary>Re-scan the themes directory. Call after install/uninstall.</summary>
    void Refresh();
}
