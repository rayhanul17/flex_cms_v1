using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Modules;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Host.Controllers.Admin;

[FcmsAuthorize]
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
                    Description = m.Manifest.Description,
                    TablePrefix = m.Manifest.TablePrefix,
                    Status = m.IsDeactivated ? "Inactive" : (rec?.Status ?? "Active"),
                    ActivatedAt = rec?.ActivatedAt,
                    DependsOn = m.Manifest.DependsOn
                };
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost("activate/{id}")]
    [ValidateAntiForgeryToken]
    public IActionResult Activate(string id)
    {
        var module = _registry.FindById(id);
        if (module is null) return FcmsFail("Module not found.");

        if (!_state.Activate(module.FolderPath))
            return FcmsFail("Could not activate module — folder missing.");

        _state.SyncWwwroot(module.FolderPath, _env.WebRootPath, module.ModuleId);
        return FcmsOk("Module activated. Restart the app to apply.");
    }

    [HttpPost("deactivate/{id}")]
    [ValidateAntiForgeryToken]
    public IActionResult Deactivate(string id)
    {
        var module = _registry.FindById(id);
        if (module is null) return FcmsFail("Module not found.");

        if (!_state.Deactivate(module.FolderPath))
            return FcmsFail("Could not deactivate module — folder missing.");

        _state.DeleteWwwroot(_env.WebRootPath, module.ModuleId);
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

        // Soft-delete the DB record now (folder will be removed on next startup
        // by ModuleManager.ProcessPendingUninstalls)
        var record = (await _records.GetAllAsync(ct))
            .FirstOrDefault(r => string.Equals(r.ModuleId, id, StringComparison.OrdinalIgnoreCase));
        if (record is not null)
        {
            record.IsDeleted = true;
            await _records.UpdateAsync(record, ct);
            await _uow.SaveChangesAsync(ct);
        }

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
}
