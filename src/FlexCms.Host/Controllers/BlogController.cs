using FlexCms.Framework.Cms;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

[Route("blog")]
public class BlogController : Controller
{
    private readonly IPostService _posts;
    private readonly ICategoryService _categories;

    public BlogController(IPostService posts, ICategoryService categories)
    {
        _posts = posts;
        _categories = categories;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var posts = await _posts.GetPublishedAsync(ct);
        var categories = await _categories.GetAllAsync(ct);
        ViewBag.Categories = categories;
        return View(posts);
    }

    [HttpGet("category/{categorySlug}")]
    public async Task<IActionResult> Category(string categorySlug, CancellationToken ct)
    {
        var category = await _categories.GetBySlugAsync(categorySlug, ct);
        if (category is null) return NotFound();

        var posts = await _posts.GetByCategoryAsync(category.Id, ct);
        var allCategories = await _categories.GetAllAsync(ct);
        ViewBag.Category = category;
        ViewBag.Categories = allCategories;
        return View("Index", posts);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Post(string slug, CancellationToken ct)
    {
        var post = await _posts.GetBySlugAsync(slug, ct);
        if (post is null || !post.IsPublished) return NotFound();

        await _posts.IncrementViewCountAsync(post.Id, ct);
        return View(post);
    }
}
