namespace FlexCms.Framework.Auth;

/// <summary>
/// FlexCMS-specific claim type names used outside the standard
/// <see cref="System.Security.Claims.ClaimTypes"/> set.
/// </summary>
public static class FcmsClaimTypes
{
    /// <summary>
    /// Carries the <c>FcmsApiToken.Id</c> when an HTTP request authenticated
    /// via Bearer token (instead of the Identity cookie). Presence of this
    /// claim is the signal that <see cref="Services.PermissionService"/>
    /// uses to require scope intersection on top of role permissions.
    /// </summary>
    public const string ApiTokenId = "fcms.api_token_id";

    /// <summary>
    /// One claim per scope granted to an API token. Scopes are exact
    /// permission keys (e.g. <c>pages.edit</c>). The token may only
    /// authorise permission expressions admitted by BOTH the underlying
    /// user's roles AND the scope set on this token.
    /// </summary>
    public const string ApiScope = "fcms.api_scope";
}
