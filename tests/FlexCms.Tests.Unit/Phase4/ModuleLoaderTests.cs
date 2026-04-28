using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using FlexCms.Framework.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlexCms.Tests.Unit.Phase4;

public class ModuleLoaderTests
{
    [Fact]
    public void LoadFromAssembly_returns_null_when_assembly_has_no_module_json()
    {
        var loader = BuildLoader();
        // The test assembly itself has no module.json embedded
        var result = loader.LoadFromAssembly(typeof(ModuleLoaderTests).Assembly);
        Assert.Null(result);
    }

    [Fact]
    public void LoadFromAssembly_returns_null_when_no_IFcmsModule_implementation()
    {
        // Use the framework assembly — it has many types but none implement
        // IFcmsModule (interfaces and abstract base don't count)
        var loader = BuildLoader();
        var result = loader.LoadFromAssembly(typeof(IFcmsModule).Assembly);
        Assert.Null(result);
    }

    private static ModuleLoader BuildLoader()
    {
        var log = Substitute.For<ILogger<ModuleLoader>>();
        return new ModuleLoader(log);
    }
}
