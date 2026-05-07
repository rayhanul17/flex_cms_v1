namespace FlexCms.Framework.Modules.Updates;

/// <summary>
/// Module update workflow (Phase 15 — Issue 93): swap a module's binaries
/// in place, run any pending DB migrations, mark the new version active.
/// On migration failure, restore the previous binaries from a sibling
/// backup folder + revert the DB record so the operator is back at the
/// known-good version with no manual recovery.
///
/// <para>
/// Single-instance / file-based; matches FlexCMS's deployment model.
/// Multi-node deployments need an orchestrated rollout (out of scope).
/// </para>
/// </summary>
public interface IModuleUpdateService
{
    /// <summary>
    /// Apply <paramref name="newPackage"/> (a file or folder containing the
    /// new module binaries + module.json) over the existing module folder.
    /// Returns the outcome — on failure the result includes whether the
    /// rollback succeeded.
    /// </summary>
    Task<ModuleUpdateResult> UpdateAsync(string moduleId, string newPackagePath, CancellationToken ct = default);
}

public sealed record ModuleUpdateResult(
    bool Success,
    string? FromVersion,
    string? ToVersion,
    string? Error = null,
    bool RolledBack = false);
