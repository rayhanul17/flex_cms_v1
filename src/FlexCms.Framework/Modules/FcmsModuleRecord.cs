using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Persisted record of a module the host has discovered. Tracks activation
/// state so the framework can replay activation steps after a restart and
/// surface module status in admin UI.
/// </summary>
public class FcmsModuleRecord : BaseEfEntity
{
    public string ModuleId { get; set; } = "";
    public string Version { get; set; } = "";

    /// <summary>"Inactive" | "Active" — controlled by the activation flow.
    /// Distinct from <see cref="BaseEfEntity.Status"/> (entity lifecycle / soft-delete).</summary>
    public string ActivationStatus { get; set; } = "Inactive";

    /// <summary>True once <c>SeedDataAsync</c> has run successfully.</summary>
    public bool SeedCompleted { get; set; }

    public DateTime? ActivatedAt { get; set; }

    /// <summary>
    /// Phase 15 / Issue 95: snapshot of capabilities the operator approved
    /// at install time. Stored as comma-separated. Empty = no capabilities
    /// approved yet (must be set before activation if the manifest declares any).
    /// </summary>
    public string ApprovedCapabilities { get; set; } = "";

    /// <summary>Phase 15 / Issue 95: who approved + when, for audit.</summary>
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// Timestamp of the most recent activation attempt — set on every startup
    /// pass of <see cref="ModuleActivationService"/>, regardless of outcome.
    /// </summary>
    public DateTime? LastActivationAttemptAt { get; set; }

    /// <summary>
    /// Last error captured while activating this module (migration failure,
    /// seed failure, permission seed failure, etc.). Cleared on a successful
    /// activation pass. The admin module list renders this as a red badge
    /// + tooltip so operators see the problem immediately.
    /// </summary>
    public string? ActivationError { get; set; }
}
