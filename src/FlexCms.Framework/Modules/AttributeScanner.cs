using System.Reflection;
using FlexCms.Framework.Modules.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Scans a module assembly for types decorated with
/// <see cref="FcmsScopedAttribute"/>, <see cref="FcmsSingletonAttribute"/>,
/// or <see cref="FcmsHostedServiceAttribute"/> and registers each one with
/// the host's service collection. Modules that prefer explicit registration
/// can simply override <see cref="IFcmsModule.RegisterServices"/> instead.
/// </summary>
public static class AttributeScanner
{
    public static void RegisterAttributedTypes(IServiceCollection services, Assembly assembly)
    {
        // GetTypes can throw on partially-loadable assemblies — fall back to the
        // types that did load so a single bad type doesn't disable scanning.
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        foreach (var type in types)
        {
            if (type is null || type.IsAbstract || type.IsInterface) continue;

            var scoped = type.GetCustomAttribute<FcmsScopedAttribute>();
            if (scoped is not null)
            {
                services.TryAddScoped(scoped.ServiceType ?? type, type);
                continue;
            }

            var singleton = type.GetCustomAttribute<FcmsSingletonAttribute>();
            if (singleton is not null)
            {
                services.TryAddSingleton(singleton.ServiceType ?? type, type);
                continue;
            }

            if (type.GetCustomAttribute<FcmsHostedServiceAttribute>() is not null
                && typeof(IHostedService).IsAssignableFrom(type))
            {
                services.AddSingleton(typeof(IHostedService), type);
            }
        }
    }
}
