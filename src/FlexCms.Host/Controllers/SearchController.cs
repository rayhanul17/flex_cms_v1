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
        var pattern = $"%{term}%";

        var pages = await _db.Pages
            .AsNoTracking()
            .Where(p => p.IsPublished &&
                (EF.Functions.Like(p.Title, pattern) || EF.Functions.Like(p.Content, pattern)))
            .OrderBy(p => p.Title)
            .Select(p => new SearchResultItem { Title = p.Title, Slug = "/" + p.Slug, Excerpt = p.MetaDescription ?? "" })
            .ToListAsync(ct);

        var posts = await _db.Posts
            .AsNoTracking()
            .Where(p => p.IsPublished &&
                (EF.Functions.Like(p.Title, pattern) ||
                 EF.Functions.Like(p.Content, pattern) ||
                 (p.Excerpt != null && EF.Functions.Like(p.Excerpt, pattern))))
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
