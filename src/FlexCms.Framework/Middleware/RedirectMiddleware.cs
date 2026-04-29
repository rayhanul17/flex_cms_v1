using FlexCms.Framework.Db.Ef;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Middleware;

/// <summary>
/// Checks incoming request paths against the FcmsRedirect table and issues
/// 301/302 redirects when a match is found. Only runs for GET/HEAD requests.
/// </summary>
public class RedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RedirectMiddleware> _logger;

    public RedirectMiddleware(RequestDelegate next, ILogger<RedirectMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Method is "GET" or "HEAD")
        {
            var path = ctx.Request.Path.Value ?? "/";
            var db = ctx.RequestServices.GetService<FcmsDbContext>();
            if (db is not null)
            {
                var redirect = await db.Redirects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => !r.IsDeleted && r.IsActive && r.FromPath == path);

                if (redirect is not null)
                {
                    ctx.Response.StatusCode = redirect.StatusCode;
                    ctx.Response.Headers.Location = redirect.ToPath;

                    // fire-and-forget HitCount increment — don't block the redirect response
                    _ = IncrementHitCountAsync(ctx.RequestServices, redirect.Id);

                    return;
                }
            }
        }

        await _next(ctx);
    }

    private async Task IncrementHitCountAsync(IServiceProvider services, Guid redirectId)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FcmsDbContext>();
            await db.Redirects
                .Where(r => r.Id == redirectId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.HitCount, r => r.HitCount + 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RedirectMiddleware: failed to increment HitCount for {Id}.", redirectId);
        }
    }
}
