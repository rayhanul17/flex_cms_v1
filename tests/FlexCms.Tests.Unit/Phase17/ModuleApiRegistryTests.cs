using FlexCms.Framework.Modules.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlexCms.Tests.Unit.Phase17;

public class ModuleApiRegistryTests
{
    [FcmsModuleApi("1.2.0", DisplayName = "Blog Public API")]
    public interface IBlogPublicApi
    {
        string Hello();
    }

    private sealed class BlogApiImpl : IBlogPublicApi
    {
        public string Hello() => "from-blog";
    }

    [FcmsModuleApi("2.0.0")]
    public interface IPaymentsApi
    {
        int Calc();
    }

    private sealed class PaymentsApiImpl : IPaymentsApi
    {
        public int Calc() => 99;
    }

    private static FcmsModuleApiRegistry CreateWith(Action<IServiceCollection> register)
    {
        var sc = new ServiceCollection();
        register(sc);
        var sp = sc.BuildServiceProvider();
        return new FcmsModuleApiRegistry(sp, NullLogger<FcmsModuleApiRegistry>.Instance);
    }

    [Fact]
    public void Get_returns_implementation_when_registered()
    {
        var reg = CreateWith(sc => sc.AddSingleton<IBlogPublicApi, BlogApiImpl>());
        var api = reg.Get<IBlogPublicApi>();
        Assert.NotNull(api);
        Assert.Equal("from-blog", api!.Hello());
    }

    [Fact]
    public void Get_returns_null_when_provider_module_not_registered()
    {
        // Simulates the "module deactivated" scenario — DI has no
        // implementation for the api interface, so consumers gracefully
        // get null instead of throwing.
        var reg = CreateWith(_ => { });
        Assert.Null(reg.Get<IBlogPublicApi>());
    }

    [Fact]
    public void Get_with_satisfied_constraint_returns_implementation()
    {
        var reg = CreateWith(sc => sc.AddSingleton<IBlogPublicApi, BlogApiImpl>());
        // Declared 1.2.0; >=1.0.0 satisfied.
        Assert.NotNull(reg.Get<IBlogPublicApi>(">=1.0.0"));
    }

    [Fact]
    public void Get_with_unsatisfied_constraint_returns_null()
    {
        var reg = CreateWith(sc => sc.AddSingleton<IBlogPublicApi, BlogApiImpl>());
        // Declared 1.2.0; >=2.0.0 NOT satisfied → null + warning logged.
        Assert.Null(reg.Get<IBlogPublicApi>(">=2.0.0"));
    }

    [Fact]
    public void Get_with_caret_constraint_blocks_major_bump()
    {
        var reg = CreateWith(sc => sc.AddSingleton<IBlogPublicApi, BlogApiImpl>());
        // ^1.0.0 = same major; declared 1.2.0 ✓
        Assert.NotNull(reg.Get<IBlogPublicApi>("^1.0.0"));
    }

    [Fact]
    public void Get_with_caret_blocks_too_low_version()
    {
        var reg = CreateWith(sc => sc.AddSingleton<IBlogPublicApi, BlogApiImpl>());
        // ^1.5.0: same major (1.x) AND >= 1.5.0 — declared 1.2.0 fails.
        Assert.Null(reg.Get<IBlogPublicApi>("^1.5.0"));
    }

    [Fact]
    public void Get_without_attribute_falls_back_to_di_lookup()
    {
        // No [FcmsModuleApi] on this interface → registry returns whatever
        // DI has, behaves like GetService<T>(). Useful for migration off
        // a plain interface.
        var sc = new ServiceCollection();
        var sp = sc.BuildServiceProvider();
        var reg = new FcmsModuleApiRegistry(sp, NullLogger<FcmsModuleApiRegistry>.Instance);
        Assert.Null(reg.Get<IDisposable>());
    }
}
