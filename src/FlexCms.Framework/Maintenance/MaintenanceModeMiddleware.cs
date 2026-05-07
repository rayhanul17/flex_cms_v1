using FlexCms.Framework.Auth;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Encodings.Web;

namespace FlexCms.Framework.Maintenance;

/// <summary>
/// Short-circuits public requests with a 503 + maintenance page when
/// <c>SiteSettings.MaintenanceModeEnabled</c> is true.
///
/// <para>Bypass paths:</para>
/// <list type="bullet">
///   <item>Admin paths (<c>/admin/...</c>, <c>/auth/...</c>) always pass — admins need to flip the toggle back off without locking themselves out.</item>
///   <item>Health checks (<c>/health</c>, <c>/health/live</c>, <c>/health/ready</c>) always pass — load balancers must keep monitoring.</item>
///   <item>Static assets pass — the maintenance page itself needs CSS/icons.</item>
///   <item>Users in any of <c>SiteSettings.MaintenanceAllowedRoles</c> pass.</item>
///   <item>Bypass token: <c>?bypass=...</c> matching <c>SiteSettings.MaintenanceBypassToken</c> sets a session cookie so subsequent requests on that browser also pass.</item>
/// </list>
/// </summary>
public sealed class MaintenanceModeMiddleware
{
    public const string BypassCookieName = "fcms-maintenance-bypass";

    private readonly RequestDelegate _next;
    public MaintenanceModeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ISettingsService settings)
    {
        var snap = await settings.GetAsync<MaintenanceSnapshot>("site:general", ctx.RequestAborted);
        if (!snap.MaintenanceModeEnabled)
        {
            await _next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? "";

        // Always-pass infrastructure paths. Hard-coded — these can't be made
        // user-configurable without risking accidentally locking the admin out.
        if (IsBypassPath(path))
        {
            await _next(ctx);
            return;
        }

        // Bypass-token route: sets a session cookie so subsequent navigation
        // works without keeping ?bypass= in every URL.
        var qsBypass = ctx.Request.Query["bypass"].ToString();
        if (!string.IsNullOrEmpty(snap.MaintenanceBypassToken) &&
            string.Equals(qsBypass, snap.MaintenanceBypassToken, StringComparison.Ordinal))
        {
            ctx.Response.Cookies.Append(BypassCookieName, snap.MaintenanceBypassToken, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = ctx.Request.IsHttps,
                Path = "/",
            });
            await _next(ctx);
            return;
        }

        if (ctx.Request.Cookies.TryGetValue(BypassCookieName, out var cookieToken) &&
            !string.IsNullOrEmpty(snap.MaintenanceBypassToken) &&
            string.Equals(cookieToken, snap.MaintenanceBypassToken, StringComparison.Ordinal))
        {
            await _next(ctx);
            return;
        }

        // Role bypass — applies only to authenticated users. Use IsInRole so
        // multi-role users (e.g. Author + Editor) match if ANY of their roles
        // is in the allowed list.
        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            var allowed = (snap.MaintenanceAllowedRoles ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (allowed.Any(r => ctx.User.IsInRole(r)))
            {
                await _next(ctx);
                return;
            }
        }

        // Render the maintenance page. Try the Razor view first (admin can
        // theme it via /Views/Home/Maintenance.cshtml); fall back to a tiny
        // inline HTML if view rendering fails (DB outage, view engine error).
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.Headers["Retry-After"] = "3600";

        var rendered = await TryRenderViewAsync(ctx, "Maintenance", snap.MaintenanceMessage);
        if (rendered is null)
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(InlineFallbackHtml(snap.MaintenanceMessage), ctx.RequestAborted);
        }
    }

    private static bool IsBypassPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/img/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> TryRenderViewAsync(HttpContext ctx, string viewName, string message)
    {
        try
        {
            var engine = ctx.RequestServices.GetService<ICompositeViewEngine>();
            var tempData = ctx.RequestServices.GetService<ITempDataDictionaryFactory>();
            if (engine is null) return null;

            var routeData = ctx.GetRouteData() ?? new RouteData();
            routeData.Values["controller"] = "Home";
            routeData.Values["action"] = viewName;

            var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
                ctx, routeData, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

            var viewResult = engine.FindView(actionContext, viewName, isMainPage: true);
            if (!viewResult.Success || viewResult.View is null) return null;

            ctx.Response.ContentType = "text/html; charset=utf-8";
            using var sw = new StringWriter();
            var viewData = new ViewDataDictionary(
                new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
            {
                Model = message,
            };
            var viewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext(
                actionContext, viewResult.View, viewData,
                tempData?.GetTempData(ctx) ?? new TempDataDictionary(ctx, ctx.RequestServices.GetRequiredService<ITempDataProvider>()),
                sw, new Microsoft.AspNetCore.Mvc.ViewFeatures.HtmlHelperOptions());

            await viewResult.View.RenderAsync(viewContext);
            await ctx.Response.WriteAsync(sw.ToString(), ctx.RequestAborted);
            return sw.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string InlineFallbackHtml(string message) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Under Maintenance</title>" +
        "<style>body{font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;background:#1a1a1a;color:#fff;text-align:center;padding:2rem}h1{font-size:2.5rem;margin-bottom:1rem}p{font-size:1.1rem;opacity:.85;max-width:600px}</style>" +
        "</head><body><div><h1>Under Maintenance</h1><p>" +
        HtmlEncoder.Default.Encode(message ?? "We're updating the site. Back shortly.") +
        "</p></div></body></html>";

    /// <summary>Local DTO matching the relevant subset of SiteSettings.</summary>
    private sealed class MaintenanceSnapshot
    {
        public bool MaintenanceModeEnabled { get; set; }
        public string MaintenanceMessage { get; set; } = "";
        public string MaintenanceAllowedRoles { get; set; } = "SuperAdmin,Admin";
        public string MaintenanceBypassToken { get; set; } = "";
    }
}
