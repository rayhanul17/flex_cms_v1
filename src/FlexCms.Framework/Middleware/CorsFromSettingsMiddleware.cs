using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;

namespace FlexCms.Framework.Middleware;

/// <summary>
/// Read-from-settings CORS middleware. The built-in <c>UseCors</c> bakes its
/// allowed-origin list at startup, but ours needs to track
/// <c>SiteSettings.CorsAllowedOrigins</c> live so admins can add/remove
/// origins without restart. Re-implementing the small slice we need keeps
/// the wiring simple.
///
/// <para>
/// Behavior:
/// <list type="bullet">
///   <item>Disabled (<c>CorsEnabled=false</c>) → middleware is a no-op.</item>
///   <item>Origin not in the allow-list → no CORS headers added; browser blocks.</item>
///   <item>Origin in the allow-list → adds <c>Access-Control-Allow-Origin: {origin}</c>,
///         credentials, methods, headers; replies 204 to OPTIONS preflight.</item>
/// </list>
/// </para>
/// </summary>
public sealed class CorsFromSettingsMiddleware
{
    private readonly RequestDelegate _next;

    public CorsFromSettingsMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ISettingsService settings)
    {
        var origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) { await _next(ctx); return; }

        var snap = await SafeSnapshotAsync(settings);
        if (!snap.Enabled) { await _next(ctx); return; }

        var allowList = (snap.AllowedOrigins ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!allowList.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        // Echo the requesting origin (NOT "*") so credentialed requests work.
        ctx.Response.Headers.AccessControlAllowOrigin = origin;
        ctx.Response.Headers.AccessControlAllowCredentials = "true";
        ctx.Response.Headers.Append("Vary", "Origin");

        if (HttpMethods.IsOptions(ctx.Request.Method))
        {
            // Preflight — short-circuit with 204.
            ctx.Response.Headers.AccessControlAllowMethods = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
            ctx.Response.Headers.AccessControlAllowHeaders = ctx.Request.Headers.AccessControlRequestHeaders.ToString();
            ctx.Response.Headers.AccessControlMaxAge = "600";
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        await _next(ctx);
    }

    private static async Task<CorsSnapshot> SafeSnapshotAsync(ISettingsService settings)
    {
        try { return await settings.GetAsync<CorsSnapshot>("site:general"); }
        catch { return new CorsSnapshot(); }
    }

    /// <summary>Subset of <c>SiteSettings</c> the CORS middleware needs — keeps Framework off Core.</summary>
    public sealed class CorsSnapshot
    {
        public bool CorsEnabled { get; set; }
        public string CorsAllowedOrigins { get; set; } = "";

        // ISettingsService deserializes by property name, so the public property
        // names must match SiteSettings exactly. Aliases that downstream code uses:
        public bool Enabled => CorsEnabled;
        public string AllowedOrigins => CorsAllowedOrigins;
    }
}
