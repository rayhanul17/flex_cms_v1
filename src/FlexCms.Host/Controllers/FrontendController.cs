using FlexCms.Framework.Cms;
using FlexCms.Framework.Helpers;
using FlexCms.Framework.I18n;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

public class FrontendController : Controller
{
    private readonly IPageService _pages;
    private readonly IFcmsTranslator _translator;

    public FrontendController(IPageService pages, IFcmsTranslator translator)
    {
        _pages = pages;
        _translator = translator;
    }

    [HttpGet]
    public async Task<IActionResult> Page(string slug, CancellationToken ct)
    {
        var resolved = await _pages.ResolveBySlugAsync(slug, _translator.CurrentLanguage, ct);
        if (resolved is null) return NotFound();
        var (page, translation) = resolved.Value;

        if (!page.IsPublished)
            return NotFound();

        // Overlay translation fields onto the page so the view shows the correct
        // language; routing/access decisions still come from the base entity.
        if (translation is not null)
        {
            page.Title = translation.Title;
            page.Content = translation.Content;
            page.MetaTitle = translation.MetaTitle;
            page.MetaDescription = translation.MetaDescription;
        }

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
