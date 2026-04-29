using FlexCms.Framework.Cms;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

public class FrontendController : Controller
{
    private readonly IPageService _pages;

    public FrontendController(IPageService pages) => _pages = pages;

    [HttpGet]
    public async Task<IActionResult> Page(string slug, CancellationToken ct)
    {
        var page = await _pages.GetBySlugAsync(slug, ct);

        if (page is null || !page.IsPublished)
            return NotFound();

        return View(page);
    }
}
