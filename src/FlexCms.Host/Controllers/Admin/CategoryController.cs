using System.Linq.Expressions;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Models;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/categories")]
public class CategoryController : BaseAdminController
{
    private readonly ICategoryService _categories;
    private readonly IRepository<FcmsCategory> _categoryRepo;

    public CategoryController(ICategoryService categories, IRepository<FcmsCategory> categoryRepo)
    {
        _categories = categories;
        _categoryRepo = categoryRepo;
    }


    [HttpGet("")]
    public IActionResult Index() => View();


    [HttpPost("datatable")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DataTable(DataTablesRequest req, CancellationToken ct)
    {
        var orderColumns = new Expression<Func<FcmsCategory, object>>[]
        {
            c => c.Name,
            c => c.Slug,
            c => c.SortOrder,
            c => c.Status
        };
        return DataTableResult(
            _categoryRepo.Query(),
            req,
            select: c => new
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                Description = c.Description,
                SortOrder = c.SortOrder,
                Status = (int)c.Status
            },
            orderColumns: orderColumns,
            globalSearch: q => c => c.Name.Contains(q) || c.Slug.Contains(q),
            permissions: new()
            {
                ["edit"] = FcmsPermissions.CategoriesEdit,
                ["delete"] = FcmsPermissions.CategoriesDelete
            },
            ct: ct);
    }


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


    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.CategoriesDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _categories.DeleteAsync(id, ct);
        ShowSuccess("Category deleted.");
        return FcmsOk("Category deleted.");
    }


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
