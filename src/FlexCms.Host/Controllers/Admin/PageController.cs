using System.Linq.Expressions;
using FlexCms.Core.Models.Settings;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Helpers;
using FlexCms.Framework.Models;
using FlexCms.Framework.Services;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlexCms.Host.Controllers.Admin;

[Route("pages/admin")]
public class PageController : BaseAdminController
{
    private readonly IPageService _pages;
    private readonly IRepository<FcmsPage> _pageRepo;

    public PageController(IPageService pages, IRepository<FcmsPage> pageRepo)
    {
        _pages = pages;
        _pageRepo = pageRepo;
    }


    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.PagesView)]
    public IActionResult Index() => View();


    [HttpPost("datatable")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PagesView)]
    public Task<IActionResult> DataTable(DataTablesRequest req, CancellationToken ct)
    {
        var orderColumns = new Expression<Func<FcmsPage, object>>[]
        {
            p => p.Title,
            p => p.Slug,
            p => p.IsPublished,
            p => p.Status,
            p => p.UpdatedAt
        };

        // Public URL is composed client-side (the View resolves SiteSettings.BaseUrl
        // and the JS does {base}/{slug} per row), so we don't push the base URL
        // through the EF projection.
        return DataTableResult(
            _pageRepo.Query(),
            req,
            select: p => new
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                IsPublished = p.IsPublished,
                Status = (int)p.Status,
                UpdatedAt = p.UpdatedAt
            },
            orderColumns: orderColumns,
            globalSearch: q => p => p.Title.Contains(q) || p.Slug.Contains(q),
            permissions: new()
            {
                ["edit"] = FcmsPermissions.PagesEdit,
                ["delete"] = FcmsPermissions.PagesDelete
            },
            ct: ct);
    }


    [HttpGet("create")]
    [FcmsAuthorize(FcmsPermissions.PagesCreate)]
    public async Task<IActionResult> Create(CancellationToken ct)
        => View(new CreateEditPageViewModel { AvailableParents = await GetParentSelectListAsync(ct) });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PagesCreate)]
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


    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(FcmsPermissions.PagesEdit)]
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
    [FcmsAuthorize(FcmsPermissions.PagesEdit)]
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


    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PagesDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _pages.DeleteAsync(id, ct);
        ShowSuccess("Page deleted.");
        return FcmsOk("Page deleted.");
    }


    private static string HashPassword(string password) => FcmsHelper.HashPagePassword(password);

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
