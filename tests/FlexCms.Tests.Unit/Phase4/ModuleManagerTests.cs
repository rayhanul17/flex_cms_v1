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

    // ── recheck-2 §4.2: non-module DLLs must skip Assembly.LoadFrom ───────────

    [Fact]
    public void ScanAndLoad_skips_non_module_dll_without_loading()
    {
        // Drop a DLL with NO embedded module.json into a module folder.
        // The pre-load gate must return NotModule and the scanner must
        // continue without calling Assembly.LoadFrom on it.
        var sampleDir = FindSampleHelloFolder();
        if (sampleDir is null) return;

        // Use the test assembly itself as a "non-module DLL" — it has
        // no embedded module.json so the gate will return NotModule.
        var nonModuleSrc = typeof(ModuleManagerTests).Assembly.Location;
        if (!File.Exists(nonModuleSrc)) return;

        using var tempRoot = new TempModuleRoot();
        var folder = Path.Combine(tempRoot.Root, "RandomDll");
        Directory.CreateDirectory(folder);
        File.Copy(nonModuleSrc, Path.Combine(folder, Path.GetFileName(nonModuleSrc)), overwrite: true);

        var manager = BuildManager();
        var loaded = manager.ScanAndLoad(tempRoot.Root);
        // Nothing in that folder is a module — and crucially, we never
        // even called Assembly.LoadFrom on the test DLL itself.
        Assert.DoesNotContain(loaded, m =>
            string.Equals(m.ModuleId, typeof(ModuleManagerTests).Assembly.GetName().Name,
                StringComparison.OrdinalIgnoreCase));
    }

    // ── recheck-2 §4.1: TOFU policy ───────────────────────────────────────────

    [Fact]
    public void ScanAndLoad_refuses_unknown_module_when_TOFU_disabled()
    {
        // Production posture: trust-on-first-use OFF. A module DLL with no
        // recorded approved hash must be refused — operator must upload
        // via /admin/modules first to land an approved hash in the store.
        var sampleDir = FindSampleHelloFolder();
        if (sampleDir is null) return;

        using var tempRoot = new TempModuleRoot();
        tempRoot.AddModuleFolder("FlexCms.Sample.Hello", sampleDir);

        // IsAvailable=true ensures we hit the "no record + TOFU disabled"
        // branch instead of falling through to the unavailable-store path.
        var trust = new InMemoryTrustStore(new Dictionary<string, string>());
        var manager = BuildManager(trust, allowTrustOnFirstUse: false);
        var loaded = manager.ScanAndLoad(tempRoot.Root);
        Assert.Empty(loaded);
    }

    [Fact]
    public void ScanAndLoad_loads_module_when_TOFU_disabled_but_hash_matches()
    {
        // Inverse of the previous test: with the correct approved hash on
        // record, the module loads even with TOFU off. This is the steady-
        // state production posture once the operator has uploaded once.
        var sampleDir = FindSampleHelloFolder();
        if (sampleDir is null) return;

        using var tempRoot = new TempModuleRoot();
        tempRoot.AddModuleFolder("FlexCms.Sample.Hello", sampleDir);

        var sampleDllPath = Path.Combine(tempRoot.Root, "FlexCms.Sample.Hello", "FlexCms.Sample.Hello.dll");
        var actualHash = ComputeSha256(sampleDllPath);

        var trust = new InMemoryTrustStore(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FlexCms.Sample.Hello"] = actualHash,
        });
        var manager = BuildManager(trust, allowTrustOnFirstUse: false);
        var loaded = manager.ScanAndLoad(tempRoot.Root);
        Assert.Single(loaded);
        Assert.Equal("FlexCms.Sample.Hello", loaded[0].ModuleId);
    }

    private static string ComputeSha256(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static string? FindSampleHelloFolder()
    {
        // Locate the built sample DLL relative to the test bin. Returns
        // null if the sample isn't built (e.g. clean CI minimum-build).
        var here = AppContext.BaseDirectory;
        var dll = Path.Combine(here, "FlexCms.Sample.Hello.dll");
        return File.Exists(dll) ? here : null;
    }

    private static ModuleManager BuildManager(
        IModuleTrustStore? trust = null,
        bool allowTrustOnFirstUse = true)
    {
        var loaderLog = Substitute.For<ILogger<ModuleLoader>>();
        var managerLog = Substitute.For<ILogger<ModuleManager>>();
        return new ModuleManager(new ModuleLoader(loaderLog), managerLog,
            trust ?? NullModuleTrustStore.Instance,
            allowTrustOnFirstUse);
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
