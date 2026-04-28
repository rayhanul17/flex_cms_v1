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
        File.WriteAllText(marker, $"Deactivated at {DateTime.UtcNow:O}\n");
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
        File.WriteAllText(marker, $"Uninstall scheduled at {DateTime.UtcNow:O}\n");
        return true;
    }
}
