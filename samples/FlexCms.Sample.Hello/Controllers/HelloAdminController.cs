using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Sample.Hello.Data;
using FlexCms.Sample.Hello.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Sample.Hello.Controllers;

/// <summary>
/// Admin CRUD for greetings — wired to the sidebar by <c>HelloModule.GetMenuItems()</c>.
///
/// Demonstrates that the framework's <c>IRepository&lt;T&gt;</c> abstraction and
/// audit-log Module column actually work for a module's own entities:
/// the controller talks to <see cref="GreetingService"/> (which uses
/// <c>EfRepository&lt;HelloGreeting&gt;</c> internally), and every audit row
/// emitted from this controller passes <c>module: HelloModule.ModuleIdValue</c>
/// so admins can filter <c>fcms_logs</c> by module in the audit log UI.
/// </summary>
[Route("admin/hello")]
[FcmsAuthorize(HelloPermissions.View)]
public class HelloAdminController : Controller
{
    private const string ModuleId = "FlexCms.Sample.Hello";

    private readonly GreetingService _service;
    private readonly IFcmsLogService _log;

    public HelloAdminController(GreetingService service, IFcmsLogService log)
    {
        _service = service;
        _log = log;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await _service.GetAllAsync(ct));

    [HttpGet("create")]
    [FcmsAuthorize(HelloPermissions.Create)]
    public IActionResult Create()
    {
        ViewData["IsNew"] = true;
        return View("Edit", new HelloGreeting { Id = Guid.Empty });
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(HelloPermissions.Create)]
    public async Task<IActionResult> Create(HelloGreeting model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Audience)) ModelState.AddModelError(nameof(model.Audience), "Audience is required.");
        if (string.IsNullOrWhiteSpace(model.Message))  ModelState.AddModelError(nameof(model.Message), "Message is required.");
        if (!ModelState.IsValid) { ViewData["IsNew"] = true; return View("Edit", model); }

        var saved = await _service.CreateAsync(model, ct);
        await _log.LogAsync("hello.create", nameof(HelloGreeting), saved.Id.ToString(), value: saved, module: ModuleId, ct: ct);
        TempData["Success"] = "Greeting created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(HelloPermissions.Edit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var row = await _service.GetByIdAsync(id, ct);
        if (row is null) return NotFound();
        return View(row);
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(HelloPermissions.Edit)]
    public async Task<IActionResult> Edit(Guid id, HelloGreeting model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Audience)) ModelState.AddModelError(nameof(model.Audience), "Audience is required.");
        if (string.IsNullOrWhiteSpace(model.Message))  ModelState.AddModelError(nameof(model.Message), "Message is required.");
        if (!ModelState.IsValid) return View(model);

        var ok = await _service.UpdateAsync(id, model.Audience, model.Message, ct);
        if (!ok) return NotFound();
        await _log.LogAsync("hello.edit", nameof(HelloGreeting), id.ToString(), value: new { id, model.Audience, model.Message }, module: ModuleId, ct: ct);
        TempData["Success"] = "Greeting updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(HelloPermissions.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _service.DeleteAsync(id, ct);
        if (deleted is null) return Json(new { isSuccess = false, message = "Not found." });
        await _log.LogAsync("hello.delete", nameof(HelloGreeting), id.ToString(), value: deleted, module: ModuleId, ct: ct);
        return Json(new { isSuccess = true, message = "Greeting deleted." });
    }
}
