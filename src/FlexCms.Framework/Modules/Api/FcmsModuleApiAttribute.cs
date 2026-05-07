namespace FlexCms.Framework.Modules.Api;

/// <summary>
/// Marks a public interface as a stable cross-module API surface
/// (Phase 17 — Issue 110). Modules that expose APIs annotate the
/// interface with <c>[FcmsModuleApi("1.0.0")]</c>; consumers resolve
/// the implementation through <see cref="IFcmsModuleApiRegistry"/> with
/// an optional version constraint.
///
/// <para>
/// Versioning is SemVer (see <see cref="FlexCms.Framework.Modules.SemVer.SemVerConstraint"/>).
/// Bumping the major version signals a breaking contract change; minor /
/// patch bumps are additive.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class FcmsModuleApiAttribute : Attribute
{
    /// <summary>SemVer of the contract — bump major on breaking change.</summary>
    public string Version { get; }

    /// <summary>
    /// Optional human-readable name. Defaults to the interface name when
    /// unspecified.
    /// </summary>
    public string? DisplayName { get; init; }

    public FcmsModuleApiAttribute(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version required.", nameof(version));
        Version = version;
    }
}
