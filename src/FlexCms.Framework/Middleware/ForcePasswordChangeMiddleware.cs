using FlexCms.Framework.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Middleware;

public class ForcePasswordChangeMiddleware
{
    private readonly RequestDelegate _next;

    public ForcePasswordChangeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IServiceProvider services)
    {
        var userManager = services.GetService<UserManager<FcmsUser>>();
        if (userManager is not null && context.User.Identity?.IsAuthenticated == true)
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user?.ForcePasswordChange == true &&
                !context.Request.Path.StartsWithSegments("/auth/change-password") &&
                !context.Request.Path.StartsWithSegments("/auth/logout"))
            {
                context.Response.Redirect("/auth/change-password");
                return;
            }
        }

        await _next(context);
    }
}
