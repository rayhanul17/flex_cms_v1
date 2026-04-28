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

    private static ModuleManager BuildManager()
    {
        var loaderLog = Substitute.For<ILogger<ModuleLoader>>();
        var managerLog = Substitute.For<ILogger<ModuleManager>>();
        return new ModuleManager(new ModuleLoader(loaderLog), managerLog);
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
        return new LoadedModule(typeof(ModuleManagerTests).Assembly, manifest, new FakeModule(id));
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
