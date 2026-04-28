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

    /// <summary>"Inactive" | "Active" — controlled by the activation flow.</summary>
    public string Status { get; set; } = "Inactive";

    /// <summary>True once <c>SeedDataAsync</c> has run successfully.</summary>
    public bool SeedCompleted { get; set; }

    public DateTime? ActivatedAt { get; set; }
}
