using FlexCms.Framework.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Sample.Hello;

public class HelloModule : BaseModule
{
    public override string ModuleId => "FlexCms.Sample.Hello";
    public override string ModuleName => "Hello";
    public override string Version => "1.0.0";
    public override string TablePrefix => "hello";

    // Services declared via [FcmsScoped] attribute are picked up automatically
    // by AttributeScanner — no need to register them manually here. This
    // override is kept empty to demonstrate that the module-level RegisterServices
    // still gets called for modules that prefer explicit registration.
    public override void RegisterServices(IServiceCollection services)
    {
    }
}
