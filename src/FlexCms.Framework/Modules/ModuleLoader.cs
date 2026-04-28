using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Loads a single module from a path on disk OR from an already-loaded
/// <see cref="Assembly"/>. Reads the embedded <c>module.json</c> manifest
/// and instantiates the type that implements <see cref="IFcmsModule"/>.
/// </summary>
public class ModuleLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ModuleLoader> _logger;

    public ModuleLoader(ILogger<ModuleLoader> logger) => _logger = logger;

    /// <summary>
    /// Load a module from a DLL path on disk. Returns <c>null</c> if the DLL
    /// has no <c>module.json</c> embedded, no <see cref="IFcmsModule"/>-implementing
    /// type, or fails to instantiate. Errors are logged but never thrown — a
    /// broken module must not crash the host.
    /// </summary>
    public LoadedModule? LoadFromPath(string dllPath, string folderPath, bool isDeactivated)
    {
        try
        {
            var assembly = Assembly.LoadFrom(dllPath);
            return LoadFromAssembly(assembly, folderPath, isDeactivated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load module DLL: {Path}", dllPath);
            return null;
        }
    }

    /// <summary>
    /// Load a module from an already-loaded assembly. Lets tests construct
    /// modules in-memory without writing DLLs to disk.
    /// </summary>
    public LoadedModule? LoadFromAssembly(Assembly assembly, string folderPath = "", bool isDeactivated = false)
    {
        var manifest = ReadManifest(assembly);
        if (manifest is null)
        {
            _logger.LogWarning("Assembly {Name} has no module.json embedded — skipping.", assembly.FullName);
            return null;
        }

        var moduleType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IFcmsModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

        if (moduleType is null)
        {
            _logger.LogWarning("Assembly {Name} has no IFcmsModule implementation — skipping.", assembly.FullName);
            return null;
        }

        try
        {
            var instance = (IFcmsModule)Activator.CreateInstance(moduleType)!;
            return new LoadedModule(assembly, manifest, instance, folderPath, isDeactivated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to instantiate module type {Type}.", moduleType.FullName);
            return null;
        }
    }

    private static ModuleManifest? ReadManifest(Assembly assembly)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("module.json", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;

        try
        {
            return JsonSerializer.Deserialize<ModuleManifest>(stream, JsonOpts);
        }
        catch
        {
            return null;
        }
    }
}
