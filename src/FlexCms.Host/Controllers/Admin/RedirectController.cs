using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Host.Controllers.Admin;

[FcmsAuthorize]
[Route("admin/redirects")]
public class RedirectController : BaseAdminController
{
    private readonly FcmsDbContext _db;

    public RedirectController(FcmsDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var redirects = await _db.Redirects
            .OrderBy(r => r.FromPath)
            .ToListAsync(ct);

        return View(redirects.Select(r => new RedirectListItemViewModel
        {
            Id = r.Id,
            FromPath = r.FromPath,
            ToPath = r.ToPath,
            StatusCode = r.StatusCode,
            IsActive = r.IsActive,
            HitCount = r.HitCount
        }).ToList());
    }

    [HttpGet("create")]
    [FcmsAuthorize(FcmsPermissions.RedirectsCreate)]
    public IActionResult Create() => View(new CreateEditRedirectViewModel());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.RedirectsCreate)]
    public async Task<IActionResult> Create(CreateEditRedirectViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var exists = await _db.Redirects.AnyAsync(r => r.FromPath == model.FromPath, ct);
        if (exists)
        {
            ModelState.AddModelError(nameof(model.FromPath), "A redirect from this path already exists.");
            return View(model);
        }

        _db.Redirects.Add(new FcmsRedirect
        {
            FromPath = model.FromPath,
            ToPath = model.ToPath,
            StatusCode = model.StatusCode,
            IsActive = model.IsActive,
            CreatedAt = FcmsTime.Now,
            UpdatedAt = FcmsTime.Now
        });
        await _db.SaveChangesAsync(ct);
        ShowSuccess("Redirect created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(FcmsPermissions.RedirectsEdit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var r = await _db.Redirects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();

        return View(new CreateEditRedirectViewModel
        {
            Id = r.Id,
            FromPath = r.FromPath,
            ToPath = r.ToPath,
            StatusCode = r.StatusCode,
            IsActive = r.IsActive
        });
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.RedirectsEdit)]
    public async Task<IActionResult> Edit(Guid id, CreateEditRedirectViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var r = await _db.Redirects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();

        var exists = await _db.Redirects.AnyAsync(x => x.FromPath == model.FromPath && x.Id != id, ct);
        if (exists)
        {
            ModelState.AddModelError(nameof(model.FromPath), "A redirect from this path already exists.");
            return View(model);
        }

        r.FromPath = model.FromPath;
        r.ToPath = model.ToPath;
        r.StatusCode = model.StatusCode;
        r.IsActive = model.IsActive;
        await _db.SaveChangesAsync(ct);
        ShowSuccess("Redirect updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.RedirectsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await _db.Redirects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return FcmsFail("Not found.");
        r.Status = EntityStatus.Deleted;
        r.DeletedAt = FcmsTime.Now;
        await _db.SaveChangesAsync(ct);
        return FcmsOk("Redirect deleted.");
    }
}
