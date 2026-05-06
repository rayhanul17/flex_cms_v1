using System.Security.Claims;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Services;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase13;

/// <summary>
/// Verifies the resolution priority: returnUrl → user claim → role map →
/// fallback. Open-redirect blocking, role precedence, malformed JSON
/// recovery, and missing-settings fallback are all covered.
/// </summary>
public class LoginRedirectServiceTests
{
    private static ISettingsService SettingsWith(string? roleMapJson = null, string? fallback = null)
    {
        var snap = new LoginRedirectService.LoginRedirectSnapshot
        {
            DefaultRoleLandingPagesJson = roleMapJson ?? """{"SuperAdmin":"/admin","Editor":"/admin/cms/posts","Subscriber":"/profile"}""",
            FallbackLandingPage = fallback ?? "/"
        };
        var m = Substitute.For<ISettingsService>();
        m.GetAsync<LoginRedirectService.LoginRedirectSnapshot>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(snap);
        return m;
    }

    private static ClaimsPrincipal User(string? landingPage = null, params string[] roles)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(landingPage))
            claims.Add(new Claim("fcms.landing_page", landingPage));
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));
        var identity = new ClaimsIdentity(claims, "test");
        return new ClaimsPrincipal(identity);
    }

    private static bool IsLocal(string url) => url.StartsWith('/') && !url.StartsWith("//");

    [Fact]
    public async Task ReturnUrl_wins_when_local()
    {
        var svc = new LoginRedirectService(SettingsWith());
        var result = await svc.ResolveAsync(User(roles: "Editor"), "/some/page", IsLocal);
        Assert.Equal("/some/page", result);
    }

    [Fact]
    public async Task ReturnUrl_blocked_when_external()
    {
        var svc = new LoginRedirectService(SettingsWith());
        var result = await svc.ResolveAsync(User(roles: "Editor"), "https://evil.com/x", IsLocal);
        Assert.Equal("/admin/cms/posts", result);   // falls through to role map
    }

    [Fact]
    public async Task PerUser_landing_page_wins_when_returnUrl_absent()
    {
        var svc = new LoginRedirectService(SettingsWith());
        var result = await svc.ResolveAsync(User(landingPage: "/admin/blog/drafts", "Editor"), null, IsLocal);
        Assert.Equal("/admin/blog/drafts", result);
    }

    [Fact]
    public async Task PerUser_landing_page_blocked_when_external()
    {
        var svc = new LoginRedirectService(SettingsWith());
        var result = await svc.ResolveAsync(User(landingPage: "https://evil.com/x", "Editor"), null, IsLocal);
        Assert.Equal("/admin/cms/posts", result);
    }

    [Fact]
    public async Task Role_map_resolves_to_role_specific_landing()
    {
        var svc = new LoginRedirectService(SettingsWith());
        Assert.Equal("/admin/cms/posts", await svc.ResolveAsync(User(roles: "Editor"), null, IsLocal));
        Assert.Equal("/profile", await svc.ResolveAsync(User(roles: "Subscriber"), null, IsLocal));
        Assert.Equal("/admin", await svc.ResolveAsync(User(roles: "SuperAdmin"), null, IsLocal));
    }

    [Fact]
    public async Task Multi_role_user_uses_highest_precedence()
    {
        var svc = new LoginRedirectService(SettingsWith());
        var user = User(roles: ["Editor", "Subscriber"]);
        var result = await svc.ResolveAsync(user, null, IsLocal);
        Assert.Equal("/admin/cms/posts", result);   // Editor wins over Subscriber
    }

    [Fact]
    public async Task Falls_back_to_FallbackLandingPage_when_no_role_match()
    {
        var svc = new LoginRedirectService(SettingsWith(fallback: "/landing"));
        var user = User(roles: "WeirdCustomRole");
        var result = await svc.ResolveAsync(user, null, IsLocal);
        Assert.Equal("/landing", result);
    }

    [Fact]
    public async Task Falls_back_to_root_when_fallback_is_external()
    {
        var svc = new LoginRedirectService(SettingsWith(fallback: "https://evil.com"));
        var user = User(roles: "X");
        var result = await svc.ResolveAsync(user, null, IsLocal);
        Assert.Equal("/", result);
    }

    [Fact]
    public async Task Malformed_role_map_json_falls_through_to_fallback()
    {
        var svc = new LoginRedirectService(SettingsWith(roleMapJson: "{ broken json"));
        var user = User(roles: "Editor");
        var result = await svc.ResolveAsync(user, null, IsLocal);
        Assert.Equal("/", result);
    }

    [Fact]
    public async Task Anonymous_user_falls_through_to_fallback()
    {
        var svc = new LoginRedirectService(SettingsWith(fallback: "/welcome"));
        var anon = new ClaimsPrincipal(new ClaimsIdentity());
        var result = await svc.ResolveAsync(anon, null, IsLocal);
        Assert.Equal("/welcome", result);
    }
}
