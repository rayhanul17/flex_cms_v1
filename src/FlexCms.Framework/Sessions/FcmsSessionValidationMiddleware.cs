using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace FlexCms.Framework.Sessions;

/// <summary>
/// Per-request enforcement of <see cref="ISessionService.IsValidAsync"/>. If
/// the principal carries a session id (claim <c>fcms.session_id</c> set by the
/// login flow) AND <see cref="SessionService.GetActiveAsync"/> reports that
/// id as revoked, the cookie is signed out and the request is treated as
/// anonymous from here on. The next interactive page render bounces back to
/// the login form.
///
/// <para>
/// <b>Where to put it</b>: after <c>UseAuthentication()</c>, before
/// <c>UseAuthorization()</c>. Skipped entirely for unauthenticated
/// requests + for requests with no session-id claim (Bearer API token, OAuth
/// callback in flight, etc.) so token-based auth keeps working.
/// </para>
/// </summary>
public sealed class FcmsSessionValidationMiddleware
{
    public const string SessionIdClaim = "fcms.session_id";

    private readonly RequestDelegate _next;

    public FcmsSessionValidationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ISessionService sessions)
    {
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var sessionId = user.FindFirstValue(SessionIdClaim);
            if (!string.IsNullOrEmpty(sessionId))
            {
                var ok = await sessions.IsValidAsync(sessionId, ctx.RequestAborted);
                if (!ok)
                {
                    // Force-logout: drop the cookie + clear the principal so
                    // downstream auth/authorization treats this as anonymous.
                    await ctx.SignOutAsync(IdentityConstants.ApplicationScheme);
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
                }
                else
                {
                    // Best-effort touch — don't fail the request if the DB write blips.
                    try { await sessions.TouchAsync(sessionId, ctx.RequestAborted); }
                    catch { /* swallowed — touch is non-critical */ }
                }
            }
        }

        await _next(ctx);
    }
}
