using System.Reflection;

namespace FlexCms.Framework.Modules;

/// <summary>
/// A module that has been discovered and loaded into memory. Carries the
/// manifest, the assembly, and the activated <see cref="IFcmsModule"/> instance.
/// </summary>
public sealed class LoadedModule
{
    public LoadedModule(
        Assembly assembly,
        ModuleManifest manifest,
        IFcmsModule instance,
        string folderPath,
        bool isDeactivated)
    {
        Assembly = assembly;
        Manifest = manifest;
        Instance = instance;
        FolderPath = folderPath;
        IsDeactivated = isDeactivated;
    }

    public Assembly Assembly { get; }
    public ModuleManifest Manifest { get; }
    public IFcmsModule Instance { get; }

    /// <summary>Absolute path to the module's folder (parent of the DLL).</summary>
    public string FolderPath { get; }

    /// <summary>
    /// True when the module folder contains a <c>.disabled</c> marker file.
    /// Deactivated modules are loaded into memory (so admin UI can list them)
    /// but their <c>RegisterServices</c> and <c>AddApplicationPart</c> are
    /// skipped — effectively "soft uninstalled".
    /// </summary>
    public bool IsDeactivated { get; }

    public string ModuleId => Manifest.ModuleId;
}
