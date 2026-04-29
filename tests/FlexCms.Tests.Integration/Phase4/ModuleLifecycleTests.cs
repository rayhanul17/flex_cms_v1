using FlexCms.Framework.Modules;
using FlexCms.Framework.Modules.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase4;

/// <summary>
/// End-to-end tests for the Phase 4 module pipeline. Uses the real
/// <c>FlexCms.Sample.Hello</c> sample module — its DLL is built as a project
/// reference (Private=false, ReferenceOutputAssembly=false) so it appears
/// in the test bin folder without auto-loading into the test process.
/// Each test copies the DLL into a temporary <c>modules/</c>-style folder
/// and exercises the real <see cref="ModuleManager"/>.
/// </summary>
public class ModuleLifecycleTests : IDisposable
{
    private const string SampleModuleDll = "FlexCms.Sample.Hello.dll";
    private const string SampleModuleId = "FlexCms.Sample.Hello";

    private readonly string _tempRoot;
    private readonly string _moduleFolder;
    private readonly ModuleManager _manager;
    private readonly ModuleStateService _state;

    public ModuleLifecycleTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "fcms_lifecycle_" + Guid.NewGuid());
        _moduleFolder = Path.Combine(_tempRoot, "Hello");
        Directory.CreateDirectory(_moduleFolder);

        // The sample DLL is dropped next to the test assembly thanks to the
        // ProjectReference in the csproj.
        var sourceDir = AppContext.BaseDirectory;
        File.Copy(
            Path.Combine(sourceDir, SampleModuleDll),
            Path.Combine(_moduleFolder, SampleModuleDll),
            overwrite: true);

        var loaderLog = Substitute.For<ILogger<ModuleLoader>>();
        var managerLog = Substitute.For<ILogger<ModuleManager>>();
        _manager = new ModuleManager(new ModuleLoader(loaderLog), managerLog);
        _state = new ModuleStateService(Substitute.For<ILogger<ModuleStateService>>());
    }

    public void Dispose()
    {
        // Once the test process has done Assembly.LoadFrom on the sample DLL,
        // Windows holds a lock on the file until the process exits — so cleanup
        // is best-effort. Temp folder will be reaped by the OS eventually.
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch (UnauthorizedAccessException) { /* DLL locked — ignore */ }
        catch (IOException) { /* DLL locked — ignore */ }
    }

    // ── Discovery + manifest ──────────────────────────────────────────────────

    [Fact]
    public void Sample_module_is_discovered_with_correct_manifest()
    {
        var loaded = _manager.ScanAndLoad(_tempRoot);

        Assert.Single(loaded);
        var module = loaded[0];
        Assert.Equal(SampleModuleId, module.ModuleId);
        Assert.Equal("Hello", module.Manifest.ModuleName);
        Assert.Equal("1.0.0", module.Manifest.Version);
        Assert.Equal("hello", module.Manifest.TablePrefix);
        Assert.False(module.IsDeactivated);
    }

    [Fact]
    public void Sample_module_assembly_contains_IFcmsModule_implementation()
    {
        var loaded = _manager.ScanAndLoad(_tempRoot);
        var module = loaded.Single();
        Assert.NotNull(module.Instance);
        Assert.Equal(SampleModuleId, module.Instance.ModuleId);
    }

    // ── Auto-scan registers attributed services ──────────────────────────────

    [Fact]
    public void AttributeScanner_registers_HelloService_via_FcmsScoped_attribute()
    {
        var loaded = _manager.ScanAndLoad(_tempRoot);
        var module = loaded.Single();

        var services = new ServiceCollection();
        AttributeScanner.RegisterAttributedTypes(services, module.Assembly);

        // HelloService is decorated with [FcmsScoped] in the sample
        var helloServiceType = module.Assembly.GetType("FlexCms.Sample.Hello.Services.HelloService");
        Assert.NotNull(helloServiceType);

        var registered = services.Any(d =>
            d.ImplementationType == helloServiceType &&
            d.Lifetime == ServiceLifetime.Scoped);
        Assert.True(registered, "HelloService should be registered as Scoped via [FcmsScoped]");
    }

    // ── Deactivation ──────────────────────────────────────────────────────────

    [Fact]
    public void Module_with_disabled_marker_is_flagged_deactivated_after_rescan()
    {
        // First scan: active
        Assert.False(_manager.ScanAndLoad(_tempRoot).Single().IsDeactivated);

        // Drop the marker, rescan
        _state.Deactivate(_moduleFolder);
        var rescan = _manager.ScanAndLoad(_tempRoot);

        Assert.Single(rescan);
        Assert.True(rescan[0].IsDeactivated);
    }

    [Fact]
    public void Activate_after_deactivate_clears_the_flag_on_rescan()
    {
        _state.Deactivate(_moduleFolder);
        Assert.True(_manager.ScanAndLoad(_tempRoot).Single().IsDeactivated);

        _state.Activate(_moduleFolder);
        Assert.False(_manager.ScanAndLoad(_tempRoot).Single().IsDeactivated);
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    [Fact]
    public void Uninstall_marker_causes_folder_deletion_on_next_scan()
    {
        // Realistic scenario: the marker was dropped during the previous host
        // run; this scan simulates a fresh restart where the DLL has NOT been
        // loaded yet (so Windows file locking does not block deletion).
        // The test deliberately skips an initial ScanAndLoad — once Assembly.LoadFrom
        // runs, the DLL stays locked for the rest of the test process lifetime.
        _state.Uninstall(_moduleFolder);

        // Folder still exists immediately after marking — deletion is deferred
        Assert.True(Directory.Exists(_moduleFolder));

        // Scan triggers ProcessPendingUninstalls before any DLL load → delete succeeds
        var scan = _manager.ScanAndLoad(_tempRoot);
        Assert.Empty(scan);
        Assert.False(Directory.Exists(_moduleFolder));
    }
}
