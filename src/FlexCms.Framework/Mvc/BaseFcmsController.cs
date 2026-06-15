using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Mvc;

/// <summary>
/// Reusable MVC controller base for the framework, host, and every module.
/// Provides:
/// <list type="bullet">
///   <item>Toast-style flash messages with append, duration, and close-button options
///         (<see cref="ShowMessage"/>, <see cref="ShowSuccess"/>, etc.).</item>
///   <item>AJAX response helpers — <see cref="FcmsOk"/> / <see cref="FcmsFail"/>
///         emit the consistent <c>{ isSuccess, message, data }</c> envelope used by
///         <c>fcms-actions.js</c>.</item>
///   <item>Cache and session shorthand backed by lazy resolution via
///         <c>HttpContext.RequestServices</c> — no constructor wiring needed in
///         derived classes.</item>
///   <item>A controller-scoped Serilog category, so each controller's log entries
///         carry the correct source name automatically.</item>
/// </list>
///
/// <para>
/// Module controllers normally inherit this directly. <c>BaseAdminController</c>
/// inherits this <em>and</em> adds admin-specific concerns (FcmsAuthorize, DataTables
/// helper) — pick whichever matches the controller's scope.
/// </para>
/// </summary>
public abstract class BaseFcmsController : Controller
{
    // ── TempData keys ─────────────────────────────────────────────────────
    // Kept as constants so the layout can read them without hardcoded strings.
    public const string TempDataMessageKey      = "Toast.Message";
    public const string TempDataTypeKey         = "Toast.Type";
    public const string TempDataDurationKey     = "Toast.Duration";
    public const string TempDataCloseButtonKey  = "Toast.CloseButton";

    public const string ViewBagMessageKey       = "FcmsToastMessage";
    public const string ViewBagTypeKey          = "FcmsToastType";
    public const string ViewBagDurationKey      = "FcmsToastDuration";
    public const string ViewBagCloseButtonKey   = "FcmsToastCloseButton";

    // ── Shorthand for DI services (lazy — no ctor required) ───────────────

    protected IMemoryCache Cache =>
        HttpContext.RequestServices.GetRequiredService<IMemoryCache>();

    /// <summary>
    /// Serilog logger scoped to the concrete controller type — log entries
    /// carry the correct source category (e.g. <c>FlexCms.Module.Investment.Controllers.InvestController</c>)
    /// without each controller having to inject <c>ILogger&lt;T&gt;</c>.
    /// </summary>
    protected ILogger Logger =>
        HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(GetType());

    // ── Toast / flash messages ────────────────────────────────────────────

    /// <summary>
    /// Surface a toast to the user.
    /// </summary>
    /// <param name="message">The text to display. HTML is escaped on the client.</param>
    /// <param name="messageType">Variant — controls the toast colour and icon.</param>
    /// <param name="appendMessage">
    /// When true, the message is appended (with a separator) to any existing message of
    /// the same render target instead of overwriting it. Useful when a controller wants
    /// to enqueue several outcomes before redirecting.
    /// </param>
    /// <param name="showAfterRedirect">
    /// When true the message rides in <c>TempData</c> and renders after the next
    /// redirect (PRG pattern). When false it goes in <see cref="ViewBag"/> for
    /// in-place renders.
    /// </param>
    /// <param name="durationSeconds">Auto-dismiss timeout. 0 = never (sticky toast).</param>
    /// <param name="showCloseButton">When false, the toast renders without the X button.</param>
    [NonAction]
    protected void ShowMessage(
        string message,
        FcmsMessageType messageType = FcmsMessageType.Success,
        bool appendMessage = false,
        bool showAfterRedirect = true,
        int durationSeconds = 5,
        bool showCloseButton = true)
    {
        var variant = messageType.ToString().ToLowerInvariant();

        if (showAfterRedirect)
        {
            var existing = TempData[TempDataMessageKey] as string;
            TempData[TempDataMessageKey] = appendMessage && !string.IsNullOrEmpty(existing)
                ? existing + " | " + message
                : message;
            TempData[TempDataTypeKey] = variant;
            TempData[TempDataDurationKey] = durationSeconds;
            TempData[TempDataCloseButtonKey] = showCloseButton;
        }
        else
        {
            var existing = ViewBag.FcmsToastMessage as string;
            ViewBag.FcmsToastMessage = appendMessage && !string.IsNullOrEmpty(existing)
                ? existing + " | " + message
                : message;
            ViewBag.FcmsToastType = variant;
            ViewBag.FcmsToastDuration = durationSeconds;
            ViewBag.FcmsToastCloseButton = showCloseButton;
        }
    }

    /// <inheritdoc cref="ShowMessage"/>
    [NonAction]
    protected void ShowSuccess(string message, bool appendMessage = false,
                            bool showAfterRedirect = true, int durationSeconds = 5,
                            bool showCloseButton = true)
        => ShowMessage(message, FcmsMessageType.Success, appendMessage, showAfterRedirect, durationSeconds, showCloseButton);

    /// <inheritdoc cref="ShowMessage"/>
    [NonAction]
    protected void ShowError(string message, bool appendMessage = false,
                          bool showAfterRedirect = true, int durationSeconds = 7,
                          bool showCloseButton = true)
        => ShowMessage(message, FcmsMessageType.Danger, appendMessage, showAfterRedirect, durationSeconds, showCloseButton);

    /// <inheritdoc cref="ShowMessage"/>
    [NonAction]
    protected void ShowWarning(string message, bool appendMessage = false,
                            bool showAfterRedirect = true, int durationSeconds = 5,
                            bool showCloseButton = true)
        => ShowMessage(message, FcmsMessageType.Warning, appendMessage, showAfterRedirect, durationSeconds, showCloseButton);

    /// <inheritdoc cref="ShowMessage"/>
    [NonAction]
    protected void ShowInfo(string message, bool appendMessage = false,
                         bool showAfterRedirect = true, int durationSeconds = 5,
                         bool showCloseButton = true)
        => ShowMessage(message, FcmsMessageType.Info, appendMessage, showAfterRedirect, durationSeconds, showCloseButton);

    // ── AJAX response envelope ────────────────────────────────────────────

    /// <summary>
    /// Standard AJAX success envelope consumed by <c>fcms-actions.js</c>.
    /// Returns <c>{ isSuccess: true, message, data }</c>.
    /// </summary>
    protected JsonResult FcmsOk(string? message = null, object? data = null)
        => Json(new { isSuccess = true, message, data });

    /// <summary>
    /// Standard AJAX failure envelope.
    /// Returns <c>{ isSuccess: false, message, errors }</c>.
    /// </summary>
    protected JsonResult FcmsFail(string message, object? errors = null)
        => Json(new { isSuccess = false, message, errors });

    // ── Cache shorthand ───────────────────────────────────────────────────

    protected T? GetCache<T>(string key) where T : class
        => Cache.TryGetValue(key, out T? val) ? val : null;

    protected void SetCache<T>(string key, T value, TimeSpan? ttl = null)
        => Cache.Set(key, value, ttl ?? TimeSpan.FromMinutes(30));

    protected void RemoveCache(string key)
        => Cache.Remove(key);

    // ── Session shorthand (JSON-serialised) ───────────────────────────────

    protected T? GetSession<T>(string key) where T : class
    {
        var json = HttpContext.Session.GetString(key);
        return json is null ? null : JsonSerializer.Deserialize<T>(json);
    }

    protected void SetSession<T>(string key, T value)
        => HttpContext.Session.SetString(key, JsonSerializer.Serialize(value));

    protected void RemoveSession(string key)
        => HttpContext.Session.Remove(key);

    // ── Route shorthand ───────────────────────────────────────────────────

    /// <summary>Active controller route value (without the <c>Controller</c> suffix).</summary>
    protected string ControllerName =>
        ControllerContext.RouteData.Values["controller"]?.ToString() ?? "";

    /// <summary>Active action method name.</summary>
    protected string ActionName =>
        ControllerContext.RouteData.Values["action"]?.ToString() ?? "";

    /// <summary>
    /// Redirect to the framework-provided error page with a one-time message.
    /// </summary>
    protected IActionResult RedirectToErrorPage(string message, string? returnUrl = null)
    {
        TempData["ErrorMessage"] = message;
        return returnUrl is not null
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Home", new { area = "" });
    }
}
