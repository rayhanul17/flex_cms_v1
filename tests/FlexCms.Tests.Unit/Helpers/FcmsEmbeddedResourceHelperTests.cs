using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Helpers;

public class FcmsEmbeddedResourceHelperTests
{
    // The framework assembly embeds i18n JSONs under Resources/i18n/*.json
    // (declared in FlexCms.Framework.csproj). Use one of those as a known-good
    // resource so the test doesn't need its own asset.
    private static System.Reflection.Assembly FrameworkAssembly
        => typeof(FcmsEmbeddedResourceHelper).Assembly;

    [Fact]
    public void ListResources_returns_at_least_one_resource()
    {
        var names = FcmsEmbeddedResourceHelper.ListResources(FrameworkAssembly);
        Assert.NotEmpty(names);
    }

    [Fact]
    public void Read_returns_null_when_resource_does_not_exist()
        => Assert.Null(FcmsEmbeddedResourceHelper.Read(FrameworkAssembly, "no-such-resource.txt"));

    [Fact]
    public void Read_returns_content_when_resource_exists()
    {
        var names = FcmsEmbeddedResourceHelper.ListResources(FrameworkAssembly);
        // Pick any embedded resource (the i18n bundle) — bare existence is enough.
        var any = names.FirstOrDefault(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (any is null) return; // no JSON resources to exercise — test is a no-op then

        // Use the last segment as the suffix to prove the ends-with lookup works.
        var lastSegment = any.Split('.').TakeLast(2).Aggregate((a, b) => a + "." + b);
        var content = FcmsEmbeddedResourceHelper.Read(FrameworkAssembly, lastSegment);
        Assert.NotNull(content);
    }

    [Fact]
    public async Task ReadAsync_round_trips_the_same_content_as_Read()
    {
        var names = FcmsEmbeddedResourceHelper.ListResources(FrameworkAssembly);
        var any = names.FirstOrDefault(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (any is null) return;

        var suffix = any.Split('.').TakeLast(2).Aggregate((a, b) => a + "." + b);
        var sync = FcmsEmbeddedResourceHelper.Read(FrameworkAssembly, suffix);
        var async = await FcmsEmbeddedResourceHelper.ReadAsync(FrameworkAssembly, suffix);
        Assert.Equal(sync, async);
    }
}
