using FlexCms.Framework.Modules.SemVer;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Resolves a module's <c>DependsOn</c> entries against the currently
/// installed module set + their declared versions (Phase 15 — Issue 94).
///
/// <para>
/// Pure logic — takes snapshots in, returns a verdict. Lifted out of
/// <see cref="ModuleActivationService"/> so it's unit-testable without
/// spinning the full module loader.
/// </para>
/// </summary>
public static class ModuleDependencyChecker
{
    /// <summary>
    /// Validate that every entry in <paramref name="dependsOn"/> is satisfied
    /// by an entry in <paramref name="installed"/>. Returns the empty list
    /// when all good; otherwise a human-readable message per failure.
    /// </summary>
    /// <param name="dependsOn">Manifest <c>DependsOn</c> entries — e.g. <c>["BlogModule>=1.2.0", "CoreApi"]</c>.</param>
    /// <param name="installed">Map from installed module id → its current version string.</param>
    public static IReadOnlyList<string> Check(
        IEnumerable<string> dependsOn,
        IReadOnlyDictionary<string, string> installed)
    {
        var failures = new List<string>();
        foreach (var raw in dependsOn ?? [])
        {
            var (modId, constraint) = SemVerConstraint.ParseDependency(raw);
            if (string.IsNullOrEmpty(modId)) continue;

            if (!installed.TryGetValue(modId, out var installedVer))
            {
                failures.Add($"Required module '{modId}' is not installed.");
                continue;
            }

            // No version constraint → presence check only.
            if (constraint is null) continue;

            var actual = global::FlexCms.Framework.Modules.SemVer.SemVer.Parse(installedVer);
            if (actual is null)
            {
                failures.Add($"Module '{modId}' has unparseable version '{installedVer}'.");
                continue;
            }

            if (!constraint.IsSatisfiedBy(actual.Value))
            {
                failures.Add($"Module '{modId}' version '{actual}' does not satisfy '{constraint.Operator}{constraint.Version}'.");
            }
        }
        return failures;
    }
}
