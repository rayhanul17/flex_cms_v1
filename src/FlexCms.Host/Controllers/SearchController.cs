using FlexCms.Framework.Db.Ef;
using FlexCms.Host.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Host.Controllers;

[Route("search")]
public class SearchController : Controller
{
    private readonly FcmsDbContext _db;

    public SearchController(FcmsDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return View(new SearchResultsViewModel { Query = "" });

        var term = q.Trim();
        var lower = term.ToLowerInvariant();

        var pages = await _db.Pages
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.IsPublished &&
                (p.Title.ToLower().Contains(lower) || p.Content.ToLower().Contains(lower)))
            .OrderBy(p => p.Title)
            .Select(p => new SearchResultItem { Title = p.Title, Slug = "/" + p.Slug, Excerpt = p.MetaDescription ?? "" })
            .ToListAsync(ct);

        var posts = await _db.Posts
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.IsPublished &&
                (p.Title.ToLower().Contains(lower) || p.Content.ToLower().Contains(lower) || (p.Excerpt != null && p.Excerpt.ToLower().Contains(lower))))
            .OrderByDescending(p => p.PublishedAt)
            .Select(p => new SearchResultItem { Title = p.Title, Slug = "/blog/" + p.Slug, Excerpt = p.Excerpt ?? p.MetaDescription ?? "" })
            .ToListAsync(ct);

        return View(new SearchResultsViewModel
        {
            Query = term,
            Results = pages.Concat(posts).ToList()
        });
    }
}
