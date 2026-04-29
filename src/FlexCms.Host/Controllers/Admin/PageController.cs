using FlexCms.Framework.Auth;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Cms;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Security.Cryptography;
using System.Text;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/pages")]
public class PageController : BaseAdminController
{
    private readonly IPageService _pages;

    public PageController(IPageService pages) => _pages = pages;

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var all = await _pages.GetAllAsync(ct);
        var dict = all.ToDictionary(p => p.Id, p => p.Title);

        var vm = all.Select(p => new PageListItemViewModel
        {
            Id = p.Id,
            Title = p.Title,
            Slug = p.Slug,
            IsPublished = p.IsPublished,
            ParentId = p.ParentId,
            ParentTitle = p.ParentId.HasValue && dict.TryGetValue(p.ParentId.Value, out var t) ? t : null,
            CreatedAt = p.CreatedAt
        }).ToList();

        return View(vm);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpGet("create")]
    [FcmsAuthorize("pages.create")]
    public async Task<IActionResult> Create(CancellationToken ct)
        => View(new CreateEditPageViewModel { AvailableParents = await GetParentSelectListAsync(ct) });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("pages.create")]
    public async Task<IActionResult> Create(CreateEditPageViewModel model, CancellationToken ct)
    {
        if (await _pages.SlugExistsAsync(model.Slug, ct: ct))
            ModelState.AddModelError(nameof(model.Slug), "This slug is already in use.");

        model.AvailableParents = await GetParentSelectListAsync(ct);
        if (!ModelState.IsValid) return View(model);

        await _pages.CreateAsync(new FcmsPage
        {
            Title = model.Title,
            Slug = model.Slug,
            Content = model.Content,
            MetaTitle = model.MetaTitle,
            MetaDescription = model.MetaDescription,
            ParentId = model.ParentId,
            SortOrder = model.SortOrder,
            IsPublished = model.IsPublished,
            PublishedAt = model.IsPublished ? FcmsTime.Now : model.ScheduledAt,
            AccessControl = model.AccessControl,
            PasswordHash = model.AccessControl == PageAccessControl.PasswordProtected && !string.IsNullOrEmpty(model.Password)
                ? HashPassword(model.Password) : null
        }, ct);

        ShowSuccess($"Page '{model.Title}' created.");
        return RedirectToAction(nameof(Index));
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize("pages.edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var page = await _pages.GetByIdAsync(id, ct);
        if (page is null) return NotFound();

        return View(new CreateEditPageViewModel
        {
            Id = page.Id,
            Title = page.Title,
            Slug = page.Slug,
            Content = page.Content,
            MetaTitle = page.MetaTitle,
            MetaDescription = page.MetaDescription,
            ParentId = page.ParentId,
            SortOrder = page.SortOrder,
            IsPublished = page.IsPublished,
            ScheduledAt = !page.IsPublished ? page.PublishedAt : null,
            AccessControl = page.AccessControl,
            AvailableParents = await GetParentSelectListAsync(ct, excludeId: id)
        });
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("pages.edit")]
    public async Task<IActionResult> Edit(Guid id, CreateEditPageViewModel model, CancellationToken ct)
    {
        if (await _pages.SlugExistsAsync(model.Slug, excludeId: id, ct: ct))
            ModelState.AddModelError(nameof(model.Slug), "This slug is already in use.");

        model.AvailableParents = await GetParentSelectListAsync(ct, excludeId: id);
        if (!ModelState.IsValid) return View(model);

        var page = await _pages.GetByIdAsync(id, ct);
        if (page is null) return NotFound();

        page.Title = model.Title;
        page.Slug = model.Slug;
        page.Content = model.Content;
        page.MetaTitle = model.MetaTitle;
        page.MetaDescription = model.MetaDescription;
        page.ParentId = model.ParentId;
        page.SortOrder = model.SortOrder;
        page.IsPublished = model.IsPublished;
        if (model.IsPublished && page.PublishedAt is null)
            page.PublishedAt = FcmsTime.Now;
        else if (!model.IsPublished && model.ScheduledAt.HasValue)
            page.PublishedAt = model.ScheduledAt;
        page.AccessControl = model.AccessControl;
        if (model.AccessControl == PageAccessControl.PasswordProtected && !string.IsNullOrEmpty(model.Password))
            page.PasswordHash = HashPassword(model.Password);
        else if (model.AccessControl != PageAccessControl.PasswordProtected)
            page.PasswordHash = null;

        await _pages.UpdateAsync(page, ct);
        ShowSuccess($"Page '{page.Title}' updated.");
        return RedirectToAction(nameof(Index));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("pages.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _pages.DeleteAsync(id, ct);
        ShowSuccess("Page deleted.");
        return FcmsOk("Page deleted.");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<List<SelectListItem>> GetParentSelectListAsync(CancellationToken ct, Guid? excludeId = null)
    {
        var all = await _pages.GetAllAsync(ct);
        return all
            .Where(p => p.Id != excludeId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .Select(p => new SelectListItem(p.Title, p.Id.ToString()))
            .ToList();
    }
}
