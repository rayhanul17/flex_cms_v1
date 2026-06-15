using System.IO.Compression;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Modules;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Host.Controllers.Admin;

[FcmsAuthorize(FcmsPermissions.SystemManage)]
[Route("admin/modules")]
public class ModulesController : BaseAdminController
{
    private readonly ModuleRegistry _registry;
    private readonly ModuleStateService _state;
    private readonly IRepository<FcmsModuleRecord> _records;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IWebHostEnvironment _env;

    public ModulesController(
        ModuleRegistry registry,
        ModuleStateService state,
        IRepository<FcmsModuleRecord> records,
        IFcmsUnitOfWork uow,
        IHostApplicationLifetime lifetime,
        IWebHostEnvironment env)
    {
        _registry = registry;
        _state = state;
        _records = records;
        _uow = uow;
        _lifetime = lifetime;
        _env = env;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var records = (await _records.GetAllAsync(ct))
            .ToDictionary(r => r.ModuleId, StringComparer.OrdinalIgnoreCase);

        var vm = new ModuleListViewModel
        {
            Modules = _registry.Modules.Select(m =>
            {
                records.TryGetValue(m.ModuleId, out var rec);
                return new ModuleListItem
                {
                    ModuleId = m.ModuleId,
                    ModuleName = m.Manifest.ModuleName,
                    Version = m.Manifest.Version,
                    Author = m.Manifest.Author,
                    Email = m.Manifest.Email,
                    Website = m.Manifest.Website,
                    Category = m.Manifest.Category,
                    Description = m.Manifest.Description,
                    TablePrefix = m.Manifest.TablePrefix,
                    MinFrameworkVersion = m.Manifest.MinFrameworkVersion,
                    Status = m.IsDeactivated ? "Inactive" : (rec?.ActivationStatus ?? "Active"),
                    ActivatedAt = rec?.ActivatedAt,
                    LastActivationAttemptAt = rec?.LastActivationAttemptAt,
                    ActivationError = rec?.ActivationError,
                    DependsOn = m.Manifest.DependsOn,
                    RequestedPermissionsCount = m.Manifest.RequestedPermissions.Length
                };
            }).ToList()
        };

        ViewBag.IsDevelopment = _env.IsDevelopment();
        return View(vm);
    }

    [HttpPost("activate/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(string id, CancellationToken ct)
    {
        var module = _registry.FindById(id);
        if (module is null) return FcmsFail("Module not found.");

        if (!_state.Activate(module.FolderPath))
            return FcmsFail("Could not activate module — folder missing.");

        _state.SyncWwwroot(module.FolderPath, _env.WebRootPath, module.ModuleId);
        await OpLog.LogAsync(FcmsAuditActions.ModuleActivated, nameof(FcmsModuleRecord), module.ModuleId,
            value: new { module.ModuleId, module.Manifest.Version }, module: "core", ct: ct);
        return FcmsOk("Module activated. Restart the app to apply.");
    }

    [HttpPost("deactivate/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(string id, CancellationToken ct)
    {
        var module = _registry.FindById(id);
        if (module is null) return FcmsFail("Module not found.");

        if (!_state.Deactivate(module.FolderPath))
            return FcmsFail("Could not deactivate module — folder missing.");

        _state.DeleteWwwroot(_env.WebRootPath, module.ModuleId);

        // Hide module menu items so they don't 404 on click after restart.
        // Restored on next activation by MenuService.SeedAsync (Status set back to Active).
        var menuService = HttpContext.RequestServices.GetService<IMenuService>();
        if (menuService is not null)
            await menuService.RemoveModuleItemsAsync(id, ct);

        await OpLog.LogAsync(FcmsAuditActions.ModuleDeactivated, nameof(FcmsModuleRecord), module.ModuleId,
            value: new { module.ModuleId, module.Manifest.Version }, module: "core", ct: ct);
        return FcmsOk("Module deactivated. Restart the app to apply.");
    }

    [HttpPost("uninstall/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Uninstall(string id, [FromForm] string confirmName, [FromForm] bool dropTables, CancellationToken ct)
    {
        var module = _registry.FindById(id);
        if (module is null) return FcmsFail("Module not found.");

        // Safety check — admin must type the module name to confirm
        if (!string.Equals(confirmName, module.Manifest.ModuleName, StringComparison.Ordinal))
            return FcmsFail($"Confirmation does not match. Type exactly: {module.Manifest.ModuleName}");

        if (!_state.Uninstall(module.FolderPath))
            return FcmsFail("Could not schedule uninstall — folder missing.");

        _state.DeleteWwwroot(_env.WebRootPath, module.ModuleId);

        // Drop tables if requested (runs before the DLL is locked on next startup)
        if (dropTables)
        {
            var opts = HttpContext.RequestServices.GetRequiredService<ModuleActivationOptions>();
            try
            {
                await module.Instance.DropTablesAsync(opts.ConnectionString, opts.Provider, ct);
            }
            catch (Exception ex)
            {
                var logger = HttpContext.RequestServices
                    .GetRequiredService<ILogger<ModulesController>>();
                logger.LogError(ex, "Module {Id}: DropTablesAsync failed.", id);
            }
        }

        // Remove module menu items
        var menuService = HttpContext.RequestServices.GetService<IMenuService>();
        if (menuService is not null)
            await menuService.RemoveModuleItemsAsync(id, ct);

        // Soft-delete the DB record now (folder will be removed on next startup
        // by ModuleManager.ProcessPendingUninstalls)
        var record = (await _records.GetAllAsync(ct))
            .FirstOrDefault(r => string.Equals(r.ModuleId, id, StringComparison.OrdinalIgnoreCase));
        if (record is not null)
        {
            record.Status = FlexCms.Framework.Db.EntityStatus.Deleted;
            record.DeletedAt ??= FlexCms.Framework.Clock.FcmsTime.Now;
            await _records.UpdateAsync(record, ct);
            await _uow.SaveChangesAsync(ct);
        }

        await OpLog.LogAsync(FcmsAuditActions.ModuleUninstalled, nameof(FcmsModuleRecord), module.ModuleId,
            value: new { module.ModuleId, module.Manifest.Version, dropTables }, module: "core", ct: ct);
        return FcmsOk("Module marked for uninstall. Restart the app to remove its files.");
    }

    [HttpPost("restart")]
    [ValidateAntiForgeryToken]
    public IActionResult Restart()
    {
        Response.OnCompleted(() =>
        {
            _lifetime.StopApplication();
            return Task.CompletedTask;
        });
        return FcmsOk("Restart triggered.");
    }

    // ── Upload a module ZIP ───────────────────────────────────────────────────
    // Accepts a ZIP whose root contains the module DLL + module.json (or a
    // single top-level folder containing them). Extracts under the solution's
    // modules/ directory; the module is loaded on the next app restart.
    //
    // Security: rejects path-traversal entries (anything with .. or absolute
    // paths) and refuses to overwrite an existing module folder unless the
    // caller passes overwrite=true.

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile? file, bool overwrite = false, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return FcmsFail("No file uploaded.");
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return FcmsFail("Module package must be a .zip file.");

        var solutionRoot = FindSolutionRoot(_env.ContentRootPath);
        if (solutionRoot is null) return FcmsFail("Could not locate the solution root.");

        var modulesRoot = Path.Combine(solutionRoot, "modules");
        Directory.CreateDirectory(modulesRoot);

        // Stage to a temp folder first so a malformed ZIP doesn't pollute
        // modules/ with a half-extracted directory.
        var stagingDir = Path.Combine(Path.GetTempPath(), "fcms_module_upload_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);

        try
        {
            await using (var fs = System.IO.File.Create(Path.Combine(stagingDir, file.FileName), 81920, FileOptions.Asynchronous))
                await file.CopyToAsync(fs, ct);

            using var archive = System.IO.Compression.ZipFile.OpenRead(Path.Combine(stagingDir, file.FileName));
            var extractDir = Path.Combine(stagingDir, "extracted");
            Directory.CreateDirectory(extractDir);

            foreach (var entry in archive.Entries)
            {
                // Path-traversal guard
                if (entry.FullName.Contains("..") || Path.IsPathRooted(entry.FullName))
                    return FcmsFail($"Refusing unsafe path in archive: {entry.FullName}");

                var dest = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
                if (!dest.StartsWith(Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    return FcmsFail($"Refusing escape from extract dir: {entry.FullName}");

                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                ZipFileExtensions.ExtractToFile(entry, dest, overwrite: true);
            }

            // Find module.json — either at extract root or one folder deep
            var manifestPath = Directory.GetFiles(extractDir, "module.json", SearchOption.AllDirectories)
                .OrderBy(p => p.Length).FirstOrDefault();
            if (manifestPath is null) return FcmsFail("Archive does not contain module.json.");

            var moduleSrcDir = Path.GetDirectoryName(manifestPath)!;
            var manifestJson = await System.IO.File.ReadAllTextAsync(manifestPath, ct);
            string? moduleId;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);
                moduleId = doc.RootElement.TryGetProperty("ModuleId", out var v) ? v.GetString() : null;
            }
            catch (Exception ex) { return FcmsFail($"module.json is invalid JSON: {ex.Message}"); }

            if (string.IsNullOrWhiteSpace(moduleId))
                return FcmsFail("module.json is missing the ModuleId property.");
            if (moduleId.Contains('/') || moduleId.Contains('\\') || moduleId.Contains(".."))
                return FcmsFail($"ModuleId contains invalid characters: {moduleId}");

            var dest2 = Path.Combine(modulesRoot, moduleId);
            if (Directory.Exists(dest2) && !overwrite)
                return FcmsFail($"A module folder named \"{moduleId}\" already exists. Re-upload with the overwrite option to replace it.");
            if (Directory.Exists(dest2)) Directory.Delete(dest2, recursive: true);

            CopyDirectory(moduleSrcDir, dest2);
            await OpLog.LogAsync(FcmsAuditActions.ModuleUploaded, nameof(FcmsModuleRecord), moduleId,
                value: new { moduleId, fileName = file.FileName, overwrite }, module: "core", ct: ct);
            return FcmsOk($"Module \"{moduleId}\" uploaded. Restart the app to load it.", new { moduleId });
        }
        catch (InvalidDataException)
        {
            return FcmsFail("File is not a valid ZIP archive.");
        }
        catch (Exception ex)
        {
            return FcmsFail($"Upload failed: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(stagingDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dest, StringComparison.Ordinal));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            System.IO.File.Copy(file, file.Replace(src, dest, StringComparison.Ordinal), overwrite: true);
    }

    // ── Dev-mode scaffold ─────────────────────────────────────────────────────

    [HttpGet("scaffold")]
    public IActionResult Scaffold()
    {
        if (!_env.IsDevelopment()) return NotFound();
        return View(new ScaffoldModuleViewModel());
    }

    [HttpPost("scaffold")]
    [ValidateAntiForgeryToken]
    public IActionResult ScaffoldPost(ScaffoldModuleViewModel model)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (!ModelState.IsValid) return View("Scaffold", model);

        // Walk up from ContentRootPath until we find both the templates and modules dirs.
        // Handles both src/FlexCms.Host (project) and bin/Debug/net10.0 (running output) layouts.
        var solutionRoot = FindSolutionRoot(_env.ContentRootPath);
        if (solutionRoot is null)
            return FcmsFail("Could not locate the solution root (looked for a 'templates' sibling).");

        var modulesRoot = Path.Combine(solutionRoot, "modules");
        Directory.CreateDirectory(modulesRoot);
        var dest = Path.Combine(modulesRoot, model.ModuleId);

        if (Directory.Exists(dest))
        {
            ModelState.AddModelError(nameof(model.ModuleId), "A module folder with this ID already exists.");
            return View("Scaffold", model);
        }

        var templateSrc = Path.Combine(solutionRoot, "templates", "flexcms-module", "content", "FlexCms.Module.Name");
        if (!Directory.Exists(templateSrc))
            return FcmsFail($"Template source not found at: {templateSrc}");

        CopyAndReplace(templateSrc, dest, model.ModuleId, model.TablePrefix);
        return FcmsOk($"Module '{model.ModuleId}' scaffolded to modules/{model.ModuleId}/. Open the project, implement your module, build, and restart.");
    }

    /// <summary>
    /// Walk up from <paramref name="start"/> looking for a directory that contains
    /// a <c>templates</c> subfolder — that's the FlexCMS solution root.
    /// </summary>
    private static string? FindSolutionRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "templates", "flexcms-module")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static void CopyAndReplace(string src, string dest, string moduleId, string tablePrefix)
    {
        Directory.CreateDirectory(dest);
        var shortName = moduleId.Split('.').Last();
        var moduleIdLower = moduleId.ToLowerInvariant();

        // Token order matters: long, distinct tokens first so they don't get
        // shadowed by shorter ones (e.g. __ShortName__ vs Name). All template
        // placeholders use double-underscore fences to avoid colliding with
        // ordinary identifiers like "Name" inside docs/comments.
        string Apply(string s) => s
            .Replace("FlexCms.Module.Name", moduleId)
            .Replace("FlexCms_Module_Name", moduleId.Replace(".", "_"))
            .Replace("flexcms.module.name", moduleIdLower)
            .Replace("__ModuleId__", moduleId)
            .Replace("__ModuleIdLower__", moduleIdLower)
            .Replace("__ShortName__", shortName)
            .Replace("__shortname__", shortName.ToLowerInvariant())
            .Replace("mod_prefix", tablePrefix);

        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var destRel = Apply(rel);

            var destFile = Path.Combine(dest, destRel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            var content = System.IO.File.ReadAllText(file);
            System.IO.File.WriteAllText(destFile, Apply(content));
        }
    }
}
