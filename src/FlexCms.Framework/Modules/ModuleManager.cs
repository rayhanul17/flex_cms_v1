using System.Security.Cryptography;
using System.Text.Json;
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
    private readonly IModuleTrustStore _trust;
    private readonly bool _allowTrustOnFirstUse;

    public ModuleManager(ModuleLoader loader, ILogger<ModuleManager> logger)
        : this(loader, logger, NullModuleTrustStore.Instance, allowTrustOnFirstUse: true) { }

    public ModuleManager(ModuleLoader loader, ILogger<ModuleManager> logger, IModuleTrustStore trust)
        : this(loader, logger, trust, allowTrustOnFirstUse: true) { }

    /// <summary>
    /// Construct the manager with full control over the trust policy.
    /// <paramref name="allowTrustOnFirstUse"/> = <c>true</c> lets the
    /// gate load a module DLL whose hash hasn't been recorded yet (the
    /// activator records it on success so the next boot enforces). Set
    /// <c>false</c> in production to refuse unknown modules outright —
    /// only previously-approved hashes load. See security-audit-recheck-2 §4.1.
    /// </summary>
    public ModuleManager(
        ModuleLoader loader,
        ILogger<ModuleManager> logger,
        IModuleTrustStore trust,
        bool allowTrustOnFirstUse)
    {
        _loader = loader;
        _logger = logger;
        _trust = trust;
        _allowTrustOnFirstUse = allowTrustOnFirstUse;
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
                // ── Pre-load integrity gate (security-audit-recheck §8.1, recheck-2 §4.2) ──
                // Read the embedded module.json + compute the DLL hash via
                // reflection-only metadata APIs BEFORE Assembly.LoadFrom
                // executes any module code. Tri-state result:
                //   NotModule     → not a module DLL (no embedded module.json)
                //                   — skip without calling Assembly.LoadFrom
                //                   so a transitive dep can never run static
                //                   initializers just because it sat next to
                //                   a module on disk
                //   InvalidModule → it IS a module DLL but its hash doesn't
                //                   match the trust store's approved hash
                //                   (or trust-on-first-use is disabled and
                //                    no record exists)
                //                   — refused outright
                //   ValidModule   → safe to load
                var integrity = PreLoadIntegrityCheck(dll, out var declaredId);
                if (integrity == PreLoadIntegrityResult.NotModule)
                    continue;  // not even a candidate; never reaches Assembly.LoadFrom

                if (integrity == PreLoadIntegrityResult.InvalidModule)
                {
                    _logger.LogError(
                        "Module DLL at '{Path}' (declared id '{Id}') failed pre-load integrity check — refusing to load.",
                        dll, declaredId);
                    continue;
                }

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
    /// Reflection-only check of a candidate DLL. Returns one of three
    /// states so the scanner can react precisely without loading any
    /// module code into the host's AssemblyLoadContext.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="System.Reflection.MetadataLoadContext"/> exclusively
    /// — no <c>Assembly.LoadFrom</c> until the result is
    /// <see cref="PreLoadIntegrityResult.ValidModule"/>.
    /// See security-audit-recheck §8.1 and recheck-2 §4.1, §4.2.
    /// </remarks>
    internal PreLoadIntegrityResult PreLoadIntegrityCheck(string dllPath, out string declaredModuleId)
    {
        declaredModuleId = "";

        // 1. Compute the file hash up front. Cheap and provider-independent.
        string fileHash;
        try
        {
            using var fs = File.OpenRead(dllPath);
            fileHash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PreLoadIntegrityCheck: could not hash {Path}", dllPath);
            // Can't hash → can't trust. Refuse outright.
            return PreLoadIntegrityResult.InvalidModule;
        }

        // 2. Read embedded module.json without executing any code from the DLL.
        var runtimeAssemblies = Directory.GetFiles(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
        var resolverPaths = new List<string>(runtimeAssemblies) { dllPath };
        try
        {
            var resolver = new System.Reflection.PathAssemblyResolver(resolverPaths);
            using var mlc = new System.Reflection.MetadataLoadContext(resolver);
            var asm = mlc.LoadFromAssemblyPath(dllPath);

            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("module.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName is null)
            {
                // Not a module DLL at all — almost certainly a transitive
                // dep that happened to sit next to a real module under
                // bin/Debug/. Skip it without Assembly.LoadFrom so any
                // module-initializer/static-constructor code in a hostile
                // dep never executes.
                return PreLoadIntegrityResult.NotModule;
            }

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null) return PreLoadIntegrityResult.NotModule;
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("ModuleId", out var idProp))
                declaredModuleId = idProp.GetString() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PreLoadIntegrityCheck: metadata read failed for {Path}", dllPath);
            // The DLL claimed to be readable (we hashed it) but the
            // metadata reader rejected it — treat as a broken module
            // package rather than a non-module file.
            return PreLoadIntegrityResult.InvalidModule;
        }

        if (string.IsNullOrEmpty(declaredModuleId))
        {
            // Embedded module.json exists but has no ModuleId — malformed
            // module package, refuse rather than guess.
            _logger.LogError("PreLoadIntegrityCheck: {Path} has module.json but no ModuleId.", dllPath);
            return PreLoadIntegrityResult.InvalidModule;
        }

        // 3. Compare against the trust store.
        var approved = _trust.GetApprovedHash(declaredModuleId);

        if (approved is null)
        {
            // No record yet — first time we've seen this module.
            // - TOFU enabled (dev / fresh install): load, activator
            //   records the hash for next boot.
            // - TOFU disabled (production): refuse. Operator must
            //   approve via the upload flow first.
            if (_allowTrustOnFirstUse)
            {
                if (_trust.IsAvailable)
                    _logger.LogInformation(
                        "PreLoadIntegrityCheck: no approved hash recorded yet for {Id}; trust-on-first-use.",
                        declaredModuleId);
                return PreLoadIntegrityResult.ValidModule;
            }

            _logger.LogError(
                "PreLoadIntegrityCheck: refusing {Id} — no approved hash recorded and trust-on-first-use is disabled. " +
                "Re-upload via /admin/modules to record an approved hash.",
                declaredModuleId);
            return PreLoadIntegrityResult.InvalidModule;
        }

        if (!string.Equals(approved, fileHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "PreLoadIntegrityCheck: DLL tampering detected for {Id} — approved {Approved}, current {Current}.",
                declaredModuleId, approved[..Math.Min(12, approved.Length)], fileHash[..12]);
            return PreLoadIntegrityResult.InvalidModule;
        }

        return PreLoadIntegrityResult.ValidModule;
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
