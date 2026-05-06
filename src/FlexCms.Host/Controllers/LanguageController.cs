using FlexCms.Framework.I18n;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

/// <summary>
/// Language switcher endpoint. POST /lang/set sets a long-lived cookie carrying
/// the selected UI language; the next request's <see cref="LanguageMiddleware"/>
/// will pick it up and apply CultureInfo + Razor T() lookups accordingly.
/// </summary>
[Route("lang")]
public class LanguageController : Controller
{
    private readonly IFcmsTranslator _translator;

    public LanguageController(IFcmsTranslator translator) => _translator = translator;

    [HttpPost("set")]
    [ValidateAntiForgeryToken]
    public IActionResult Set(string lang, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(lang) || !_translator.SupportedLanguages.Contains(lang, StringComparer.OrdinalIgnoreCase))
            return BadRequest("Unsupported language.");

        Response.Cookies.Append(SupportedLanguages.CookieName, lang.ToLowerInvariant(), new CookieOptions
        {
            HttpOnly = false,           // intentionally readable by JS so a client-side switcher can show current value
            IsEssential = true,         // GDPR — user-set preference, not tracking
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });

        return LocalRedirect(SafeReturnUrl(returnUrl));
    }

    private string SafeReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
