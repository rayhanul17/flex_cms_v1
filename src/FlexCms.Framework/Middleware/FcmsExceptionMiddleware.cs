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
                body {
                    background: #1a1a2e;
                    color: #fff;
                    font-family: system-ui, -apple-system, sans-serif;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    height: 100vh;
                    margin: 0;
                }
                .box {
                    text-align: center;
                    max-width: 480px;
                    padding: 3rem;
                    background: rgba(255,255,255,0.05);
                    border-radius: 24px;
                    border: 1px solid rgba(255,255,255,0.1);
                    backdrop-filter: blur(10px);
                }
                h1 { font-size: 3rem; margin: 0; color: #ff4d4d; }
                h2 { color: #fff; margin: 1rem 0; }
                p { color: rgba(255,255,255,0.7); line-height: 1.6; }
                a { 
                    display: inline-block;
                    margin-top: 1.5rem;
                    background: #4e54c8;
                    color: #fff;
                    text-decoration: none;
                    padding: 0.8rem 2rem;
                    border-radius: 12px;
                    font-weight: 600;
                }
            </style>
        </head>
        <body>
            <div class="box">
                <h1>500</h1>
                <h2>Unexpected Error</h2>
                <p>An error occurred while processing your request. Our team has been notified.<br/>
                   Please try again or <a href="/">Return Home</a>.</p>
            </div>
        </body>
        </html>
        """;
}
