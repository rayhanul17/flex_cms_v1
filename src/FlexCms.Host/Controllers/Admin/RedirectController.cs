using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[FcmsAuthorize]
[Route("admin/redirects")]
public class RedirectController : BaseAdminController
{
    // Use IRepository<> + IFcmsUnitOfWork instead of FcmsDbContext directly
    // so the controller stays decoupled from the concrete EF provider.
    private readonly IRepository<FcmsRedirect> _redirects;
    private readonly IFcmsUnitOfWork _uow;

    public RedirectController(IRepository<FcmsRedirect> redirects, IFcmsUnitOfWork uow)
    {
        _redirects = redirects;
        _uow = uow;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var redirects = (await _redirects.GetAllAsync(ct))
            .OrderBy(r => r.FromPath)
            .ToList();

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

        var exists = await _redirects.ExistsAsync(r => r.FromPath == model.FromPath, ct);
        if (exists)
        {
            ModelState.AddModelError(nameof(model.FromPath), "A redirect from this path already exists.");
            return View(model);
        }

        await _redirects.AddAsync(new FcmsRedirect
        {
            FromPath = model.FromPath,
            ToPath = model.ToPath,
            StatusCode = model.StatusCode,
            IsActive = model.IsActive,
            CreatedAt = FcmsTime.Now,
            UpdatedAt = FcmsTime.Now
        }, ct);
        await _uow.SaveChangesAsync(ct);
        ShowSuccess("Redirect created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(FcmsPermissions.RedirectsEdit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var r = await _redirects.GetByIdAsync(id, ct);
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

        var r = await _redirects.GetByIdAsync(id, ct);
        if (r is null) return NotFound();

        var exists = await _redirects.ExistsAsync(x => x.FromPath == model.FromPath && x.Id != id, ct);
        if (exists)
        {
            ModelState.AddModelError(nameof(model.FromPath), "A redirect from this path already exists.");
            return View(model);
        }

        r.FromPath = model.FromPath;
        r.ToPath = model.ToPath;
        r.StatusCode = model.StatusCode;
        r.IsActive = model.IsActive;
        await _redirects.UpdateAsync(r, ct);
        await _uow.SaveChangesAsync(ct);
        ShowSuccess("Redirect updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.RedirectsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await _redirects.GetByIdAsync(id, ct);
        if (r is null) return FcmsFail("Not found.");
        await _redirects.SoftDeleteAsync(r, ct);
        await _uow.SaveChangesAsync(ct);
        return FcmsOk("Redirect deleted.");
    }
}
