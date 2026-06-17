using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Discovers all modules under a <c>modules/</c> root folder, loads each one
/// via <see cref="ModuleLoader"/>, and returns the list ordered by their
/// <see cref="ModuleManifest.DependsOn"/> declarations (dependencies first).
/// </summary>
public class ModuleManager
{
    /// <summary>Marker file name placed in a module folder to disable the module.</summary>
    public const string DisabledMarker = ".disabled";

    /// <summary>Marker file name that schedules folder deletion on next startup.</summary>
    public const string UninstallMarker = ".uninstall-pending";

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
    /// <remarks>
    /// Two file-based markers control lifecycle:
    /// <list type="bullet">
    ///   <item><c>.uninstall-pending</c> in a module folder → folder is deleted before scanning. Used to bypass Windows DLL locks.</item>
    ///   <item><c>.disabled</c> in a module folder → module is loaded but flagged as deactivated. The host skips its service registration and route mapping.</item>
    /// </list>
    /// </remarks>
    public IReadOnlyList<LoadedModule> ScanAndLoad(string modulesRoot)
    {
        if (!Directory.Exists(modulesRoot))
        {
            _logger.LogInformation("Modules folder does not exist — skipping scan: {Path}", modulesRoot);
            return [];
        }

        ProcessPendingUninstalls(modulesRoot);

        var loaded = new List<LoadedModule>();
        foreach (var moduleFolder in Directory.GetDirectories(modulesRoot))
        {
            var disabled = File.Exists(Path.Combine(moduleFolder, DisabledMarker));

            // 1) Try DLLs sitting at the folder root — that's where Upload writes
            //    them after extracting the package zip.
            // 2) Fall back to bin/{Release,Debug}/net*/  so source-controlled dev
            //    modules (scaffolded straight into modules/<Id>/) work without
            //    a manual copy step — the developer just runs `dotnet build` on
            //    the project and restarts the host.
            var candidates = Directory.GetFiles(moduleFolder, "*.dll")
                .Concat(SafeEnumerate(Path.Combine(moduleFolder, "bin", "Release")))
                .Concat(SafeEnumerate(Path.Combine(moduleFolder, "bin", "Debug")));

            foreach (var dll in candidates)
            {
                var module = _loader.LoadFromPath(dll, moduleFolder, disabled);
                if (module is null) continue;
                loaded.Add(module);
                _logger.LogInformation("Loaded module {Id} v{Version} (deactivated={Off}) from {Path}",
                    module.ModuleId, module.Manifest.Version, disabled, dll);

                // Warn when the folder name doesn't match ModuleId. Admin-uploaded
                // updates land in modules/{ModuleId}/ (see ModuleUpdateService), so
                // a dev-cloned folder with a different name would create a sibling
                // folder on first upload — both with valid DLLs, the load order
                // becomes the deciding factor. Matching the names eliminates the
                // footgun.
                var folderName = Path.GetFileName(moduleFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.Equals(folderName, module.ModuleId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Module {Id} loaded from folder '{Folder}' (mismatch). " +
                        "Admin uploads land in modules/<Id>/ — rename the folder to avoid duplicate-load.",
                        module.ModuleId, folderName);
                }
                break; // one module per subfolder
            }
        }

        return SortByDependencies(loaded);
    }

    /// <summary>
    /// Enumerate DLLs inside any <c>net*</c> subfolder of the given path,
    /// returning an empty sequence if the path doesn't exist (uncompiled
    /// project, missing bin/, etc.).
    /// </summary>
    private static IEnumerable<string> SafeEnumerate(string binRoot)
    {
        if (!Directory.Exists(binRoot)) yield break;
        foreach (var tfmDir in Directory.GetDirectories(binRoot, "net*"))
            foreach (var dll in Directory.GetFiles(tfmDir, "*.dll"))
                yield return dll;
    }

    private void ProcessPendingUninstalls(string modulesRoot)
    {
        foreach (var moduleFolder in Directory.GetDirectories(modulesRoot))
        {
            if (!File.Exists(Path.Combine(moduleFolder, UninstallMarker))) continue;

            try
            {
                Directory.Delete(moduleFolder, recursive: true);
                _logger.LogInformation("Uninstalled module folder: {Path}", moduleFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete module folder during uninstall: {Path}", moduleFolder);
            }
        }
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
