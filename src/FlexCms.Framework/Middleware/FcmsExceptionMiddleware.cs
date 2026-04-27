using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace FlexCms.Framework.Middleware;

public class FcmsExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FcmsExceptionMiddleware> _logger;

    public FcmsExceptionMiddleware(RequestDelegate next, ILogger<FcmsExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"An unexpected error occurred.\"}");
        }
    }
}
