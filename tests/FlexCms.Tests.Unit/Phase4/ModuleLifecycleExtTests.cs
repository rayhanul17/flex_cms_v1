using FlexCms.Framework.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Tests.Unit.Phase4;

public class ModuleLifecycleExtTests
{
    // ── IFcmsModule new methods have no-op defaults in BaseModule ─────────────

    [Fact]
    public void BaseModule_CreateMigrationContext_returns_null_by_default()
    {
        var module = new StubModule();
        Assert.Null(module.CreateMigrationContext("conn", "mysql"));
    }

    [Fact]
    public async Task BaseModule_SeedDataAsync_completes_without_throwing()
    {
        var module = new StubModule();
        var sp = new ServiceCollection().BuildServiceProvider();
        await module.SeedDataAsync(sp); // should not throw
    }

    [Fact]
    public async Task BaseModule_DropTablesAsync_completes_without_throwing()
    {
        var module = new StubModule();
        await module.DropTablesAsync("conn", "mysql"); // should not throw
    }

    // ── ModuleStateService wwwroot helpers ────────────────────────────────────

    [Fact]
    public void SyncWwwroot_copies_files_from_module_wwwroot()
    {
        using var temp = new TempDir();
        var moduleFolder = Path.Combine(temp.Path, "MyModule");
        var wwwrootSrc = Path.Combine(moduleFolder, "wwwroot", "css");
        Directory.CreateDirectory(wwwrootSrc);
        File.WriteAllText(Path.Combine(wwwrootSrc, "style.css"), "body{}");

        var webRoot = Path.Combine(temp.Path, "wwwroot");
        Directory.CreateDirectory(webRoot);

        var svc = new ModuleStateService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ModuleStateService>.Instance);
        svc.SyncWwwroot(moduleFolder, webRoot, "MyModule");

        Assert.True(File.Exists(Path.Combine(webRoot, "modules", "MyModule", "css", "style.css")));
    }

    [Fact]
    public void SyncWwwroot_is_noop_when_no_wwwroot_folder()
    {
        using var temp = new TempDir();
        var moduleFolder = Path.Combine(temp.Path, "MyModule");
        Directory.CreateDirectory(moduleFolder); // no wwwroot subfolder

        var webRoot = Path.Combine(temp.Path, "wwwroot");
        Directory.CreateDirectory(webRoot);

        var svc = new ModuleStateService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ModuleStateService>.Instance);
        svc.SyncWwwroot(moduleFolder, webRoot, "MyModule"); // should not throw

        Assert.False(Directory.Exists(Path.Combine(webRoot, "modules", "MyModule")));
    }

    [Fact]
    public void DeleteWwwroot_removes_module_assets()
    {
        using var temp = new TempDir();
        var webRoot = temp.Path;
        var dest = Path.Combine(webRoot, "modules", "MyModule", "css");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "style.css"), "body{}");

        var svc = new ModuleStateService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ModuleStateService>.Instance);
        svc.DeleteWwwroot(webRoot, "MyModule");

        Assert.False(Directory.Exists(Path.Combine(webRoot, "modules", "MyModule")));
    }

    // ── IFcmsModelBuilder registered in DI ───────────────────────────────────

    [Fact]
    public void IFcmsModelBuilder_can_be_registered_and_resolved()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFcmsModelBuilder, StubModelBuilder>();
        var sp = services.BuildServiceProvider();

        var builders = sp.GetServices<IFcmsModelBuilder>().ToList();
        Assert.Single(builders);
        Assert.IsType<StubModelBuilder>(builders[0]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class StubModule : BaseModule
    {
        public override string ModuleId => "Test.Stub";
        public override string ModuleName => "Stub";
        public override string Version => "1.0.0";
        public override string TablePrefix => "stub";
    }

    private sealed class StubModelBuilder : IFcmsModelBuilder
    {
        public void Build(ModelBuilder modelBuilder) { }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fcms_test_" + Guid.NewGuid());
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
