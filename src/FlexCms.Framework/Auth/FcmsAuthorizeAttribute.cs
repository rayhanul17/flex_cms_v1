using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Auth;

/// <summary>
/// Requires the user to be authenticated. SuperAdmin role bypasses all permission checks.
/// <para>
/// Usage:<br/>
/// [FcmsAuthorize]                      — login required only<br/>
/// [FcmsAuthorize(FcmsPermissions.UsersCreate)]      — login + SuperAdmin OR has permission<br/>
/// [FcmsAuthorize("a&amp;b")]           — login + SuperAdmin OR has BOTH a AND b<br/>
/// [FcmsAuthorize("a|b")]               — login + SuperAdmin OR has a OR b
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class FcmsAuthorizeAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    public string? Permission { get; }
    public int Order => 0;
    public bool IsReusable => false;

    public FcmsAuthorizeAttribute(string? permission = null)
    {
        Permission = permission;
    }

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var permService = serviceProvider.GetService<IPermissionService>();
        return new FcmsAuthorizeFilter(Permission, permService);
    }
}

internal sealed class FcmsAuthorizeFilter : IAsyncAuthorizationFilter
{
    private readonly string? _permission;
    private readonly IPermissionService? _permService;

    public FcmsAuthorizeFilter(string? permission, IPermissionService? permService)
    {
        _permission = permission;
        _permService = permService;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = IsAjax(context.HttpContext.Request)
                ? Forbidden("Authentication required.")
                : new ChallengeResult();
            return;
        }

        // SuperAdmin bypasses all permission checks.
        // Check both the standard ClaimTypes.Role claim and the normalized-uppercase variant
        // that MongoUserStore stores (UserManager.AddToRoleAsync normalizes role names).
        if (user.IsInRole(FcmsRoles.SuperAdmin) ||
            user.IsInRole(FcmsRoles.SuperAdmin.ToUpperInvariant())) return;

        if (_permission is null) return;

        // No PermissionService registered yet (Phase 3 not fully wired) → deny non-SuperAdmin
        if (_permService is null)
        {
            context.Result = IsAjax(context.HttpContext.Request)
                ? Forbidden("Access denied.")
                : new ForbidResult();
            return;
        }

        var hasPermission = await _permService.HasPermissionAsync(
            user, _permission, context.HttpContext.RequestAborted);

        if (!hasPermission)
        {
            context.Result = IsAjax(context.HttpContext.Request)
                ? Forbidden("You do not have permission to perform this action.")
                : new ForbidResult();
        }
    }

    private static bool IsAjax(HttpRequest req)
        => req.Headers["X-Requested-With"] == "XMLHttpRequest"
        || (req.Headers.Accept.ToString().Contains("application/json")
            && !req.Headers.Accept.ToString().Contains("text/html"));

    private static JsonResult Forbidden(string message)
        => new(new { isSuccess = false, message })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
}
