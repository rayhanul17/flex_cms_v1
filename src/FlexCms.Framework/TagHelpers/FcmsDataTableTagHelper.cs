using System.Text;
using System.Text.Json;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FlexCms.Framework.TagHelpers;

/// <summary>
/// Renders a server-side jQuery DataTable + JS init script in one go. Children
/// (&lt;fcms-data-column&gt;, optionally &lt;fcms-data-actions&gt; with
/// &lt;fcms-data-action&gt;) define columns and action buttons.
///
/// The TagHelper evaluates user permissions ONCE here (Razor render time) and
/// emits <c>visible: true/false</c> flags into the JS init config — JS never
/// receives permission key strings.
/// </summary>
[HtmlTargetElement("fcms-data-table")]
[RestrictChildren("fcms-data-column", "fcms-data-actions")]
public sealed class FcmsDataTableTagHelper : TagHelper
{
    private readonly IPermissionService? _permService;
    private readonly IHttpContextAccessor _httpCtx;

    public FcmsDataTableTagHelper(IHttpContextAccessor httpCtx, IPermissionService? permService = null)
    {
        _httpCtx = httpCtx;
        _permService = permService;
    }

    [HtmlAttributeName("id")] public string? Id { get; set; }

    /// <summary>Endpoint that returns the DataTablesResponse JSON.</summary>
    [HtmlAttributeName("url")] public string Url { get; set; } = "";

    /// <summary>Base URL for action endpoints (Edit/Delete/Toggle/etc.). e.g. /admin/pages</summary>
    [HtmlAttributeName("base-url")] public string BaseUrl { get; set; } = "";

    /// <summary>Field name carrying the friendly entity name (used in delete confirm).</summary>
    [HtmlAttributeName("confirm-name-field")] public string? ConfirmNameField { get; set; }

    [HtmlAttributeName("page-length")] public int PageLength { get; set; } = 25;

    // Standard 4 actions — pass permission keys; if any non-null, an Actions column is auto-added
    [HtmlAttributeName("edit-permission")] public string? EditPermission { get; set; }
    [HtmlAttributeName("toggle-permission")] public string? TogglePermission { get; set; }
    [HtmlAttributeName("delete-permission")] public string? DeletePermission { get; set; }
    [HtmlAttributeName("restore-permission")] public string? RestorePermission { get; set; }

    // Children collected via context.Items
    [HtmlAttributeNotBound] public List<ColumnData> Columns { get; } = new();
    [HtmlAttributeNotBound] public List<FcmsDataActionTagHelper.ActionData> CustomActions { get; } = new();

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var tableId = string.IsNullOrWhiteSpace(Id) ? "fcmsTbl_" + Guid.NewGuid().ToString("N")[..8] : Id;

        // Make this instance available to children
        context.Items[typeof(FcmsDataTableTagHelper)] = this;
        await output.GetChildContentAsync();

        // Permission resolution
        var user = _httpCtx.HttpContext?.User;
        var isSuperAdmin = user?.IsInRole(FcmsRoles.SuperAdmin) == true;

        async Task<bool> Allowed(string? perm)
        {
            if (string.IsNullOrWhiteSpace(perm)) return true;
            if (isSuperAdmin) return true;
            if (_permService is null || user is null) return false;
            return await _permService.HasPermissionAsync(user, perm, _httpCtx.HttpContext!.RequestAborted);
        }

        var hasActionsColumn = EditPermission is not null
                            || TogglePermission is not null
                            || DeletePermission is not null
                            || RestorePermission is not null
                            || CustomActions.Count > 0;

        // Build JSON config for fcms.dataTable()
        var cfg = new
        {
            url = Url,
            baseUrl = BaseUrl,
            pageLength = PageLength,
            confirmNameField = ConfirmNameField,
            columns = Columns.Select(c => new
            {
                field = c.Field,
                type = c.Type,
                sortable = c.Sortable,
                searchable = c.Searchable
            }).ToArray(),
            actions = hasActionsColumn ? new
            {
                edit = new { visible = await Allowed(EditPermission) && EditPermission is not null },
                toggle = new { visible = await Allowed(TogglePermission) && TogglePermission is not null },
                @delete = new { visible = await Allowed(DeletePermission) && DeletePermission is not null },
                restore = new { visible = await Allowed(RestorePermission ?? DeletePermission) && (RestorePermission ?? DeletePermission) is not null },
                custom = await ResolveCustomActions(Allowed)
            } : null,
            defaultSort = ResolveDefaultSort()
        };

        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        // Build the table HTML shell
        var sb = new StringBuilder();
        sb.Append($"<table id=\"{tableId}\" class=\"table table-hover w-100 align-middle\"><thead><tr>");
        foreach (var col in Columns)
            sb.Append($"<th>{System.Web.HttpUtility.HtmlEncode(col.Header ?? col.Field)}</th>");
        if (hasActionsColumn)
            sb.Append("<th class=\"text-end\">Actions</th>");
        sb.Append("</tr></thead><tbody></tbody></table>");

        // Init script
        sb.Append("<script>(function(){function init(){if(typeof jQuery==='undefined'||typeof fcms==='undefined'||!fcms.dataTable){setTimeout(init,30);return;}");
        sb.Append($"fcms.dataTable('#{tableId}', {json});");
        sb.Append("}init();})();</script>");

        output.TagName = null;
        output.Content.SetHtmlContent(sb.ToString());
    }

    private async Task<object[]> ResolveCustomActions(Func<string?, Task<bool>> allowed)
    {
        var list = new List<object>();
        foreach (var a in CustomActions)
        {
            list.Add(new
            {
                visible = await allowed(a.Permission),
                label = a.Label,
                icon = a.Icon,
                variant = a.Variant ?? "secondary",
                urlTemplate = a.UrlTemplate ?? $"{BaseUrl.TrimEnd('/')}/{{id}}/{a.Type}",
                confirmTitle = a.ConfirmTitle,
                confirmMessage = a.ConfirmMessage,
                confirmLabel = a.ConfirmLabel
            });
        }
        return list.ToArray();
    }

    private object[] ResolveDefaultSort()
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            var c = Columns[i];
            if (!string.IsNullOrEmpty(c.DefaultSort))
                return new object[] { new object[] { i, c.DefaultSort } };
        }
        return new object[] { new object[] { 0, "asc" } };
    }

    public sealed record ColumnData(
        string Field,
        string? Header,
        string? Type,        // "status", "date", "bool", "code", or null (text)
        bool Sortable,
        bool Searchable,
        string? DefaultSort  // "asc" | "desc" | null
    );
}

[HtmlTargetElement("fcms-data-column", ParentTag = "fcms-data-table", TagStructure = TagStructure.WithoutEndTag)]
public sealed class FcmsDataColumnTagHelper : TagHelper
{
    [HtmlAttributeName("field")] public string Field { get; set; } = "";
    [HtmlAttributeName("header")] public string? Header { get; set; }
    [HtmlAttributeName("type")] public string? Type { get; set; }
    [HtmlAttributeName("sortable")] public bool Sortable { get; set; } = true;
    [HtmlAttributeName("searchable")] public bool Searchable { get; set; } = true;
    [HtmlAttributeName("default-sort")] public string? DefaultSort { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (context.Items.TryGetValue(typeof(FcmsDataTableTagHelper), out var p)
            && p is FcmsDataTableTagHelper parent)
        {
            parent.Columns.Add(new FcmsDataTableTagHelper.ColumnData(
                Field, Header, Type, Sortable, Searchable, DefaultSort));
        }
        output.SuppressOutput();
    }
}

[HtmlTargetElement("fcms-data-actions", ParentTag = "fcms-data-table")]
[RestrictChildren("fcms-data-action")]
public sealed class FcmsDataActionsTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Just iterate children — they'll register themselves on the parent table via context.Items
        await output.GetChildContentAsync();
        output.SuppressOutput();
    }
}

[HtmlTargetElement("fcms-data-action", ParentTag = "fcms-data-actions", TagStructure = TagStructure.WithoutEndTag)]
public sealed class FcmsDataActionTagHelper : TagHelper
{
    [HtmlAttributeName("type")] public string Type { get; set; } = "custom";
    [HtmlAttributeName("label")] public string? Label { get; set; }
    [HtmlAttributeName("icon")] public string? Icon { get; set; }
    [HtmlAttributeName("variant")] public string? Variant { get; set; }
    [HtmlAttributeName("permission")] public string? Permission { get; set; }
    [HtmlAttributeName("url-template")] public string? UrlTemplate { get; set; }
    [HtmlAttributeName("confirm-title")] public string? ConfirmTitle { get; set; }
    [HtmlAttributeName("confirm-message")] public string? ConfirmMessage { get; set; }
    [HtmlAttributeName("confirm-label")] public string? ConfirmLabel { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (context.Items.TryGetValue(typeof(FcmsDataTableTagHelper), out var p)
            && p is FcmsDataTableTagHelper parent)
        {
            parent.CustomActions.Add(new ActionData(
                Type, Label, Icon, Variant, Permission, UrlTemplate,
                ConfirmTitle, ConfirmMessage, ConfirmLabel));
        }
        output.SuppressOutput();
    }

    public sealed record ActionData(
        string Type, string? Label, string? Icon, string? Variant,
        string? Permission, string? UrlTemplate,
        string? ConfirmTitle, string? ConfirmMessage, string? ConfirmLabel);
}
