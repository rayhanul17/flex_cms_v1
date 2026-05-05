using FlexCms.Framework.Auth;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using System.Security.Claims;

namespace FlexCms.Tests.Unit.Phase3;

public class FcmsAuthorizeFilterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AuthorizationFilterContext BuildContext(
        ClaimsPrincipal user,
        bool isAjax = false)
    {
        var httpContext = new DefaultHttpContext { User = user };
        if (isAjax)
            httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new AuthorizationFilterContext(actionContext, []);
    }

    private static ClaimsPrincipal Unauthenticated()
        => new(new ClaimsIdentity()); // no auth type → IsAuthenticated = false

    private static ClaimsPrincipal AuthenticatedUser(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "test@example.com")
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static IPermissionService PermService(bool hasPermission)
    {
        var svc = Substitute.For<IPermissionService>();
        svc.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(hasPermission);
        return svc;
    }

    // ── Unauthenticated ───────────────────────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_browser_request_returns_challenge()
    {
        var attr = new FcmsAuthorizeAttribute();
        var filter = (IAsyncAuthorizationFilter)attr.CreateInstance(
            BuildServiceProvider(permService: null));

        var ctx = BuildContext(Unauthenticated());
        await filter.OnAuthorizationAsync(ctx);

        Assert.IsType<ChallengeResult>(ctx.Result);
    }

    [Fact]
    public async Task Unauthenticated_ajax_request_returns_403_json()
    {
        var attr = new FcmsAuthorizeAttribute();
        var filter = (IAsyncAuthorizationFilter)attr.CreateInstance(
            BuildServiceProvider(permService: null));

        var ctx = BuildContext(Unauthenticated(), isAjax: true);
        await filter.OnAuthorizationAsync(ctx);

        var result = Assert.IsType<JsonResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    // ── SuperAdmin bypass ─────────────────────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_with_permission_required_passes()
    {
        var attr = new FcmsAuthorizeAttribute("users.create");
        var filter = (IAsyncAuthorizationFilter)attr.CreateInstance(
            BuildServiceProvider(permService: PermService(false))); // even if service says no

        var ctx = BuildContext(AuthenticatedUser(FcmsRoles.SuperAdmin));
        await filter.OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result); // no result set = allowed
    }

    [Fact]
    public async Task SuperAdmin_no_permission_required_passes()
    {
        var attr = new FcmsAuthorizeAttribute();
        var filter = (IAsyncAuthorizationFilter)attr.CreateInstance(
            BuildServiceProvider(permService: null));

        var ctx = BuildContext(AuthenticatedUser(FcmsRoles.SuperAdmin));
        await filter.OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result);
    }

    // ── Authenticated non-SuperAdmin ──────────────────────────────────────────

    [Fact]
    public async Task Authenticated_no_permission_required_passes()
    {
        var attr = new FcmsAuthorizeAttribute(); // no permission key
        var filter = (IAsyncAuthorizationFilter)attr.CreateInstance(
            BuildServiceProvider(permService: null));

        var ctx = BuildContext(AuthenticatedUser("Editor"));
        await filter.OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task Authenticated_with_permission_passes()
    {
        var attr = new FcmsAuthorizeAttribute("users.create");
        var filter = (IAsyncAuthorizationFilter)attr.CreateInstance(
            BuildServiceProvider(permService: PermService(true)));

        var ctx = BuildContext(AuthenticatedUser("Editor"));
        await filter.OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task Authenticated_without_permission_browser_returns_forbid()
    {
        var attr = new FcmsAuthorizeAttribute("users.create");
        var filter = (IAsyncAuthorizationFilter)attr.CreateInstance(
            BuildServiceProvider(permService: PermService(false)));

        var ctx = BuildContext(AuthenticatedUser("Editor"));
        await filter.OnAuthorizationAsync(ctx);

        Assert.IsType<ForbidResult>(ctx.Result);
    }

    [Fact]
    public async Task Authenticated_without_permission_ajax_returns_403_json()
    {
        var attr = new FcmsAuthorizeAttribute("users.create");
        var filter = (IAsyncAuthorizationFilter)attr.CreateInstance(
            BuildServiceProvider(permService: PermService(false)));

        var ctx = BuildContext(AuthenticatedUser("Editor"), isAjax: true);
        await filter.OnAuthorizationAsync(ctx);

        var result = Assert.IsType<JsonResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    // ── SuperAdmin normalized name fallback (regression: ffa759e) ────────────
    // MongoDB stored role names in uppercase ("SUPERADMIN") due to NormalizedName
    // being used instead of Name. Filter must accept both casings.

    [Fact]
    public async Task SuperAdmin_uppercase_role_claim_also_passes()
    {
        var attr = new FcmsAuthorizeAttribute("users.create");
        var filter = (IAsyncAuthorizationFilter)attr.CreateInstance(
            BuildServiceProvider(permService: PermService(false)));

        // Simulate MongoDB storing "SUPERADMIN" instead of "SuperAdmin"
        var ctx = BuildContext(AuthenticatedUser(FcmsRoles.SuperAdmin.ToUpperInvariant()));
        await filter.OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result); // must still be allowed
    }

    // ── Helper: build minimal IServiceProvider ────────────────────────────────

    private static IServiceProvider BuildServiceProvider(IPermissionService? permService)
    {
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IPermissionService)).Returns(permService);
        return sp;
    }
}
