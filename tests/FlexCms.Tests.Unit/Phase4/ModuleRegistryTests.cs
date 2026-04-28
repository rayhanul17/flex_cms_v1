using FlexCms.Framework.Modules;

namespace FlexCms.Tests.Unit.Phase4;

public class ModuleRegistryTests
{
    [Fact]
    public void Empty_registry_has_zero_modules()
    {
        var reg = new ModuleRegistry([]);
        Assert.Empty(reg.Modules);
        Assert.Null(reg.FindById("anything"));
    }

    [Fact]
    public void FindById_is_case_insensitive()
    {
        var reg = new ModuleRegistry([Module("Blog")]);
        Assert.NotNull(reg.FindById("blog"));
        Assert.NotNull(reg.FindById("BLOG"));
        Assert.NotNull(reg.FindById("Blog"));
        Assert.Null(reg.FindById("ecom"));
    }

    [Fact]
    public void Modules_collection_preserves_input_order()
    {
        var a = Module("A");
        var b = Module("B");
        var c = Module("C");

        var reg = new ModuleRegistry([a, b, c]);
        Assert.Equal(["A", "B", "C"], reg.Modules.Select(m => m.ModuleId));
    }

    private static LoadedModule Module(string id)
    {
        var manifest = new ModuleManifest { ModuleId = id, ModuleName = id, Version = "1.0.0" };
        return new LoadedModule(typeof(ModuleRegistryTests).Assembly, manifest, new Fake(id));
    }

    private sealed class Fake : BaseModule
    {
        public Fake(string id) { ModuleId = id; }
        public override string ModuleId { get; }
        public override string ModuleName => ModuleId;
        public override string Version => "1.0.0";
        public override string TablePrefix => ModuleId.ToLowerInvariant();
    }
}
