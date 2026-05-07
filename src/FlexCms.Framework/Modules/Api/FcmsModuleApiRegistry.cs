using FlexCms.Framework.Modules.SemVer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Modules.Api;

public sealed class FcmsModuleApiRegistry : IFcmsModuleApiRegistry
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<FcmsModuleApiRegistry> _logger;

    public FcmsModuleApiRegistry(IServiceProvider sp, ILogger<FcmsModuleApiRegistry> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    public T? Get<T>(string? versionConstraint = null) where T : class
    {
        var attr = typeof(T).GetCustomAttributes(typeof(FcmsModuleApiAttribute), inherit: false)
            .Cast<FcmsModuleApiAttribute>()
            .FirstOrDefault();

        if (attr is null)
        {
            // Defensive: caller annotated something else with FcmsModuleApi
            // expectation — return whatever DI has, behavior matches plain
            // GetService<T>(). Logged so the typo shows up.
            _logger.LogDebug("Get<{Type}> called without [FcmsModuleApi] on the interface.", typeof(T).Name);
            return _sp.GetService<T>();
        }

        var impl = _sp.GetService<T>();
        if (impl is null) return null;

        if (string.IsNullOrWhiteSpace(versionConstraint)) return impl;

        var constraint = SemVerConstraint.Parse(versionConstraint);
        var actual = global::FlexCms.Framework.Modules.SemVer.SemVer.Parse(attr.Version);
        if (constraint is null || actual is null)
        {
            _logger.LogWarning("Get<{Type}>: malformed version (declared='{Decl}', constraint='{C}'). Returning null.",
                typeof(T).Name, attr.Version, versionConstraint);
            return null;
        }

        if (!constraint.IsSatisfiedBy(actual.Value))
        {
            _logger.LogWarning("Get<{Type}>: declared version {Decl} does not satisfy '{C}'. Returning null.",
                typeof(T).Name, attr.Version, versionConstraint);
            return null;
        }

        return impl;
    }

    public IReadOnlyList<RegisteredModuleApi> List()
    {
        // Reflect over every service registration whose service type is an
        // interface marked with [FcmsModuleApi]. Built lazily on demand;
        // the registrations themselves are cheap to enumerate.
        var result = new List<RegisteredModuleApi>();
        foreach (var sd in EnumerateRegisteredServices())
        {
            var attr = sd.GetCustomAttributes(typeof(FcmsModuleApiAttribute), inherit: false)
                .Cast<FcmsModuleApiAttribute>()
                .FirstOrDefault();
            if (attr is null) continue;

            var impl = _sp.GetService(sd);
            result.Add(new RegisteredModuleApi(
                InterfaceName: sd.FullName ?? sd.Name,
                Version: attr.Version,
                DisplayName: attr.DisplayName ?? sd.Name,
                ImplementationTypeName: impl?.GetType().FullName ?? "(none)"));
        }
        return result;
    }

    private static IEnumerable<Type> EnumerateRegisteredServices()
    {
        // We don't have direct access to IServiceCollection at runtime.
        // Walk loaded assemblies for interfaces with our attribute — that's
        // the universe modules can register against. Cheap one-shot for
        // List() (admin call).
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            foreach (var t in types)
            {
                if (t is null || !t.IsInterface) continue;
                if (t.GetCustomAttributes(typeof(FcmsModuleApiAttribute), inherit: false).Length > 0)
                    yield return t;
            }
        }
    }
}
