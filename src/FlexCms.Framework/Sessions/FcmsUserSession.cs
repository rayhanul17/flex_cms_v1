using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Sessions;

/// <summary>
/// One row per active sign-in cookie. Lets us list "Active sessions"
/// in the user profile + force-logout from another device + report on
/// concurrent-session anomalies.
///
/// <para>
/// Entry created on successful login by <see cref="SessionService.RecordLoginAsync"/>;
/// flipped to <see cref="IsRevoked"/> on logout or admin revoke;
/// <see cref="FcmsSessionValidationMiddleware"/> blocks any request whose session
/// id matches a revoked row.
/// </para>
///
/// <para>
/// Decorated with <see cref="FcmsAuditIgnoreEntityAttribute"/> because
/// <c>FcmsSessionValidationMiddleware</c> updates <see cref="LastSeenAt"/> on
/// every authenticated request. Letting <c>FcmsAuditInterceptor</c> emit an
/// audit row for each update would flood <c>fcms_logs</c> within a single
/// admin browsing session and drown out the real audit events (page edits,
/// permission changes, module activations). Domain-level session events —
/// login, logout, force-revoke — go through <c>IFcmsLogService</c> directly
/// from <c>SessionService</c> with named actions like <c>"Session.Login"</c>.
/// </para>
/// </summary>
[FcmsAuditIgnoreEntity]
public class FcmsUserSession : BaseEfEntity
{
    public Guid UserId { get; set; }

    /// <summary>Stable session id stored in the cookie's claim payload + back here.</summary>
    public string SessionId { get; set; } = "";

    public string IpAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";

    /// <summary>Best-effort browser/OS extracted via UAParser at login time.</summary>
    public string DeviceLabel { get; set; } = "";

    public DateTime LastSeenAt { get; set; }

    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevokeReason { get; set; }
}
