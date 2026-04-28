using FlexCms.Framework.Modules;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlexCms.Tests.Unit.Phase4;

public class ModuleStateServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _moduleFolder;
    private readonly ModuleStateService _state;

    public ModuleStateServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "fcms_state_test_" + Guid.NewGuid());
        _moduleFolder = Path.Combine(_tempRoot, "TestModule");
        Directory.CreateDirectory(_moduleFolder);

        _state = new ModuleStateService(Substitute.For<ILogger<ModuleStateService>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Deactivate ────────────────────────────────────────────────────────────

    [Fact]
    public void Deactivate_creates_disabled_marker()
    {
        Assert.True(_state.Deactivate(_moduleFolder));
        Assert.True(File.Exists(Path.Combine(_moduleFolder, ModuleManager.DisabledMarker)));
    }

    [Fact]
    public void Deactivate_returns_false_when_folder_missing()
    {
        var bogus = Path.Combine(_tempRoot, "nope");
        Assert.False(_state.Deactivate(bogus));
    }

    // ── Activate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Activate_removes_disabled_marker_when_present()
    {
        _state.Deactivate(_moduleFolder);
        Assert.True(_state.Activate(_moduleFolder));
        Assert.False(File.Exists(Path.Combine(_moduleFolder, ModuleManager.DisabledMarker)));
    }

    [Fact]
    public void Activate_is_noop_when_marker_absent()
    {
        // Already active — calling Activate should still succeed
        Assert.True(_state.Activate(_moduleFolder));
        Assert.False(File.Exists(Path.Combine(_moduleFolder, ModuleManager.DisabledMarker)));
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    [Fact]
    public void Uninstall_writes_pending_marker_does_not_delete_folder()
    {
        Assert.True(_state.Uninstall(_moduleFolder));
        Assert.True(File.Exists(Path.Combine(_moduleFolder, ModuleManager.UninstallMarker)));
        // Folder must still exist — actual deletion happens on next startup
        Assert.True(Directory.Exists(_moduleFolder));
    }

    // ── ModuleManager.ProcessPendingUninstalls ────────────────────────────────

    [Fact]
    public void ScanAndLoad_deletes_folders_with_uninstall_marker()
    {
        _state.Uninstall(_moduleFolder);

        var manager = new ModuleManager(
            new ModuleLoader(Substitute.For<ILogger<ModuleLoader>>()),
            Substitute.For<ILogger<ModuleManager>>());
        manager.ScanAndLoad(_tempRoot);

        Assert.False(Directory.Exists(_moduleFolder));
    }
}
