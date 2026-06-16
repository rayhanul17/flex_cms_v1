using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Module.Name.Data;
using FlexCms.Module.Name.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Module.Name.Controllers;

/// <summary>
/// Admin CRUD controller. Wire-in is automatic — modules' assemblies are added
/// as MVC ApplicationParts during framework startup, so attribute-routed
/// actions become reachable without any host-side registration. Razor views
/// under <c>Views/Admin__ShortName__/</c> are compiled into this DLL by the
/// Razor SDK and served by the host.
///
/// <para>
/// Permission keys must match the strings registered by the module's
/// <c>GetPermissions()</c>. They are fully-qualified (module ID prefix included)
/// — the helper constants on <see cref="__ShortName__Permissions"/> encode that.
/// </para>
/// </summary>
[Route("admin/mod_prefix")]
[FcmsAuthorize(__ShortName__Permissions.View)]
public class Admin__ShortName__Controller : Controller
{
    private readonly __ShortName__Service _service;
    private readonly IFcmsLogService _log;

    public Admin__ShortName__Controller(__ShortName__Service service, IFcmsLogService log)
    {
        _service = service;
        _log = log;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await _service.GetAllAsync(ct));

    [HttpGet("create")]
    [FcmsAuthorize(__ShortName__Permissions.Create)]
    public IActionResult Create()
    {
        ViewData["IsNew"] = true;
        return View("Edit", new __ShortName__Item { Id = Guid.Empty });
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(__ShortName__Permissions.Create)]
    public async Task<IActionResult> Create(__ShortName__Item model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            ModelState.AddModelError(nameof(model.Title), "Title is required.");
        if (!ModelState.IsValid) { ViewData["IsNew"] = true; return View("Edit", model); }

        var saved = await _service.CreateAsync(model, ct);
        await _log.LogAsync("__shortname__.create", nameof(__ShortName__Item), saved.Id.ToString(),
            value: saved, module: __ShortName__Module.ModuleIdValue, ct: ct);
        TempData["Success"] = "__ShortName__ created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(__ShortName__Permissions.Edit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var row = await _service.GetByIdAsync(id, ct);
        if (row is null) return NotFound();
        return View(row);
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(__ShortName__Permissions.Edit)]
    public async Task<IActionResult> Edit(Guid id, __ShortName__Item model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            ModelState.AddModelError(nameof(model.Title), "Title is required.");
        if (!ModelState.IsValid) return View(model);

        var ok = await _service.UpdateAsync(id, model.Title, model.Description, model.IsPublished, ct);
        if (!ok) return NotFound();
        await _log.LogAsync("__shortname__.update", nameof(__ShortName__Item), id.ToString(),
            value: new { id, model.Title, model.IsPublished }, module: __ShortName__Module.ModuleIdValue, ct: ct);
        TempData["Success"] = "__ShortName__ updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(__ShortName__Permissions.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _service.DeleteAsync(id, ct);
        if (deleted is null) return Json(new { isSuccess = false, message = "Not found." });
        await _log.LogAsync("__shortname__.delete", nameof(__ShortName__Item), id.ToString(),
            value: deleted, module: __ShortName__Module.ModuleIdValue, ct: ct);
        return Json(new { isSuccess = true, message = "Deleted." });
    }
}
