using FlexCms.Framework.Modules;
using FlexCms.Framework.Modules.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FlexCms.Tests.Unit.Phase4;

public class AttributeScannerTests
{
    [Fact]
    public void Scoped_attribute_with_no_service_type_registers_against_implementation()
    {
        var services = new ServiceCollection();
        AttributeScanner.RegisterAttributedTypes(services, typeof(SelfScoped).Assembly);

        var descriptor = services.FirstOrDefault(d => d.ImplementationType == typeof(SelfScoped));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
        Assert.Equal(typeof(SelfScoped), descriptor.ServiceType);
    }

    [Fact]
    public void Scoped_attribute_with_service_type_registers_against_interface()
    {
        var services = new ServiceCollection();
        AttributeScanner.RegisterAttributedTypes(services, typeof(InterfaceImpl).Assembly);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMyContract));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
        Assert.Equal(typeof(InterfaceImpl), descriptor.ImplementationType);
    }

    [Fact]
    public void Singleton_attribute_registers_singleton_lifetime()
    {
        var services = new ServiceCollection();
        AttributeScanner.RegisterAttributedTypes(services, typeof(SelfSingleton).Assembly);

        var descriptor = services.FirstOrDefault(d => d.ImplementationType == typeof(SelfSingleton));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact]
    public void HostedService_attribute_registers_as_IHostedService()
    {
        var services = new ServiceCollection();
        AttributeScanner.RegisterAttributedTypes(services, typeof(MyHostedService).Assembly);

        var hosted = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Any(d => d.ImplementationType == typeof(MyHostedService));
        Assert.True(hosted);
    }

    [Fact]
    public void HostedService_attribute_ignored_when_type_does_not_implement_interface()
    {
        var services = new ServiceCollection();
        AttributeScanner.RegisterAttributedTypes(services, typeof(BadHostedService).Assembly);

        var hosted = services.Any(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(BadHostedService));
        Assert.False(hosted);
    }

    [Fact]
    public void Abstract_and_interface_types_are_skipped()
    {
        var services = new ServiceCollection();
        AttributeScanner.RegisterAttributedTypes(services, typeof(AbstractAttributed).Assembly);

        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(AbstractAttributed));
    }

    [Fact]
    public void Duplicate_scan_does_not_double_register()
    {
        var services = new ServiceCollection();
        AttributeScanner.RegisterAttributedTypes(services, typeof(SelfScoped).Assembly);
        AttributeScanner.RegisterAttributedTypes(services, typeof(SelfScoped).Assembly);

        var count = services.Count(d => d.ImplementationType == typeof(SelfScoped));
        Assert.Equal(1, count);
    }

    // ── Test fixture types ────────────────────────────────────────────────────

    [FcmsScoped]
    public class SelfScoped { }

    public interface IMyContract { }

    [FcmsScoped(typeof(IMyContract))]
    public class InterfaceImpl : IMyContract { }

    [FcmsSingleton]
    public class SelfSingleton { }

    [FcmsHostedService]
    public class MyHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Has the attribute but does NOT implement IHostedService — must be skipped.</summary>
    [FcmsHostedService]
    public class BadHostedService { }

    [FcmsScoped]
    public abstract class AbstractAttributed { }
}
