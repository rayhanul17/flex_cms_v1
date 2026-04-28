using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;

namespace FlexCms.Framework.Middleware;

public class FcmsExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FcmsExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public FcmsExceptionMiddleware(
        RequestDelegate next,
        ILogger<FcmsExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (_env.IsDevelopment())
            {
                // Development: rethrow so the built-in developer exception page
                // shows full stack trace, route info, and request details
                throw;
            }

            // Production: generic response
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            if (IsApiRequest(context))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "{\"error\":\"An unexpected error occurred. Please try again later.\"}");
            }
            else
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(GenericErrorHtml());
            }
        }
    }

    private static bool IsApiRequest(HttpContext context)
    {
        var accept = context.Request.Headers.Accept.ToString();
        var path = context.Request.Path.Value ?? "";
        return accept.Contains("application/json") || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GenericErrorHtml() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8"/>
            <meta name="viewport" content="width=device-width,initial-scale=1"/>
            <title>Something went wrong</title>
            <style>
                body{font-family:system-ui,sans-serif;background:#f8f9fa;display:flex;
                     align-items:center;justify-content:center;height:100vh;margin:0}
                .box{text-align:center;max-width:480px;padding:2rem}
                h1{font-size:4rem;color:#dee2e6;margin:0}
                h2{color:#343a40;margin:.5rem 0}
                p{color:#6c757d}
                a{color:#0d6efd;text-decoration:none}
            </style>
        </head>
        <body>
            <div class="box">
                <h1>500</h1>
                <h2>Something went wrong</h2>
                <p>An unexpected error occurred. The issue has been logged.<br/>
                   Please try again or <a href="/">return to the homepage</a>.</p>
            </div>
        </body>
        </html>
        """;
}
