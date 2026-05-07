using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Models;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
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

    protected IFcmsLogService OpLog =>
        HttpContext.RequestServices.GetRequiredService<IFcmsLogService>();

    /// <summary>
    /// Serilog logger scoped to the concrete controller type — so log entries
    /// carry the correct source category (e.g. "FlexCms.Host.Controllers.Admin.SettingsController")
    /// without each controller having to inject ILogger&lt;T&gt; in its constructor.
    /// </summary>
    protected ILogger Logger =>
        HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(GetType());

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

    // ── DataTable helper (server-side processing + auto permission flags) ─────

    /// <summary>
    /// Build a server-side DataTables JSON response from an EF query.
    ///
    /// <example>
    /// [HttpPost("datatable")]
    /// public Task&lt;IActionResult&gt; DataTable([FromForm] DataTablesRequest req, CancellationToken ct)
    ///     =&gt; DataTableResult(_db.Pages, req,
    ///            select: p =&gt; new { p.Id, p.Title, p.Slug, Status = (int)p.Status, p.UpdatedAt },
    ///            orderColumns: new Expression&lt;Func&lt;FcmsPage, object&gt;&gt;[] {
    ///                p =&gt; p.Title, p =&gt; p.Slug, p =&gt; p.Status, p =&gt; p.UpdatedAt!
    ///            },
    ///            globalSearch: q =&gt; p =&gt; p.Title.Contains(q) || p.Slug.Contains(q),
    ///            permissions: new() { ["canEdit"] = FcmsPermissions.PagesEdit, ["canDelete"] = FcmsPermissions.PagesDelete },
    ///            ct: ct);
    /// </example>
    /// </summary>
    protected async Task<IActionResult> DataTableResult<TEntity, TResult>(
        IQueryable<TEntity> source,
        DataTablesRequest req,
        Expression<Func<TEntity, TResult>> select,
        IReadOnlyList<Expression<Func<TEntity, object>>> orderColumns,
        Func<string, Expression<Func<TEntity, bool>>>? globalSearch = null,
        Dictionary<string, string>? permissions = null,
        CancellationToken ct = default)
        where TEntity : class
    {
        Expression<Func<TEntity, bool>>? searchFilter = null;
        if (globalSearch is not null && !string.IsNullOrWhiteSpace(req.SearchValue))
            searchFilter = globalSearch(req.SearchValue);

        var response = await source.ToDataTableAsync(req, select, searchFilter, orderColumns, ct);

        if (permissions is { Count: > 0 })
        {
            var permService = HttpContext.RequestServices.GetService<IPermissionService>();
            var user = HttpContext.User;
            var isSuperAdmin = user.IsInRole(FcmsRoles.SuperAdmin);

            foreach (var (flagName, permKey) in permissions)
            {
                if (isSuperAdmin) { response.Permissions[flagName] = true; continue; }
                response.Permissions[flagName] = permService is not null
                    && await permService.HasPermissionAsync(user, permKey, ct);
            }
        }

        return Json(response);
    }
}
