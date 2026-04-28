using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Modules;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[FcmsAuthorize]
[Route("admin/modules")]
public class ModulesController : BaseAdminController
{
    private readonly ModuleRegistry _registry;
    private readonly IRepository<FcmsModuleRecord> _records;

    public ModulesController(ModuleRegistry registry, IRepository<FcmsModuleRecord> records)
    {
        _registry = registry;
        _records = records;
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
                    Status = rec?.Status ?? "Pending",
                    ActivatedAt = rec?.ActivatedAt,
                    DependsOn = m.Manifest.DependsOn
                };
            }).ToList()
        };

        return View(vm);
    }
}
