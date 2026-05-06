using System.Text;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FlexCms.Framework.TagHelpers;

/// <summary>
/// Renders a permission-filtered action-button group for an entity row.
/// Standard buttons (Edit / Toggle / Delete / Restore) are emitted via attributes;
/// extra buttons can be injected via child &lt;fcms-action&gt; elements.
///
/// <code>
/// &lt;fcms-row-actions
///     entity-id="@u.Id"
///     base-url="/admin/users"
///     status="@u.Status"
///     edit-permission="@FcmsPermissions.UsersEdit"
///     toggle-permission="@FcmsPermissions.UsersEdit"
///     delete-permission="@FcmsPermissions.UsersDelete"
///     confirm-name="@u.Email" /&gt;
/// </code>
/// </summary>
[HtmlTargetElement("fcms-row-actions")]
[RestrictChildren("fcms-action")]
public sealed class FcmsRowActionsTagHelper : TagHelper
{
    private readonly IPermissionService? _permService;
    private readonly IHttpContextAccessor _httpCtx;

    public FcmsRowActionsTagHelper(IHttpContextAccessor httpCtx, IPermissionService? permService = null)
    {
        _httpCtx = httpCtx;
        _permService = permService;
    }

    [HtmlAttributeName("entity-id")]
    public Guid EntityId { get; set; }

    [HtmlAttributeName("base-url")]
    public string BaseUrl { get; set; } = "";

    [HtmlAttributeName("status")]
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    /// <summary>Friendly name shown in the delete confirm dialog. Optional.</summary>
    [HtmlAttributeName("confirm-name")]
    public string? ConfirmName { get; set; }

    [HtmlAttributeName("edit-permission")] public string? EditPermission { get; set; }
    [HtmlAttributeName("toggle-permission")] public string? TogglePermission { get; set; }
    [HtmlAttributeName("delete-permission")] public string? DeletePermission { get; set; }
    [HtmlAttributeName("restore-permission")] public string? RestorePermission { get; set; }

    /// <summary>Custom action child elements (collected during ProcessAsync).</summary>
    [HtmlAttributeNotBound]
    public List<FcmsActionTagHelper.ActionData> CustomActions { get; } = new();

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Stash this instance so child <fcms-action> tag helpers can register themselves
        context.Items[typeof(FcmsRowActionsTagHelper)] = this;
        await output.GetChildContentAsync();

        var user = _httpCtx.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            output.SuppressOutput();
            return;
        }

        var isSuperAdmin = user.IsInRole(FcmsRoles.SuperAdmin);

        async Task<bool> Allowed(string? perm)
        {
            if (string.IsNullOrWhiteSpace(perm)) return true;
            if (isSuperAdmin) return true;
            if (_permService is null) return false;
            return await _permService.HasPermissionAsync(user, perm, _httpCtx.HttpContext!.RequestAborted);
        }

        var sb = new StringBuilder();
        sb.Append("<div class=\"btn-group btn-group-sm\" role=\"group\">");

        var isDeleted = Status == EntityStatus.Deleted;
        var isActive = Status == EntityStatus.Active;
        var baseUrl = (BaseUrl ?? "").TrimEnd('/');
        var displayName = string.IsNullOrWhiteSpace(ConfirmName) ? "this item" : System.Web.HttpUtility.HtmlEncode(ConfirmName);

        // Edit (link, not AJAX)
        if (!isDeleted && await Allowed(EditPermission))
            sb.Append($"<a class=\"btn btn-outline-info\" href=\"{baseUrl}/{EntityId}/edit\" title=\"Edit\"><i class=\"bi bi-pencil\"></i></a>");

        // Toggle Active/InActive (no confirm — direct AJAX)
        if (!isDeleted && await Allowed(TogglePermission))
        {
            var label = isActive ? "Deactivate" : "Activate";
            var icon = isActive ? "bi-pause-circle" : "bi-play-circle";
            var variant = isActive ? "warning" : "success";
            sb.Append($"<button type=\"button\" class=\"btn btn-outline-{variant}\" title=\"{label}\""
                    + $" data-fcms-action=\"toggle-active\" data-url=\"{baseUrl}/{EntityId}/toggle-active\">"
                    + $"<i class=\"bi {icon}\"></i></button>");
        }

        // Delete (with confirm)
        if (!isDeleted && await Allowed(DeletePermission))
            sb.Append($"<button type=\"button\" class=\"btn btn-outline-danger\" title=\"Delete\""
                    + $" data-fcms-action=\"delete\""
                    + $" data-url=\"{baseUrl}/{EntityId}/delete\""
                    + $" data-confirm-title=\"Delete?\""
                    + $" data-confirm-message=\"Move {displayName} to trash?\""
                    + $" data-confirm-label=\"Delete\""
                    + $" data-confirm-variant=\"danger\">"
                    + $"<i class=\"bi bi-trash\"></i></button>");

        // Restore (only when soft-deleted) — uses delete-permission unless restore-permission given
        if (isDeleted && await Allowed(RestorePermission ?? DeletePermission))
            sb.Append($"<button type=\"button\" class=\"btn btn-outline-success\" title=\"Restore\""
                    + $" data-fcms-action=\"restore\""
                    + $" data-url=\"{baseUrl}/{EntityId}/restore\""
                    + $" data-confirm-title=\"Restore?\""
                    + $" data-confirm-message=\"Restore {displayName}?\""
                    + $" data-confirm-label=\"Restore\""
                    + $" data-confirm-variant=\"success\">"
                    + $"<i class=\"bi bi-arrow-counterclockwise\"></i></button>");

        // Custom child actions
        foreach (var a in CustomActions)
        {
            if (!await Allowed(a.Permission)) continue;

            var url = a.Url is null ? $"{baseUrl}/{EntityId}/{a.Type}" : a.Url;
            var variant = a.Variant ?? "secondary";
            var icon = a.Icon ?? "bi-box";
            var label = a.Label ?? a.Type;
            var attrs = new StringBuilder();
            attrs.Append($" data-fcms-action=\"custom\" data-url=\"{System.Web.HttpUtility.HtmlAttributeEncode(url)}\"");
            if (!string.IsNullOrWhiteSpace(a.ConfirmTitle))
            {
                attrs.Append($" data-confirm-title=\"{System.Web.HttpUtility.HtmlAttributeEncode(a.ConfirmTitle)}\"");
                if (!string.IsNullOrWhiteSpace(a.ConfirmMessage))
                    attrs.Append($" data-confirm-message=\"{System.Web.HttpUtility.HtmlAttributeEncode(a.ConfirmMessage)}\"");
                attrs.Append($" data-confirm-label=\"{System.Web.HttpUtility.HtmlAttributeEncode(a.ConfirmLabel ?? label)}\"");
                attrs.Append($" data-confirm-variant=\"{variant}\"");
            }
            sb.Append($"<button type=\"button\" class=\"btn btn-outline-{variant}\" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(label)}\"{attrs}>"
                    + $"<i class=\"bi {icon}\"></i></button>");
        }

        sb.Append("</div>");
        output.TagName = null; // Don't render the wrapper element
        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>
/// Child element of <see cref="FcmsRowActionsTagHelper"/>. Defines an extra button
/// (besides the standard Edit/Toggle/Delete/Restore).
/// </summary>
[HtmlTargetElement("fcms-action", ParentTag = "fcms-row-actions", TagStructure = TagStructure.WithoutEndTag)]
public sealed class FcmsActionTagHelper : TagHelper
{
    /// <summary>"edit" / "toggle" / "delete" / "restore" / "custom" / arbitrary suffix.</summary>
    [HtmlAttributeName("type")]
    public string Type { get; set; } = "custom";

    [HtmlAttributeName("label")] public string? Label { get; set; }
    [HtmlAttributeName("icon")] public string? Icon { get; set; }

    /// <summary>success / danger / warning / info / primary / secondary.</summary>
    [HtmlAttributeName("variant")] public string? Variant { get; set; }

    [HtmlAttributeName("permission")] public string? Permission { get; set; }

    /// <summary>Full URL. If null, defaults to {base-url}/{entity-id}/{type}.</summary>
    [HtmlAttributeName("url")] public string? Url { get; set; }

    [HtmlAttributeName("confirm-title")] public string? ConfirmTitle { get; set; }
    [HtmlAttributeName("confirm-message")] public string? ConfirmMessage { get; set; }
    [HtmlAttributeName("confirm-label")] public string? ConfirmLabel { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (context.Items.TryGetValue(typeof(FcmsRowActionsTagHelper), out var parent)
            && parent is FcmsRowActionsTagHelper p)
        {
            p.CustomActions.Add(new ActionData(Type, Label, Icon, Variant, Permission, Url, ConfirmTitle, ConfirmMessage, ConfirmLabel));
        }
        output.SuppressOutput();
    }

    public sealed record ActionData(
        string Type,
        string? Label,
        string? Icon,
        string? Variant,
        string? Permission,
        string? Url,
        string? ConfirmTitle,
        string? ConfirmMessage,
        string? ConfirmLabel);
}
