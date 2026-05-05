using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/categories")]
public class CategoryController : BaseAdminController
{
    private readonly ICategoryService _categories;

    public CategoryController(ICategoryService categories) => _categories = categories;

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var all = await _categories.GetAllAsync(ct);
        var counts = await Task.WhenAll(all.Select(c => _categories.GetPostCountAsync(c.Id, ct)));
        var vm = all.Select((c, i) => new CategoryListItemViewModel
        {
            Id = c.Id,
            Name = c.Name,
            Slug = c.Slug,
            Description = c.Description,
            PostCount = counts[i]
        }).ToList();

        return View(vm);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpGet("create")]
    [FcmsAuthorize(FcmsPermissions.CategoriesCreate)]
    public async Task<IActionResult> Create(CancellationToken ct)
        => View(new CreateEditCategoryViewModel { AvailableParents = await GetParentSelectListAsync(ct) });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.CategoriesCreate)]
    public async Task<IActionResult> Create(CreateEditCategoryViewModel model, CancellationToken ct)
    {
        if (await _categories.SlugExistsAsync(model.Slug, ct: ct))
            ModelState.AddModelError(nameof(model.Slug), "This slug is already in use.");

        model.AvailableParents = await GetParentSelectListAsync(ct);
        if (!ModelState.IsValid) return View(model);

        await _categories.CreateAsync(new FcmsCategory
        {
            Name = model.Name,
            Slug = model.Slug,
            Description = model.Description,
            SortOrder = model.SortOrder
        }, ct);

        ShowSuccess($"Category '{model.Name}' created.");
        return RedirectToAction(nameof(Index));
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(FcmsPermissions.CategoriesEdit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var cat = await _categories.GetByIdAsync(id, ct);
        if (cat is null) return NotFound();

        return View(new CreateEditCategoryViewModel
        {
            Id = cat.Id,
            Name = cat.Name,
            Slug = cat.Slug,
            Description = cat.Description,
            SortOrder = cat.SortOrder,
            AvailableParents = await GetParentSelectListAsync(ct, excludeId: id)
        });
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.CategoriesEdit)]
    public async Task<IActionResult> Edit(Guid id, CreateEditCategoryViewModel model, CancellationToken ct)
    {
        if (await _categories.SlugExistsAsync(model.Slug, excludeId: id, ct: ct))
            ModelState.AddModelError(nameof(model.Slug), "This slug is already in use.");

        model.AvailableParents = await GetParentSelectListAsync(ct, excludeId: id);
        if (!ModelState.IsValid) return View(model);

        var cat = await _categories.GetByIdAsync(id, ct);
        if (cat is null) return NotFound();

        cat.Name = model.Name;
        cat.Slug = model.Slug;
        cat.Description = model.Description;
        cat.SortOrder = model.SortOrder;

        await _categories.UpdateAsync(cat, ct);
        ShowSuccess($"Category '{cat.Name}' updated.");
        return RedirectToAction(nameof(Index));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.CategoriesDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _categories.DeleteAsync(id, ct);
        ShowSuccess("Category deleted.");
        return FcmsOk("Category deleted.");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task<List<SelectListItem>> GetParentSelectListAsync(CancellationToken ct, Guid? excludeId = null)
    {
        var all = await _categories.GetAllAsync(ct);
        return all
            .Where(c => c.Id != excludeId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
    }
}
