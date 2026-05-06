using FlexCms.Framework.Themes;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

/// <summary>
/// Theme-mode toggle endpoint. Accessible to anyone — the cookie is a
/// per-visitor preference and shouldn't require auth. Unsafe values are
/// coerced to <see cref="ThemeMode.Auto"/> by the helper, so there's no
/// validation surface to abuse.
/// </summary>
[Route("theme")]
public class ThemeController : Controller
{
    [HttpPost("mode")]
    [ValidateAntiForgeryToken]
    public IActionResult SetMode(string mode, string? returnUrl)
    {
        ThemeMode.Set(HttpContext, mode);
        return LocalRedirect(!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
