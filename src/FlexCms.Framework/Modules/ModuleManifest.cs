namespace FlexCms.Framework.Modules;

/// <summary>
/// Deserialized contents of a module's <c>module.json</c> file. Every module
/// DLL must embed a <c>module.json</c> resource with these fields.
/// </summary>
public class ModuleManifest
{
    public string ModuleId { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Optional author / support email — shown on the module list page.</summary>
    public string Email { get; set; } = "";

    /// <summary>
    /// Optional homepage / docs URL — rendered as a clickable link on the
    /// module list page. Validated against http(s) before display so a
    /// malformed value never produces a broken anchor.
    /// </summary>
    public string Website { get; set; } = "";

    /// <summary>
    /// Optional broad category for grouping in the admin module list and a
    /// future marketplace ("Commerce", "CRM", "Finance", "Education", etc.).
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>Minimum FlexCms framework version this module requires.</summary>
    public string MinFrameworkVersion { get; set; } = "1.0.0";

    /// <summary>Table prefix for entities (e.g. "blog" → "blog_posts").</summary>
    public string TablePrefix { get; set; } = "";

    /// <summary>Module IDs that must be loaded before this one.</summary>
    public string[] DependsOn { get; set; } = [];

    /// <summary>Permission keys this module needs declared up-front.</summary>
    public string[] RequestedPermissions { get; set; } = [];

    /// <summary>
    /// Sandbox manifest (Phase 15 — Issue 95): coarse-grained capability
    /// declarations the operator approves before activation. Keep these
    /// simple intent strings rather than mirroring OS/AppDomain permissions
    /// — this is admin-readable consent, not enforcement.
    /// </summary>
    /// <example>["filesystem.read", "outbound.http", "send.email"]</example>
    public string[] RequestedCapabilities { get; set; } = [];
}
