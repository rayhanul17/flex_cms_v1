using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Implemented by every module DLL. The framework discovers types implementing
/// this interface during the module scan phase, instantiates them, and uses
/// the metadata to register services and (later) activate the module.
/// </summary>
public interface IFcmsModule
{
    /// <summary>
    /// Globally unique module identifier — usually the assembly name.
    /// Example: "FlexCms.Blog".
    /// </summary>
    string ModuleId { get; }

    /// <summary>Human-friendly name shown in admin UI. Example: "Blog".</summary>
    string ModuleName { get; }

    /// <summary>SemVer string. Example: "1.0.0".</summary>
    string Version { get; }

    /// <summary>
    /// Table-name prefix for this module's entities (e.g. "blog" → "blog_posts").
    /// Combined with <see cref="Helpers.FcmsHelper.GetEntityName{T}(string)"/>.
    /// </summary>
    string TablePrefix { get; }

    /// <summary>
    /// Register the module's services with the host DI container.
    /// Called by the framework once during startup, before the container is built.
    /// </summary>
    void RegisterServices(IServiceCollection services);
}
