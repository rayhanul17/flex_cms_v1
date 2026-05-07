namespace FlexCms.Framework.Modules.Api;

/// <summary>
/// Cross-module typed API resolver (Phase 17 — Issue 110). Modules expose
/// implementations of <c>[FcmsModuleApi]</c>-marked interfaces, register
/// them via DI, and other modules look them up here.
///
/// <para>
/// Why not just inject the interface directly via <c>IServiceProvider</c>?
/// Two reasons:
/// </para>
/// <list type="number">
///   <item>Graceful "module not loaded" — <see cref="Get{T}"/> returns null
///         instead of throwing when the providing module is deactivated /
///         uninstalled. Consumers render without crashing.</item>
///   <item>Version compatibility — caller can demand <c>>=1.2.0</c>; mismatch
///         returns null + a logged warning, so a module upgrade across a
///         breaking change degrades cleanly instead of NREing on a missing
///         method.</item>
/// </list>
/// </summary>
public interface IFcmsModuleApiRegistry
{
    /// <summary>
    /// Resolve an implementation of <typeparamref name="T"/>. Returns null
    /// if no module has registered one (e.g. provider deactivated) or if
    /// <paramref name="versionConstraint"/> is set + the registered
    /// version doesn't satisfy it.
    /// </summary>
    T? Get<T>(string? versionConstraint = null) where T : class;

    /// <summary>List every registered API + its version + display name. Admin diagnostic.</summary>
    IReadOnlyList<RegisteredModuleApi> List();
}

public sealed record RegisteredModuleApi(string InterfaceName, string Version, string? DisplayName, string ImplementationTypeName);
