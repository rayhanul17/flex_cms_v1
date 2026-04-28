using FlexCms.Framework.Auth;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace FlexCms.Host.Controllers.Admin;

[FcmsAuthorize]
public abstract class BaseAdminController : Controller
{
    // ── Shorthand helpers ─────────────────────────────────────────────────────

    protected IFcmsContextService FcmsContext =>
        HttpContext.RequestServices.GetRequiredService<IFcmsContextService>();

    protected IMemoryCache Cache =>
        HttpContext.RequestServices.GetRequiredService<IMemoryCache>();

    // ── Cache ─────────────────────────────────────────────────────────────────

    protected T? GetCache<T>(string key) where T : class
        => Cache.TryGetValue(key, out T? val) ? val : null;

    protected void SetCache<T>(string key, T value, TimeSpan? ttl = null)
        => Cache.Set(key, value, ttl ?? TimeSpan.FromMinutes(30));

    protected void RemoveCache(string key)
        => Cache.Remove(key);

    // ── Session ───────────────────────────────────────────────────────────────

    protected T? GetSession<T>(string key) where T : class
    {
        var json = HttpContext.Session.GetString(key);
        return json is null ? null : JsonSerializer.Deserialize<T>(json);
    }

    protected void SetSession<T>(string key, T value)
        => HttpContext.Session.SetString(key, JsonSerializer.Serialize(value));

    protected void RemoveSession(string key)
        => HttpContext.Session.Remove(key);

    // ── Toast / alert feedback ────────────────────────────────────────────────

    /// <summary>
    /// Sets a toast message. type: "success" | "danger" | "warning" | "info"
    /// showAfterRedirect=true → stored in TempData (survives PRG); false → ViewBag (same page).
    /// </summary>
    protected void ShowMessage(
        string message,
        string type = "success",
        bool showAfterRedirect = true)
    {
        if (showAfterRedirect)
        {
            TempData["Toast.Message"] = message;
            TempData["Toast.Type"] = type;
        }
        else
        {
            ViewBag.ToastMessage = message;
            ViewBag.ToastType = type;
        }
    }

    protected void ShowSuccess(string message, bool afterRedirect = true)
        => ShowMessage(message, "success", afterRedirect);

    protected void ShowError(string message, bool afterRedirect = true)
        => ShowMessage(message, "danger", afterRedirect);

    protected void ShowWarning(string message, bool afterRedirect = true)
        => ShowMessage(message, "warning", afterRedirect);

    protected void ShowInfo(string message, bool afterRedirect = true)
        => ShowMessage(message, "info", afterRedirect);

    // ── AJAX helpers ──────────────────────────────────────────────────────────

    protected JsonResult FcmsOk(string? message = null, object? data = null)
        => Json(new { isSuccess = true, message, data });

    protected JsonResult FcmsFail(string message, object? errors = null)
        => Json(new { isSuccess = false, message, errors });

    // ── Redirect helpers ──────────────────────────────────────────────────────

    protected IActionResult RedirectToErrorPage(string message, string? returnUrl = null)
    {
        TempData["ErrorMessage"] = message;
        return returnUrl is not null
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Home", new { area = "" });
    }

    // ── Shared view data ──────────────────────────────────────────────────────

    protected string ControllerName =>
        ControllerContext.RouteData.Values["controller"]?.ToString() ?? "";

    protected string ActionName =>
        ControllerContext.RouteData.Values["action"]?.ToString() ?? "";
}
