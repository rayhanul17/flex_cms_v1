using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Modules;
using FlexCms.Sample.Hello.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Sample.Hello.Controllers;

/// <summary>
/// Admin CRUD for greetings — wired to the sidebar by <c>HelloModule.GetMenuItems()</c>.
/// Uses the per-request HelloDbContext pattern the seed already established:
/// rebuild from <see cref="ModuleActivationOptions"/> rather than register the
/// context in host DI, which keeps the module self-contained.
/// </summary>
[Route("admin/hello")]
[FcmsAuthorize(HelloPermissions.View)]
public class HelloAdminController : Controller
{
    private readonly ModuleActivationOptions _opts;
    private readonly IFcmsLogService _log;

    public HelloAdminController(ModuleActivationOptions opts, IFcmsLogService log)
    {
        _opts = opts;
        _log = log;
    }

    private HelloDbContext NewDb() => (HelloDbContext)new HelloModule().CreateMigrationContext(_opts.ConnectionString, _opts.Provider)!;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await using var db = NewDb();
        var rows = await db.Greetings.OrderByDescending(g => g.CreatedAt).ToListAsync(ct);
        return View(rows);
    }

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
        if (!ModelState.IsValid) return View("Edit", model);

        await using var db = NewDb();
        if (model.Id == Guid.Empty) model.Id = Guid.NewGuid();
        db.Greetings.Add(model);
        await db.SaveChangesAsync(ct);
        await _log.LogAsync("hello.create", nameof(HelloGreeting), model.Id.ToString(), value: model, ct: ct);
        TempData["Success"] = "Greeting created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(HelloPermissions.Edit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        await using var db = NewDb();
        var row = await db.Greetings.FirstOrDefaultAsync(g => g.Id == id, ct);
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

        await using var db = NewDb();
        var row = await db.Greetings.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (row is null) return NotFound();

        row.Audience = model.Audience.Trim();
        row.Message = model.Message.Trim();
        await db.SaveChangesAsync(ct);
        await _log.LogAsync("hello.edit", nameof(HelloGreeting), row.Id.ToString(), value: row, ct: ct);
        TempData["Success"] = "Greeting updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(HelloPermissions.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await using var db = NewDb();
        var row = await db.Greetings.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (row is null) return Json(new { isSuccess = false, message = "Not found." });
        db.Greetings.Remove(row);
        await db.SaveChangesAsync(ct);
        await _log.LogAsync("hello.delete", nameof(HelloGreeting), id.ToString(), value: row, ct: ct);
        return Json(new { isSuccess = true, message = "Greeting deleted." });
    }
}
