using Microsoft.AspNetCore.Http;

namespace FlexCms.Framework.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["X-XSS-Protection"] = "1; mode=block";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        // CSP — practical baseline:
        //  - default-src 'self'            : block all cross-origin by default
        //  - style-src 'unsafe-inline'     : Bootstrap (and many Razor views) emit inline style="..." attributes
        //  - img-src/font-src data:        : icon fonts and data-URI images
        //  - connect-src ws: wss:          : SignalR + dotnet watch hot reload
        //  - frame-ancestors 'none'        : modern equivalent of X-Frame-Options DENY
        // A stricter nonce-based CSP is planned (see plan B7/M6) but not yet implemented.
        h["Content-Security-Policy"] = string.Join("; ", new[]
        {
            "default-src 'self'",
            "script-src 'self' 'unsafe-inline'",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self' data:",
            "connect-src 'self' ws: wss:",
            "frame-ancestors 'none'"
        });
        // camera=self lets admin pages (KYC capture, document upload) call
        // navigator.mediaDevices.getUserMedia. Other origins embedding our
        // pages in an iframe still cannot. Microphone + geolocation stay
        // disabled — neither is used.
        h["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";

        await _next(context);
    }
}
