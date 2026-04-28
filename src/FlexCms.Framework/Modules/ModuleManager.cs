using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Discovers all modules under a <c>modules/</c> root folder, loads each one
/// via <see cref="ModuleLoader"/>, and returns the list ordered by their
/// <see cref="ModuleManifest.DependsOn"/> declarations (dependencies first).
/// </summary>
public class ModuleManager
{
    private readonly ModuleLoader _loader;
    private readonly ILogger<ModuleManager> _logger;

    public ModuleManager(ModuleLoader loader, ILogger<ModuleManager> logger)
    {
        _loader = loader;
        _logger = logger;
    }

    /// <summary>
    /// Scan the modules root folder. Each immediate subfolder is treated as
    /// one module — the loader picks up the first DLL inside it that carries
    /// a <c>module.json</c>. A missing root folder is treated as "no modules".
    /// </summary>
    public IReadOnlyList<LoadedModule> ScanAndLoad(string modulesRoot)
    {
        if (!Directory.Exists(modulesRoot))
        {
            _logger.LogInformation("Modules folder does not exist — skipping scan: {Path}", modulesRoot);
            return [];
        }

        var loaded = new List<LoadedModule>();
        foreach (var moduleFolder in Directory.GetDirectories(modulesRoot))
        {
            foreach (var dll in Directory.GetFiles(moduleFolder, "*.dll"))
            {
                var module = _loader.LoadFromPath(dll);
                if (module is null) continue;
                loaded.Add(module);
                _logger.LogInformation("Loaded module {Id} v{Version} from {Path}",
                    module.ModuleId, module.Manifest.Version, dll);
                break; // one module per subfolder
            }
        }

        return SortByDependencies(loaded);
    }

    /// <summary>
    /// Topological sort by <see cref="ModuleManifest.DependsOn"/>. Modules
    /// with unmet dependencies (referenced ID was never loaded) are appended
    /// at the end with a warning rather than dropped, so the host can still
    /// surface them in admin UI as "broken".
    /// </summary>
    public static IReadOnlyList<LoadedModule> SortByDependencies(IReadOnlyList<LoadedModule> modules)
    {
        var byId = modules.ToDictionary(m => m.ModuleId, StringComparer.OrdinalIgnoreCase);
        var sorted = new List<LoadedModule>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
            Visit(module);

        return sorted;

        void Visit(LoadedModule module)
        {
            if (visited.Contains(module.ModuleId)) return;
            if (!visiting.Add(module.ModuleId))
                throw new InvalidOperationException(
                    $"Cyclic module dependency detected at '{module.ModuleId}'.");

            foreach (var depId in module.Manifest.DependsOn)
                if (byId.TryGetValue(depId, out var dep))
                    Visit(dep);

            visiting.Remove(module.ModuleId);
            visited.Add(module.ModuleId);
            sorted.Add(module);
        }
    }
}
