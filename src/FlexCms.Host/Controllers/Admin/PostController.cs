using FlexCms.Framework.Auth;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Cms;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/posts")]
public class PostController : BaseAdminController
{
    private readonly IPostService _posts;
    private readonly ICategoryService _categories;

    public PostController(IPostService posts, ICategoryService categories)
    {
        _posts = posts;
        _categories = categories;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var all = await _posts.GetAllAsync(ct);
        var vm = all.Select(p => new PostListItemViewModel
        {
            Id = p.Id,
            Title = p.Title,
            Slug = p.Slug,
            IsPublished = p.IsPublished,
            CategoryName = p.Category?.Name,
            CreatedAt = p.CreatedAt,
            ViewCount = p.ViewCount
        }).ToList();

        return View(vm);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpGet("create")]
    [FcmsAuthorize("posts.create")]
    public async Task<IActionResult> Create(CancellationToken ct)
        => View(new CreateEditPostViewModel { AvailableCategories = await GetCategorySelectListAsync(ct) });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("posts.create")]
    public async Task<IActionResult> Create(CreateEditPostViewModel model, CancellationToken ct)
    {
        if (await _posts.SlugExistsAsync(model.Slug, ct: ct))
            ModelState.AddModelError(nameof(model.Slug), "This slug is already in use.");

        model.AvailableCategories = await GetCategorySelectListAsync(ct);
        if (!ModelState.IsValid) return View(model);

        await _posts.CreateAsync(new FcmsPost
        {
            Title = model.Title,
            Slug = model.Slug,
            Excerpt = model.Excerpt,
            Content = model.Content,
            MetaTitle = model.MetaTitle,
            MetaDescription = model.MetaDescription,
            FeaturedImageUrl = model.FeaturedImageUrl,
            CategoryId = model.CategoryId,
            IsPublished = model.IsPublished,
            PublishedAt = model.IsPublished ? FcmsTime.Now : model.ScheduledAt
        }, ParseTags(model.Tags), ct);

        ShowSuccess($"Post '{model.Title}' created.");
        return RedirectToAction(nameof(Index));
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize("posts.edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var post = await _posts.GetByIdAsync(id, ct);
        if (post is null) return NotFound();

        var tags = string.Join(", ", post.PostTags.Select(pt => pt.Tag.Slug));
        return View(new CreateEditPostViewModel
        {
            Id = post.Id,
            Title = post.Title,
            Slug = post.Slug,
            Excerpt = post.Excerpt,
            Content = post.Content,
            MetaTitle = post.MetaTitle,
            MetaDescription = post.MetaDescription,
            FeaturedImageUrl = post.FeaturedImageUrl,
            CategoryId = post.CategoryId,
            IsPublished = post.IsPublished,
            ScheduledAt = !post.IsPublished ? post.PublishedAt : null,
            Tags = tags,
            AvailableCategories = await GetCategorySelectListAsync(ct)
        });
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("posts.edit")]
    public async Task<IActionResult> Edit(Guid id, CreateEditPostViewModel model, CancellationToken ct)
    {
        if (await _posts.SlugExistsAsync(model.Slug, excludeId: id, ct: ct))
            ModelState.AddModelError(nameof(model.Slug), "This slug is already in use.");

        model.AvailableCategories = await GetCategorySelectListAsync(ct);
        if (!ModelState.IsValid) return View(model);

        var post = await _posts.GetByIdAsync(id, ct);
        if (post is null) return NotFound();

        post.Title = model.Title;
        post.Slug = model.Slug;
        post.Excerpt = model.Excerpt;
        post.Content = model.Content;
        post.MetaTitle = model.MetaTitle;
        post.MetaDescription = model.MetaDescription;
        post.FeaturedImageUrl = model.FeaturedImageUrl;
        post.CategoryId = model.CategoryId;
        post.IsPublished = model.IsPublished;
        if (model.IsPublished && post.PublishedAt is null)
            post.PublishedAt = FcmsTime.Now;
        else if (!model.IsPublished && model.ScheduledAt.HasValue)
            post.PublishedAt = model.ScheduledAt;

        await _posts.UpdateAsync(post, ParseTags(model.Tags), ct);
        ShowSuccess($"Post '{post.Title}' updated.");
        return RedirectToAction(nameof(Index));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("posts.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _posts.DeleteAsync(id, ct);
        ShowSuccess("Post deleted.");
        return FcmsOk("Post deleted.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<SelectListItem>> GetCategorySelectListAsync(CancellationToken ct)
    {
        var cats = await _categories.GetAllAsync(ct);
        return cats
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
    }

    private static IEnumerable<string> ParseTags(string raw)
        => raw.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Select(t => t.ToLowerInvariant())
              .Distinct();
}
