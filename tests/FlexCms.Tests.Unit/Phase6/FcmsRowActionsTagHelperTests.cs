using System.Security.Claims;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;
using FlexCms.Framework.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase6;

public class FcmsRowActionsTagHelperTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (FcmsRowActionsTagHelper helper, IPermissionService perm, IHttpContextAccessor http) Build(
        bool isSuperAdmin = false,
        bool authenticated = true)
    {
        var perm = Substitute.For<IPermissionService>();
        var http = Substitute.For<IHttpContextAccessor>();

        var claims = new List<Claim>();
        if (isSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, FcmsRoles.SuperAdmin));
        var identity = authenticated ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity();
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        http.HttpContext.Returns(ctx);

        return (new FcmsRowActionsTagHelper(http, perm), perm, http);
    }

    private static TagHelperOutput EmptyOutput() => new(
        "fcms-row-actions",
        new TagHelperAttributeList(),
        (useCachedResult, encoder) =>
            Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

    private static TagHelperContext Ctx() => new(
        new TagHelperAttributeList(),
        new Dictionary<object, object>(),
        Guid.NewGuid().ToString());

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_user_renders_nothing()
    {
        var (h, _, _) = Build(authenticated: false);
        h.EntityId = Guid.NewGuid();
        h.BaseUrl = "/admin/users";
        h.EditPermission = FcmsPermissions.UsersEdit;

        var output = EmptyOutput();
        await h.ProcessAsync(Ctx(), output);

        Assert.True(output.IsContentModified == false || string.IsNullOrEmpty(output.Content.GetContent()));
    }

    [Fact]
    public async Task SuperAdmin_sees_all_buttons_without_permission_check()
    {
        var (h, perm, _) = Build(isSuperAdmin: true);
        h.EntityId = Guid.NewGuid();
        h.BaseUrl = "/admin/users";
        h.Status = EntityStatus.Active;
        h.EditPermission = FcmsPermissions.UsersEdit;
        h.TogglePermission = FcmsPermissions.UsersEdit;
        h.DeletePermission = FcmsPermissions.UsersDelete;

        var output = EmptyOutput();
        await h.ProcessAsync(Ctx(), output);

        var html = output.Content.GetContent();
        Assert.Contains("/edit", html);
        Assert.Contains("toggle-active", html);
        Assert.Contains("data-fcms-action=\"delete\"", html);
        // No permission checks should have happened (SuperAdmin bypass)
        await perm.DidNotReceive().HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task User_without_delete_permission_does_not_see_delete_button()
    {
        var (h, perm, _) = Build();
        h.EntityId = Guid.NewGuid();
        h.BaseUrl = "/admin/users";
        h.Status = EntityStatus.Active;
        h.EditPermission = FcmsPermissions.UsersEdit;
        h.DeletePermission = FcmsPermissions.UsersDelete;

        perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), FcmsPermissions.UsersEdit, Arg.Any<CancellationToken>()).Returns(true);
        perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), FcmsPermissions.UsersDelete, Arg.Any<CancellationToken>()).Returns(false);

        var output = EmptyOutput();
        await h.ProcessAsync(Ctx(), output);

        var html = output.Content.GetContent();
        Assert.Contains("/edit", html);
        Assert.DoesNotContain("data-fcms-action=\"delete\"", html);
    }

    [Fact]
    public async Task Deleted_status_shows_only_restore_button()
    {
        var (h, perm, _) = Build();
        h.EntityId = Guid.NewGuid();
        h.BaseUrl = "/admin/users";
        h.Status = EntityStatus.Deleted;
        h.EditPermission = FcmsPermissions.UsersEdit;
        h.TogglePermission = FcmsPermissions.UsersEdit;
        h.DeletePermission = FcmsPermissions.UsersDelete;

        perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var output = EmptyOutput();
        await h.ProcessAsync(Ctx(), output);

        var html = output.Content.GetContent();
        Assert.DoesNotContain("/edit", html);
        Assert.DoesNotContain("toggle-active", html);
        Assert.DoesNotContain("data-fcms-action=\"delete\"", html);
        Assert.Contains("data-fcms-action=\"restore\"", html);
    }

    [Fact]
    public async Task Active_status_shows_Deactivate_label_on_toggle()
    {
        var (h, perm, _) = Build();
        h.EntityId = Guid.NewGuid();
        h.BaseUrl = "/admin/users";
        h.Status = EntityStatus.Active;
        h.TogglePermission = FcmsPermissions.UsersEdit;
        perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var output = EmptyOutput();
        await h.ProcessAsync(Ctx(), output);

        Assert.Contains("title=\"Deactivate\"", output.Content.GetContent());
    }

    [Fact]
    public async Task InActive_status_shows_Activate_label_on_toggle()
    {
        var (h, perm, _) = Build();
        h.EntityId = Guid.NewGuid();
        h.BaseUrl = "/admin/users";
        h.Status = EntityStatus.InActive;
        h.TogglePermission = FcmsPermissions.UsersEdit;
        perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var output = EmptyOutput();
        await h.ProcessAsync(Ctx(), output);

        Assert.Contains("title=\"Activate\"", output.Content.GetContent());
    }

    [Fact]
    public async Task Confirm_name_appears_in_delete_message()
    {
        var (h, perm, _) = Build();
        h.EntityId = Guid.NewGuid();
        h.BaseUrl = "/admin/users";
        h.Status = EntityStatus.Active;
        h.DeletePermission = FcmsPermissions.UsersDelete;
        h.ConfirmName = "raj@example.com";
        perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var output = EmptyOutput();
        await h.ProcessAsync(Ctx(), output);

        Assert.Contains("raj@example.com", output.Content.GetContent());
    }

    [Fact]
    public async Task Custom_action_with_permission_renders_with_correct_url()
    {
        var (h, perm, _) = Build();
        h.EntityId = Guid.NewGuid();
        h.BaseUrl = "/admin/posts";
        h.Status = EntityStatus.Active;

        h.CustomActions.Add(new FcmsActionTagHelper.ActionData(
            Type: "publish",
            Label: "Publish",
            Icon: "bi-globe",
            Variant: "success",
            Permission: FcmsPermissions.PostsEdit,
            Url: $"/admin/posts/{h.EntityId}/publish",
            ConfirmTitle: "Publish?",
            ConfirmMessage: null,
            ConfirmLabel: null));

        perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), FcmsPermissions.PostsEdit, Arg.Any<CancellationToken>()).Returns(true);

        var output = EmptyOutput();
        await h.ProcessAsync(Ctx(), output);

        var html = output.Content.GetContent();
        Assert.Contains("/publish", html);
        Assert.Contains("Publish", html);
        Assert.Contains("data-fcms-action=\"custom\"", html);
        Assert.Contains("data-confirm-title=\"Publish?\"", html);
    }

    [Fact]
    public async Task Custom_action_user_lacks_permission_does_not_render()
    {
        var (h, perm, _) = Build();
        h.EntityId = Guid.NewGuid();
        h.BaseUrl = "/admin/posts";
        h.Status = EntityStatus.Active;

        h.CustomActions.Add(new FcmsActionTagHelper.ActionData(
            "publish", "Publish", "bi-globe", "success",
            FcmsPermissions.PostsEdit, null, null, null, null));

        perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var output = EmptyOutput();
        await h.ProcessAsync(Ctx(), output);

        Assert.DoesNotContain("/publish", output.Content.GetContent());
    }
}
