using FlexCms.Framework.Clock;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Performs the file-system lifecycle operations on a module folder:
/// activate (delete <c>.disabled</c> marker), deactivate (create the marker),
/// uninstall (mark for deletion-on-next-startup).
///
/// All operations are pure file IO. The host must be restarted afterwards
/// for the new state to take effect — DI is built once at startup.
/// </summary>
public class ModuleStateService
{
    private readonly ILogger<ModuleStateService> _logger;

    public ModuleStateService(ILogger<ModuleStateService> logger) => _logger = logger;

    /// <summary>
    /// Remove the <c>.disabled</c> marker from the module folder. The module
    /// will be wired on next host startup.
    /// </summary>
    public bool Activate(string moduleFolder)
    {
        if (!Directory.Exists(moduleFolder))
        {
            _logger.LogWarning("Activate: module folder does not exist: {Path}", moduleFolder);
            return false;
        }

        var marker = Path.Combine(moduleFolder, ModuleManager.DisabledMarker);
        if (File.Exists(marker)) File.Delete(marker);
        return true;
    }

    /// <summary>
    /// Drop a <c>.disabled</c> marker into the module folder. The module will
    /// be discovered but skipped during wiring on next host startup.
    /// </summary>
    public bool Deactivate(string moduleFolder)
    {
        if (!Directory.Exists(moduleFolder))
        {
            _logger.LogWarning("Deactivate: module folder does not exist: {Path}", moduleFolder);
            return false;
        }

        var marker = Path.Combine(moduleFolder, ModuleManager.DisabledMarker);
        File.WriteAllText(marker, $"Deactivated at {FcmsTime.Now:O}\n");
        return true;
    }

    /// <summary>
    /// Drop a <c>.uninstall-pending</c> marker into the module folder. On the
    /// next host startup, <see cref="ModuleManager"/> deletes the entire folder
    /// before scanning — bypassing Windows file-locking on the loaded DLL.
    /// </summary>
    public bool Uninstall(string moduleFolder)
    {
        if (!Directory.Exists(moduleFolder))
        {
            _logger.LogWarning("Uninstall: module folder does not exist: {Path}", moduleFolder);
            return false;
        }

        var marker = Path.Combine(moduleFolder, ModuleManager.UninstallMarker);
        File.WriteAllText(marker, $"Uninstall scheduled at {FcmsTime.Now:O}\n");
        return true;
    }


    /// <summary>
    /// Copy <c>{moduleFolder}/wwwroot/</c> → <c>{webRootPath}/modules/{moduleId}/</c>.
    /// Called on activation so module CSS/JS are reachable at runtime.
    /// No-op when the module has no wwwroot folder.
    /// <para>
    /// The <c>uploads/</c> subfolder is deliberately skipped — uploads are
    /// runtime user data, written directly under
    /// <c>modules/&lt;id&gt;/wwwroot/uploads/</c> by
    /// <c>IFcmsFileUploadService</c> and served from there by
    /// <c>UseFcmsModuleStaticFiles</c>. Copying them into the host wwwroot
    /// would double the disk footprint and create a stale copy that
    /// silently diverges from the authoritative one.
    /// </para>
    /// </summary>
    public void SyncWwwroot(string moduleFolder, string webRootPath, string moduleId)
    {
        var src = Path.Combine(moduleFolder, "wwwroot");
        if (!Directory.Exists(src)) return;

        var dest = Path.Combine(webRootPath, "modules", moduleId);
        CopyDirectory(src, dest, skipTopLevel: "uploads");
        _logger.LogInformation("Module {Id}: wwwroot synced to {Dest}", moduleId, dest);
    }

    /// <summary>
    /// Delete <c>{webRootPath}/modules/{moduleId}/</c>.
    /// Called on deactivation and uninstall.
    /// </summary>
    public void DeleteWwwroot(string webRootPath, string moduleId)
    {
        var dest = Path.Combine(webRootPath, "modules", moduleId);
        if (!Directory.Exists(dest)) return;

        try
        {
            Directory.Delete(dest, recursive: true);
            _logger.LogInformation("Module {Id}: wwwroot removed from {Dest}", moduleId, dest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Id}: failed to remove wwwroot at {Dest}", moduleId, dest);
        }
    }

    private static void CopyDirectory(string src, string dest, string? skipTopLevel = null)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
        {
            var name = Path.GetFileName(dir);
            if (skipTopLevel is not null && string.Equals(name, skipTopLevel, StringComparison.OrdinalIgnoreCase))
                continue;
            CopyDirectory(dir, Path.Combine(dest, name));
        }
    }
}
