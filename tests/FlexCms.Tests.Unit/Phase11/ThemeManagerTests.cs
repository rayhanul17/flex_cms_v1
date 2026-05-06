using System.Text.Json;
using FlexCms.Framework.Themes;
using Xunit;

namespace FlexCms.Tests.Unit.Phase11;

/// <summary>
/// ThemeManager scans a directory of theme.json manifests and exposes them
/// alongside the always-present built-in default. Each test creates a
/// disposable temp themes-root and tears it down on dispose.
/// </summary>
public sealed class ThemeManagerTests : IDisposable
{
    private readonly string _root;

    public ThemeManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fcms_theme_tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private void WriteManifest(string folder, ThemeManifest manifest)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "theme.json"), JsonSerializer.Serialize(manifest));
    }

    [Fact]
    public void Empty_root_still_exposes_the_built_in_default()
    {
        var mgr = new ThemeManager(_root);
        Assert.Single(mgr.All);
        Assert.Equal(ThemeManager.DefaultId, mgr.All[0].Id);
        Assert.True(mgr.Default.IsBuiltIn);
    }

    [Fact]
    public void Discovers_disk_themes_alongside_built_in_default()
    {
        WriteManifest("MyTheme", new ThemeManifest { Id = "MyTheme", Name = "My Theme" });
        WriteManifest("Other", new ThemeManifest { Id = "Other", Name = "Other" });

        var mgr = new ThemeManager(_root);

        Assert.Equal(3, mgr.All.Count);
        Assert.Contains(mgr.All, t => t.Id == "MyTheme");
        Assert.Contains(mgr.All, t => t.Id == "Other");
    }

    [Fact]
    public void Manifest_without_id_is_skipped()
    {
        WriteManifest("Bad", new ThemeManifest { Id = "", Name = "Nameless" });
        var mgr = new ThemeManager(_root);
        Assert.Single(mgr.All);   // only the default
    }

    [Fact]
    public void Duplicate_id_with_built_in_is_dropped()
    {
        WriteManifest("Dup", new ThemeManifest { Id = ThemeManager.DefaultId, Name = "Hijack" });
        var mgr = new ThemeManager(_root);
        Assert.Single(mgr.All);   // built-in wins
        Assert.Equal("FlexCms Default", mgr.All[0].Name);
    }

    [Fact]
    public void Get_returns_default_for_default_id_case_insensitive()
    {
        var mgr = new ThemeManager(_root);
        Assert.Same(mgr.Default, mgr.Get(ThemeManager.DefaultId));
        Assert.Same(mgr.Default, mgr.Get("flexcms.default"));   // case-insensitive
    }

    [Fact]
    public void Get_unknown_returns_null()
    {
        var mgr = new ThemeManager(_root);
        Assert.Null(mgr.Get("does-not-exist"));
    }

    [Fact]
    public void Refresh_picks_up_themes_added_after_construction()
    {
        var mgr = new ThemeManager(_root);
        Assert.Single(mgr.All);

        WriteManifest("Late", new ThemeManifest { Id = "Late", Name = "Late" });
        mgr.Refresh();

        Assert.Equal(2, mgr.All.Count);
        Assert.NotNull(mgr.Get("Late"));
    }

    [Fact]
    public void Garbled_manifest_file_is_skipped_without_throwing()
    {
        var dir = Path.Combine(_root, "Bad");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "theme.json"), "{ this is not json");

        var mgr = new ThemeManager(_root);   // must not throw
        Assert.Single(mgr.All);
    }
}
