using FlexCms.Framework.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace FlexCms.Framework.Middleware;

public class ForcePasswordChangeMiddleware
{
    private readonly RequestDelegate _next;

    public ForcePasswordChangeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, UserManager<FcmsUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated == true)
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
