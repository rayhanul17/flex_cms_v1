using FlexCms.Framework.Themes;
using Xunit;

namespace FlexCms.Tests.Integration.Phase11Cleanup;

/// <summary>
/// Verifies the three built-in theme directories shipped with the host
/// (<c>FlexCms.Theme.AdminLte</c>, <c>FlexCms.Theme.Bootstrap</c>,
/// <c>FlexCms.Theme.Tailwind</c>) get discovered correctly when the
/// <see cref="ThemeManager"/> scans the host's themes folder.
///
/// <para>
/// The test points the manager at the actual <c>src/FlexCms.Host/themes/</c>
/// directory by walking up from the test bin location, so a missing/renamed
/// theme manifest will fail this test.
/// </para>
/// </summary>
public class BuiltInThemesDiscoveryTests
{
    private static string ResolveHostThemesRoot()
    {
        // Walk up from AppContext.BaseDirectory until we find the repo's `src` folder,
        // then dive into the host project's themes directory.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "FlexCms.Host", "themes")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "FlexCms.Host", "themes");
    }

    [Fact]
    public void Manager_finds_three_built_in_themes_plus_default()
    {
        var mgr = new ThemeManager(ResolveHostThemesRoot());

        Assert.NotNull(mgr.Get("FlexCms.Theme.Bootstrap"));
        Assert.NotNull(mgr.Get("FlexCms.Theme.AdminLte"));
        Assert.NotNull(mgr.Get("FlexCms.Theme.Tailwind"));

        // Built-in default is always present.
        Assert.NotNull(mgr.Get(ThemeManager.DefaultId));

        // 3 disk + 1 built-in = 4
        Assert.Equal(4, mgr.All.Count);
    }

    [Fact]
    public void Bootstrap_theme_manifest_marked_as_built_in_and_supports_public()
    {
        var mgr = new ThemeManager(ResolveHostThemesRoot());
        var bs = mgr.Get("FlexCms.Theme.Bootstrap")!;

        Assert.True(bs.IsBuiltIn, "Bootstrap theme must be IsBuiltIn so the admin UI prevents deletion.");
        Assert.True(bs.SupportsPublic);
        Assert.Contains("dark", bs.SupportedModes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("light", bs.SupportedModes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdminLte_theme_supports_both_admin_and_public()
    {
        var mgr = new ThemeManager(ResolveHostThemesRoot());
        var adminLte = mgr.Get("FlexCms.Theme.AdminLte")!;

        Assert.True(adminLte.SupportsAdmin);
        Assert.True(adminLte.SupportsPublic);
    }

    [Fact]
    public void Tailwind_theme_is_public_only()
    {
        var mgr = new ThemeManager(ResolveHostThemesRoot());
        var tw = mgr.Get("FlexCms.Theme.Tailwind")!;

        Assert.True(tw.SupportsPublic);
        Assert.False(tw.SupportsAdmin, "Tailwind variant only ships a public layout.");
    }

    [Theory]
    [InlineData("FlexCms.Theme.Bootstrap")]
    [InlineData("FlexCms.Theme.AdminLte")]
    [InlineData("FlexCms.Theme.Tailwind")]
    public void Each_built_in_theme_has_a_PublicLayout_cshtml_on_disk(string themeId)
    {
        var path = Path.Combine(ResolveHostThemesRoot(), themeId, "Views", "Shared", "_PublicLayout.cshtml");
        Assert.True(File.Exists(path), $"Missing layout file: {path}");
    }
}
