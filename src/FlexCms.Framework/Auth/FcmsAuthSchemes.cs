namespace FlexCms.Framework.Auth;

/// <summary>
/// Authentication scheme names used by FlexCMS. Centralised here so
/// controllers, middleware, and DI registration don't pass magic
/// strings around.
/// </summary>
public static class FcmsAuthSchemes
{
    /// <summary>
    /// Default scheme registered in DI. Forwards Bearer requests to
    /// <see cref="Api.FcmsApiTokenAuthenticationHandler.SchemeName"/>
    /// and everything else to the Identity application-cookie scheme.
    /// </summary>
    public const string Smart = "FcmsSmart";
}
