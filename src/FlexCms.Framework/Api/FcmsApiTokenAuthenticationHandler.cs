using System.Security.Claims;
using System.Text.Encodings.Web;
using FlexCms.Framework.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlexCms.Framework.Api;

public sealed class FcmsApiTokenAuthenticationOptions : AuthenticationSchemeOptions { }

/// <summary>
/// AuthN handler that resolves <c>Authorization: Bearer fcms_...</c> headers
/// against <see cref="IApiTokenService"/>. On success, issues a
/// <see cref="ClaimsPrincipal"/> populated with:
/// <list type="bullet">
///   <item><c>NameIdentifier</c> = the underlying user id.</item>
///   <item><c>Name</c> = the user's UserName.</item>
///   <item>Role claims for every role the underlying user holds.</item>
///   <item><c>fcms.api_token_id</c> = the token row id.</item>
///   <item><c>fcms.api_scope</c> = each scope granted (one claim per scope).</item>
/// </list>
/// Existing <c>[Authorize]</c> + <c>[FcmsAuthorize]</c> attributes work
/// unchanged — token-bearing requests look like a normal logged-in user
/// for downstream code.
/// </summary>
public sealed class FcmsApiTokenAuthenticationHandler : AuthenticationHandler<FcmsApiTokenAuthenticationOptions>
{
    public const string SchemeName = "FcmsApiToken";

    private readonly IApiTokenService _tokens;
    private readonly UserManager<FcmsUser> _users;

    public FcmsApiTokenAuthenticationHandler(
        IOptionsMonitor<FcmsApiTokenAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiTokenService tokens,
        UserManager<FcmsUser> users)
        : base(options, logger, encoder)
    {
        _tokens = tokens;
        _users = users;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var raw = auth["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(raw)) return AuthenticateResult.NoResult();

        var token = await _tokens.ValidateAsync(raw, Context.RequestAborted);
        if (token is null) return AuthenticateResult.Fail("Invalid or expired API token.");

        var user = await _users.FindByIdAsync(token.UserId.ToString());
        if (user is null) return AuthenticateResult.Fail("Token owner no longer exists.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? ""),
            new("fcms.api_token_id", token.Id.ToString())
        };
        foreach (var scope in (token.Scopes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new Claim("fcms.api_scope", scope));

        // Pull the user's role list so existing [Authorize(Roles=...)] checks work.
        var roles = await _users.GetRolesAsync(user);
        foreach (var role in roles) claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
