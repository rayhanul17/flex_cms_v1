using FlexCms.Framework.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlexCms.Tests.Unit.Phase4;

public class ModuleManagerTests
{
    // ── ScanAndLoad ───────────────────────────────────────────────────────────

    [Fact]
    public void ScanAndLoad_returns_empty_when_modules_folder_missing()
    {
        var manager = BuildManager();
        var result = manager.ScanAndLoad("/path/that/definitely/does/not/exist/__nope__");
        Assert.Empty(result);
    }

    [Fact]
    public void ScanAndLoad_returns_empty_when_folder_exists_but_has_no_modules()
    {
        var temp = Path.Combine(Path.GetTempPath(), "fcms_modules_empty_" + Guid.NewGuid());
        Directory.CreateDirectory(temp);
        try
        {
            var manager = BuildManager();
            Assert.Empty(manager.ScanAndLoad(temp));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    // ── SortByDependencies ────────────────────────────────────────────────────

    [Fact]
    public void SortByDependencies_orders_dependencies_before_dependents()
    {
        var a = Module("A");
        var b = Module("B", dependsOn: ["A"]);
        var c = Module("C", dependsOn: ["B"]);

        // Pass them in reverse to prove the sort actually does work
        var sorted = ModuleManager.SortByDependencies([c, b, a]);

        Assert.Equal(["A", "B", "C"], sorted.Select(m => m.ModuleId));
    }

    [Fact]
    public void SortByDependencies_handles_independent_modules()
    {
        var a = Module("A");
        var b = Module("B");

        var sorted = ModuleManager.SortByDependencies([a, b]);
        Assert.Equal(2, sorted.Count);
        Assert.Contains(a, sorted);
        Assert.Contains(b, sorted);
    }

    [Fact]
    public void SortByDependencies_ignores_unmet_dependencies_silently()
    {
        // Module B depends on "Missing" which was never loaded — it should
        // still appear in the result (not be dropped).
        var b = Module("B", dependsOn: ["Missing"]);

        var sorted = ModuleManager.SortByDependencies([b]);
        Assert.Single(sorted);
        Assert.Equal("B", sorted[0].ModuleId);
    }

    [Fact]
    public void SortByDependencies_throws_on_cycle()
    {
        var a = Module("A", dependsOn: ["B"]);
        var b = Module("B", dependsOn: ["A"]);

        Assert.Throws<InvalidOperationException>(() =>
            ModuleManager.SortByDependencies([a, b]));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // ── Pre-load integrity (security-audit-recheck §8.1) ──────────────────────

    [Fact]
    public void ScanAndLoad_loads_module_when_trust_store_is_unavailable()
    {
        // Fresh install: no trust DB yet. Load must still proceed, otherwise
        // a brand-new admin upload could never be picked up. The activator
        // records the hash on activation so the NEXT boot enforces.
        var sampleDir = FindSampleHelloFolder();
        if (sampleDir is null) return;  // Sample isn't built locally; skip.

        using var tempRoot = new TempModuleRoot();
        var moduleDir = tempRoot.AddModuleFolder("FlexCms.Sample.Hello", sampleDir);

        var manager = BuildManager(NullModuleTrustStore.Instance);
        var loaded = manager.ScanAndLoad(tempRoot.Root);
        Assert.Single(loaded);
        Assert.Equal("FlexCms.Sample.Hello", loaded[0].ModuleId);
    }

    [Fact]
    public void ScanAndLoad_refuses_module_when_trust_store_hash_mismatches()
    {
        // Tamper scenario: a hash was previously recorded, the DLL on disk
        // no longer matches. Pre-load gate must reject — Assembly.LoadFrom
        // never runs, the module is absent from the registry, no services
        // get registered.
        var sampleDir = FindSampleHelloFolder();
        if (sampleDir is null) return;

        using var tempRoot = new TempModuleRoot();
        tempRoot.AddModuleFolder("FlexCms.Sample.Hello", sampleDir);

        var fakeTrust = new InMemoryTrustStore(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FlexCms.Sample.Hello"] = "deadbeef" + new string('0', 56),  // 64 hex chars, will never match
        });

        var manager = BuildManager(fakeTrust);
        var loaded = manager.ScanAndLoad(tempRoot.Root);
        Assert.Empty(loaded);
    }

    private static string? FindSampleHelloFolder()
    {
        // Locate the built sample DLL relative to the test bin. Returns
        // null if the sample isn't built (e.g. clean CI minimum-build).
        var here = AppContext.BaseDirectory;
        var dll = Path.Combine(here, "FlexCms.Sample.Hello.dll");
        return File.Exists(dll) ? here : null;
    }

    private static ModuleManager BuildManager(IModuleTrustStore? trust = null)
    {
        var loaderLog = Substitute.For<ILogger<ModuleLoader>>();
        var managerLog = Substitute.For<ILogger<ModuleManager>>();
        return new ModuleManager(new ModuleLoader(loaderLog), managerLog,
            trust ?? NullModuleTrustStore.Instance);
    }

    private sealed class InMemoryTrustStore : IModuleTrustStore
    {
        private readonly Dictionary<string, string> _approved;
        public InMemoryTrustStore(Dictionary<string, string> approved) { _approved = approved; }
        public string? GetApprovedHash(string moduleId)
            => _approved.TryGetValue(moduleId, out var h) ? h : null;
        public bool IsAvailable => true;
    }

    private sealed class TempModuleRoot : IDisposable
    {
        public string Root { get; }
        public TempModuleRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "fcms_test_modules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string AddModuleFolder(string moduleId, string sourceDir)
        {
            var dest = Path.Combine(Root, moduleId);
            Directory.CreateDirectory(dest);
            // Copy only the DLL we need — the integrity check resolves
            // dependencies via the runtime assemblies path, so we don't
            // need to drag every transitive .dll across.
            var srcDll = Path.Combine(sourceDir, moduleId + ".dll");
            File.Copy(srcDll, Path.Combine(dest, moduleId + ".dll"), overwrite: true);
            return dest;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static LoadedModule Module(string id, params string[] dependsOn)
    {
        var manifest = new ModuleManifest
        {
            ModuleId = id,
            ModuleName = id,
            Version = "1.0.0",
            TablePrefix = id.ToLowerInvariant(),
            DependsOn = dependsOn ?? []
        };
        return new LoadedModule(typeof(ModuleManagerTests).Assembly, manifest, new FakeModule(id),
            folderPath: "", isDeactivated: false);
    }

    private sealed class FakeModule : BaseModule
    {
        public FakeModule(string id) { ModuleId = id; }
        public override string ModuleId { get; }
        public override string ModuleName => ModuleId;
        public override string Version => "1.0.0";
        public override string TablePrefix => ModuleId.ToLowerInvariant();
    }
}
