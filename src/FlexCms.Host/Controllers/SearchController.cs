using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Host.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

[Route("search")]
public class SearchController : Controller
{
    // string.Contains is translated to SQL LIKE by EF Core across providers,
    // so we don't need EF.Functions.Like here.
    private readonly IRepository<FcmsPage> _pages;
    private readonly IRepository<FcmsPost> _posts;

    public SearchController(IRepository<FcmsPage> pages, IRepository<FcmsPost> posts)
    {
        _pages = pages;
        _posts = posts;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return View(new SearchResultsViewModel { Query = "" });

        var term = q.Trim();

        var pages = (await _pages.FindAsync(
                p => p.IsPublished &&
                     (p.Title.Contains(term) || p.Content.Contains(term)),
                ct))
            .OrderBy(p => p.Title)
            .Select(p => new SearchResultItem
            {
                Title = p.Title,
                Slug = "/" + p.Slug,
                Excerpt = p.MetaDescription ?? ""
            })
            .ToList();

        var posts = (await _posts.FindAsync(
                p => p.IsPublished &&
                     (p.Title.Contains(term) ||
                      p.Content.Contains(term) ||
                      (p.Excerpt != null && p.Excerpt.Contains(term))),
                ct))
            .OrderByDescending(p => p.PublishedAt)
            .Select(p => new SearchResultItem
            {
                Title = p.Title,
                Slug = "/blog/" + p.Slug,
                Excerpt = p.Excerpt ?? p.MetaDescription ?? ""
            })
            .ToList();

        return View(new SearchResultsViewModel
        {
            Query = term,
            Results = pages.Concat(posts).ToList()
        });
    }
}
