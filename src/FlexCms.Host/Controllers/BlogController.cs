using FlexCms.Framework.Cms;
using FlexCms.Framework.Cms.Preview;
using FlexCms.Framework.I18n;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

[Route("blog")]
public class BlogController : Controller
{
    private readonly IPostService _posts;
    private readonly ICategoryService _categories;
    private readonly IFcmsTranslator _translator;
    private readonly IPreviewTokenService _previewTokens;

    public BlogController(IPostService posts, ICategoryService categories, IFcmsTranslator translator, IPreviewTokenService previewTokens)
    {
        _posts = posts;
        _categories = categories;
        _translator = translator;
        _previewTokens = previewTokens;
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
    public async Task<IActionResult> Post(string slug, [FromQuery(Name = "preview")] string? previewToken, CancellationToken ct)
    {
        var resolved = await _posts.ResolveBySlugAsync(slug, _translator.CurrentLanguage, ct);
        if (resolved is null) return NotFound();
        var (post, translation) = resolved.Value;

        var isPreview = false;
        if (!post.IsPublished)
        {
            isPreview = await _previewTokens.ValidateAsync(nameof(FcmsPost), post.Id, previewToken, ct);
            if (!isPreview) return NotFound();
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        }

        if (translation is not null)
        {
            post.Title = translation.Title;
            post.Excerpt = translation.Excerpt;
            post.Content = translation.Content;
            post.MetaTitle = translation.MetaTitle;
            post.MetaDescription = translation.MetaDescription;
        }

        // Don't pollute the public view-count from preview/draft sharing.
        if (!isPreview)
            await _posts.IncrementViewCountAsync(post.Id, ct);
        return View(post);
    }
}
