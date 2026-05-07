using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;

namespace FlexCms.Framework.Middleware;

/// <summary>
/// Blocks external sites from <c>&lt;img src&gt;</c>-ing the site's media
/// files (drains bandwidth, masks attribution). When
/// <c>SiteSettings.PreventHotlinking</c> is on, requests under
/// <c>/uploads/</c> with a <c>Referer</c> outside the host's own domain
/// (or the <c>HotlinkWhitelist</c>) are 403'd.
///
/// <para>
/// Same-origin requests pass (Referer empty or starts with our host).
/// First-party direct visits (URL in browser bar, no Referer) also pass —
/// most users opening an image link directly.
/// </para>
/// </summary>
public sealed class HotlinkProtectionMiddleware
{
    private readonly RequestDelegate _next;
    public HotlinkProtectionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ISettingsService settings)
    {
        var path = ctx.Request.Path.Value ?? "";
        // Cheap pre-filter: only inspect requests under /uploads/ — settings
        // lookup is async + per-request, so skip it for everything else.
        if (!path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var snap = await settings.GetAsync<HotlinkSnapshot>("site:general", ctx.RequestAborted);
        if (!snap.PreventHotlinking)
        {
            await _next(ctx);
            return;
        }

        var referer = ctx.Request.Headers["Referer"].ToString();

        // Direct hit (no referer) → allow. Browsers also strip Referer when
        // following an HTTPS→HTTP downgrade or with Referrer-Policy=no-referrer.
        if (string.IsNullOrEmpty(referer))
        {
            await _next(ctx);
            return;
        }

        // Same-origin → allow. Compare the host of the referer URL against
        // the request's own host (handles port + scheme via Uri parsing).
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            if (string.Equals(refererUri.Host, ctx.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }

            // Whitelist match — comma-separated list of allowed hostnames
            // (e.g. "partner.com,cdn.partner.com"). We compare on host only;
            // operators don't have to think about scheme / paths.
            if (!string.IsNullOrWhiteSpace(snap.HotlinkWhitelist))
            {
                var allowed = snap.HotlinkWhitelist
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (allowed.Any(h => string.Equals(h, refererUri.Host, StringComparison.OrdinalIgnoreCase)))
                {
                    await _next(ctx);
                    return;
                }
            }
        }

        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsync("Hotlinking not permitted.", ctx.RequestAborted);
    }

    private sealed class HotlinkSnapshot
    {
        public bool PreventHotlinking { get; set; }
        public string HotlinkWhitelist { get; set; } = "";
    }
}
