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
            if (user?.ForcePasswordChange == true && !IsAllowedPath(context.Request.Path))
            {
                context.Response.Redirect("/auth/change-password");
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Paths a force-password-change user can still reach without being looped back.
    /// Includes /Home/Error so the StatusCodePagesWithReExecute target doesn't
    /// trigger an infinite redirect when something 404s during this state.
    /// </summary>
    private static bool IsAllowedPath(PathString path)
        => path.StartsWithSegments("/auth/change-password")
        || path.StartsWithSegments("/Auth/ChangePassword")
        || path.StartsWithSegments("/auth/logout")
        || path.StartsWithSegments("/Auth/Logout")
        || path.StartsWithSegments("/Home/Error");
}
