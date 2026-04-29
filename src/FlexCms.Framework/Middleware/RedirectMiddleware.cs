using FlexCms.Framework.Db.Ef;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Middleware;

/// <summary>
/// Checks incoming request paths against the FcmsRedirect table and issues
/// 301/302 redirects when a match is found. Only runs for GET/HEAD requests.
/// </summary>
public class RedirectMiddleware
{
    private readonly RequestDelegate _next;

    public RedirectMiddleware(RequestDelegate next) => _next = next;

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
                    return;
                }
            }
        }

        await _next(ctx);
    }
}
