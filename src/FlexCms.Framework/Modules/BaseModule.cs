using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Convenience base class — modules typically inherit from this rather than
/// implement <see cref="IFcmsModule"/> directly. Provides a no-op default
/// for <see cref="RegisterServices"/>.
/// </summary>
public abstract class BaseModule : IFcmsModule
{
    public abstract string ModuleId { get; }
    public abstract string ModuleName { get; }
    public abstract string Version { get; }
    public abstract string TablePrefix { get; }

    public virtual void RegisterServices(IServiceCollection services) { }
}
