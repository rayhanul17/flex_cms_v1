using System.Reflection;

namespace FlexCms.Framework.Modules;

/// <summary>
/// A module that has been discovered and loaded into memory. Carries the
/// manifest, the assembly, and the activated <see cref="IFcmsModule"/> instance.
/// </summary>
public sealed class LoadedModule
{
    public LoadedModule(Assembly assembly, ModuleManifest manifest, IFcmsModule instance)
    {
        Assembly = assembly;
        Manifest = manifest;
        Instance = instance;
    }

    public Assembly Assembly { get; }
    public ModuleManifest Manifest { get; }
    public IFcmsModule Instance { get; }

    public string ModuleId => Manifest.ModuleId;
}
