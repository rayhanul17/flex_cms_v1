using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Models;
using FlexCms.Framework.Mvc;
using FlexCms.Module.Name.Data;
using Microsoft.AspNetCore.Mvc;
using System.Linq.Expressions;

namespace FlexCms.Module.Name.Controllers;

/// <summary>
/// Admin CRUD controller. Wire-in is automatic — modules' assemblies are added as
/// MVC ApplicationParts during framework startup, so attribute-routed actions
/// become reachable without any host-side registration.
///
/// <para>
/// Permission keys must match the strings registered by the module's
/// <c>GetPermissions()</c>. They are fully-qualified (module ID prefix included)
/// — the helper constants on <see cref="__ShortName__Permissions"/> encode that.
/// </para>
/// </summary>
[FcmsAuthorize(__ShortName__Permissions.View)]
[Route("admin/mod_prefix")]
public class Admin__ShortName__Controller : BaseFcmsController
{
    private readonly IRepository<__ShortName__Item> _items;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IFcmsLogService _audit;

    public Admin__ShortName__Controller(
        IRepository<__ShortName__Item> items,
        IFcmsUnitOfWork uow,
        IFcmsLogService audit)
    {
        _items = items;
        _uow = uow;
        _audit = audit;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpPost("datatable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DataTable([FromForm] DataTablesRequest req, CancellationToken ct)
    {
        var orderColumns = new Expression<Func<__ShortName__Item, object>>[]
        {
            x => x.Title,
            x => x.IsPublished,
            x => x.CreatedAt
        };

        var response = await _items.Query().ToDataTableAsync(
            req,
            select: x => new
            {
                x.Id,
                x.Title,
                x.Description,
                x.IsPublished,
                CreatedAt = x.CreatedAt
            },
            globalSearchFilter: string.IsNullOrWhiteSpace(req.SearchValue)
                ? null
                : x => x.Title.Contains(req.SearchValue) || x.Description.Contains(req.SearchValue),
            orderColumns: orderColumns,
            ct: ct);

        return Json(response);
    }

    [HttpGet("create")]
    [FcmsAuthorize(__ShortName__Permissions.Create)]
    public IActionResult Create() => View(new __ShortName__Item());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(__ShortName__Permissions.Create)]
    public async Task<IActionResult> CreatePost(__ShortName__Item model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Create", model);

        await _items.AddAsync(model, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("__shortname__.create", nameof(__ShortName__Item), model.Id.ToString(),
            value: model, module: "FlexCms.Module.Name", ct: ct);

        ShowSuccess("__ShortName__ created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("edit/{id:guid}")]
    [FcmsAuthorize(__ShortName__Permissions.Edit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(id, ct);
        if (item is null) return NotFound();
        return View(item);
    }

    [HttpPost("edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(__ShortName__Permissions.Edit)]
    public async Task<IActionResult> EditPost(Guid id, __ShortName__Item model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Edit", model);

        var item = await _items.GetByIdAsync(id, ct);
        if (item is null) return NotFound();

        item.Title = model.Title;
        item.Description = model.Description;
        item.IsPublished = model.IsPublished;
        await _items.UpdateAsync(item, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("__shortname__.update", nameof(__ShortName__Item), item.Id.ToString(),
            value: item, module: "FlexCms.Module.Name", ct: ct);

        ShowSuccess("__ShortName__ updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(__ShortName__Permissions.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(id, ct);
        if (item is null) return FcmsFail("Not found.");

        await _items.SoftDeleteAsync(item, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("__shortname__.delete", nameof(__ShortName__Item), item.Id.ToString(),
            value: item, module: "FlexCms.Module.Name", ct: ct);

        return FcmsOk("Deleted.");
    }
}
