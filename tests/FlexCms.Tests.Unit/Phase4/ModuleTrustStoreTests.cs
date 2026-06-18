using FlexCms.Framework.Modules;

namespace FlexCms.Tests.Unit.Phase4;

/// <summary>
/// Tests around the pre-load module trust check — security-audit-recheck §8.1.
/// The store is what feeds <see cref="ModuleManager"/> the approved hashes
/// it uses to refuse loading tampered DLLs before <c>Assembly.LoadFrom</c>.
/// </summary>
public class ModuleTrustStoreTests
{
    [Fact]
    public void NullStore_is_unavailable_and_returns_null_hash()
    {
        var store = NullModuleTrustStore.Instance;
        Assert.False(store.IsAvailable);
        Assert.Null(store.GetApprovedHash("Anything.Module"));
    }

    [Fact]
    public void AdoStore_Build_returns_NullStore_when_connection_string_empty()
    {
        // A fresh install has no DB configured yet. The store must
        // degrade gracefully so module discovery can still proceed
        // (trust-on-first-use applies until the activator records hashes).
        var store = AdoModuleTrustStore.Build(provider: "postgresql", connectionString: "");
        Assert.False(store.IsAvailable);
        Assert.Null(store.GetApprovedHash("Anything.Module"));
    }

    [Fact]
    public void AdoStore_Build_returns_NullStore_when_provider_empty()
    {
        var store = AdoModuleTrustStore.Build(provider: "", connectionString: "Host=x;");
        Assert.False(store.IsAvailable);
    }

    [Fact]
    public void AdoStore_Build_returns_NullStore_for_unrecognised_provider()
    {
        var store = AdoModuleTrustStore.Build(provider: "oracle", connectionString: "Host=x;");
        Assert.False(store.IsAvailable);
    }

    [Fact]
    public void AdoStore_Build_does_not_throw_on_unreachable_db()
    {
        // A misconfigured connection string must NOT prevent host startup.
        // Build returns an unavailable store and ModuleManager skips
        // integrity checks for this boot. The activator's hash compare on
        // a successful future boot resumes the protection.
        var store = AdoModuleTrustStore.Build(
            provider: "postgresql",
            connectionString: "Host=does-not-exist.localdomain;Port=5432;Database=x;Username=u;Password=p;Timeout=2");
        Assert.False(store.IsAvailable);
    }
}
