using System.Security.Claims;
using System.Text.Json;
using FlexCms.Framework.Services;

namespace FlexCms.Framework.Auth;

public sealed class LoginRedirectService : ILoginRedirectService
{
    private readonly ISettingsService _settings;

    /// <summary>Roles in fallback precedence order — first match wins.</summary>
    private static readonly string[] RolePrecedence =
        [FcmsRoles.SuperAdmin, "Admin", "Editor", "Author", "Subscriber"];

    public LoginRedirectService(ISettingsService settings) => _settings = settings;

    public async Task<string> ResolveAsync(ClaimsPrincipal user, string? returnUrl, Func<string, bool> isLocalUrl, CancellationToken ct = default)
    {
        // 1. Caller-supplied returnUrl wins, but ONLY if it's local.
        if (!string.IsNullOrWhiteSpace(returnUrl) && isLocalUrl(returnUrl))
            return returnUrl;

        // 2. Per-user override claim.
        var custom = user?.FindFirstValue("fcms.landing_page");
        if (!string.IsNullOrWhiteSpace(custom) && isLocalUrl(custom))
            return custom;

        var snap = await SafeGetAsync(ct);

        // 3. Role map — pick the highest-precedence role the user holds that has a mapping.
        if (user is not null && !string.IsNullOrWhiteSpace(snap.DefaultRoleLandingPagesJson))
        {
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(snap.DefaultRoleLandingPagesJson)
                          ?? new();
                foreach (var role in RolePrecedence)
                {
                    if (user.IsInRole(role) && map.TryGetValue(role, out var path) && !string.IsNullOrWhiteSpace(path) && isLocalUrl(path))
                        return path;
                }
                // Fall through to check non-precedence roles too — first hit wins.
                foreach (var (role, path) in map)
                {
                    if (user.IsInRole(role) && !string.IsNullOrWhiteSpace(path) && isLocalUrl(path))
                        return path;
                }
            }
            catch (JsonException) { /* malformed JSON → fall through */ }
        }

        // 4. SiteSettings.FallbackLandingPage; "/" if empty.
        var fallback = string.IsNullOrWhiteSpace(snap.FallbackLandingPage) ? "/" : snap.FallbackLandingPage;
        return isLocalUrl(fallback) ? fallback : "/";
    }

    private async Task<LoginRedirectSnapshot> SafeGetAsync(CancellationToken ct)
    {
        try { return await _settings.GetAsync<LoginRedirectSnapshot>("site:general", ct); }
        catch { return new LoginRedirectSnapshot(); }
    }

    /// <summary>Slice of SiteSettings the redirect resolver cares about — keeps Framework off Core.</summary>
    public sealed class LoginRedirectSnapshot
    {
        public string DefaultRoleLandingPagesJson { get; set; } =
            """{"SuperAdmin":"/admin","Admin":"/admin","Editor":"/admin/cms/posts","Author":"/admin/cms/posts/mine","Subscriber":"/profile"}""";
        public string FallbackLandingPage { get; set; } = "/";
    }
}
