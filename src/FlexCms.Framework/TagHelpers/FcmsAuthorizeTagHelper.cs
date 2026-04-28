using FlexCms.Framework.Auth;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FlexCms.Framework.TagHelpers;

/// <summary>
/// Hides the element if the current user does not have the required permission.
/// SuperAdmin always sees everything.
/// <code>
/// &lt;button fcms-authorize="users.create"&gt;Add User&lt;/button&gt;
/// &lt;a fcms-authorize="posts.edit|posts.create" href="..."&gt;Edit&lt;/a&gt;
/// </code>
/// </summary>
[HtmlTargetElement(Attributes = "fcms-authorize")]
public sealed class FcmsAuthorizeTagHelper : TagHelper
{
    private readonly IPermissionService? _permService;
    private readonly IHttpContextAccessor _httpCtx;

    [HtmlAttributeName("fcms-authorize")]
    public string Permission { get; set; } = "";

    public FcmsAuthorizeTagHelper(IHttpContextAccessor httpCtx, IPermissionService? permService = null)
    {
        _httpCtx = httpCtx;
        _permService = permService;
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var user = _httpCtx.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            output.SuppressOutput();
            return;
        }

        if (user.IsInRole(FcmsRoles.SuperAdmin)) return;

        if (_permService is null || string.IsNullOrWhiteSpace(Permission))
        {
            output.SuppressOutput();
            return;
        }

        var hasPermission = await _permService.HasPermissionAsync(
            user, Permission, _httpCtx.HttpContext!.RequestAborted);

        if (!hasPermission)
            output.SuppressOutput();
    }
}
