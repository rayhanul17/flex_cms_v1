using FlexCms.Framework.Cms;
using FlexCms.Framework.Cms.Comments;
using FlexCms.Framework.Cms.Preview;
using FlexCms.Framework.I18n;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

[Route("blog")]
[AllowAnonymous]
public class BlogController : Controller
{
    private readonly IPostService _posts;
    private readonly ICategoryService _categories;
    private readonly IFcmsTranslator _translator;
    private readonly IPreviewTokenService _previewTokens;
    private readonly ICommentService _comments;
    private readonly IFcmsContextService _ctx;

    public BlogController(
        IPostService posts,
        ICategoryService categories,
        IFcmsTranslator translator,
        IPreviewTokenService previewTokens,
        ICommentService comments,
        IFcmsContextService ctx)
    {
        _posts = posts;
        _categories = categories;
        _translator = translator;
        _previewTokens = previewTokens;
        _comments = comments;
        _ctx = ctx;
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

        if (!isPreview)
            await _posts.IncrementViewCountAsync(post.Id, ct);

        var comments = await _comments.GetApprovedAsync(nameof(FcmsPost), post.Id, ct);
        ViewBag.Comments = comments;
        ViewBag.IsAuthenticated = _ctx.IsAuthenticated;
        ViewBag.CurrentUserName = _ctx.IsAuthenticated ? User.Identity?.Name : null;

        return View(post);
    }

    [HttpPost("{slug}/comment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitComment(string slug, string body, string? authorName, string? authorEmail, CancellationToken ct)
    {
        var resolved = await _posts.ResolveBySlugAsync(slug, _translator.CurrentLanguage, ct);
        if (resolved is null) return NotFound();
        var (post, _) = resolved.Value;
        if (!post.IsPublished) return NotFound();

        if (string.IsNullOrWhiteSpace(body) || body.Length > 2000)
        {
            TempData["CommentError"] = "Comment must be between 1 and 2000 characters.";
            return RedirectToAction(nameof(Post), new { slug });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        Guid? authorUserId = null;
        if (_ctx.IsAuthenticated && Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid))
        {
            authorUserId = uid;
            authorName  = User.Identity?.Name ?? authorName;
        }

        var comment = new FcmsComment
        {
            EntityType    = nameof(FcmsPost),
            EntityId      = post.Id,
            Body          = body.Trim(),
            AuthorUserId  = authorUserId,
            AuthorName    = authorName?.Trim() ?? "Anonymous",
            AuthorEmail   = authorEmail?.Trim() ?? "",
            IpAddress     = ip ?? "",
        };

        await _comments.SubmitAsync(comment, ct);

        TempData["CommentSuccess"] = "Your comment has been submitted and is awaiting moderation. Thank you!";
        return RedirectToAction(nameof(Post), new { slug });
    }
}
