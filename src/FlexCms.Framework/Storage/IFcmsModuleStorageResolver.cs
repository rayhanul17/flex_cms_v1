namespace FlexCms.Framework.Storage;

/// <summary>
/// Resolves a moduleId into the physical filesystem path + public URL prefix
/// where uploads for that module should land. Modules own their own
/// <c>wwwroot/uploads/</c> folder so:
/// <list type="bullet">
/// <item>The host's <c>wwwroot/</c> stays clean — only host-managed static
///   assets (admin CSS/JS, media library) live there.</item>
/// <item>Each module's data is self-contained — uninstall a module by
///   deleting its folder, no orphaned files in the host wwwroot.</item>
/// <item>Backups + restores are per-module.</item>
/// <item>The parent <c>.gitignore</c> stays simple (it already ignores the
///   whole <c>modules/&lt;id&gt;/</c> folder).</item>
/// </list>
/// </summary>
public sealed record ModuleStorageTarget(string PhysicalDirectory, string PublicUrlBase);

public interface IFcmsModuleStorageResolver
{
    /// <summary>
    /// Return the physical folder + URL prefix for a module's uploads.
    /// Pass <c>null</c> for host-level uploads (legacy / media library).
    /// </summary>
    ModuleStorageTarget Resolve(string? moduleId);
}
