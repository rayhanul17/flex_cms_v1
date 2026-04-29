using FlexCms.Framework.Cms;
using FlexCms.Framework.Helpers;
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

        switch (page.AccessControl)
        {
            case PageAccessControl.AuthenticatedOnly when !User.Identity!.IsAuthenticated:
                return RedirectToAction("Login", "Auth", new { returnUrl = Request.Path });

            case PageAccessControl.PasswordProtected:
                var submitted = Request.Query["pw"].ToString();
                if (!string.IsNullOrEmpty(submitted))
                {
                    var hash = HashPassword(submitted);
                    if (hash == page.PasswordHash)
                    {
                        HttpContext.Session.SetString($"page_unlocked_{page.Id}", "1");
                    }
                }
                var unlocked = HttpContext.Session.GetString($"page_unlocked_{page.Id}");
                if (unlocked != "1")
                    return View("PagePassword", page);
                break;
        }

        return View(page);
    }

    internal static string HashPassword(string password) => FcmsHelper.HashPagePassword(password);
}
