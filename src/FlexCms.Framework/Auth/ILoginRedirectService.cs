using System.Security.Claims;

namespace FlexCms.Framework.Auth;

/// <summary>
/// Resolves "where should this user land after a successful login?". Priority
/// order (first non-empty / safe wins):
/// <list type="number">
///   <item>Caller-supplied <paramref name="returnUrl"/> if local + non-empty.</item>
///   <item>User's per-account <c>CustomLandingPage</c> claim/profile field.</item>
///   <item>Per-role <c>DefaultLandingPage</c> from <c>SiteSettings.DefaultRoleLandingPagesJson</c>
///         (precedence: SuperAdmin > Admin > Editor > Author > Subscriber > others).</item>
///   <item><c>SiteSettings.FallbackLandingPage</c> (default <c>/</c>).</item>
/// </list>
/// External / off-host returnUrls are blocked to prevent open-redirect.
/// </summary>
public interface ILoginRedirectService
{
    Task<string> ResolveAsync(ClaimsPrincipal user, string? returnUrl, Func<string, bool> isLocalUrl, CancellationToken ct = default);
}
