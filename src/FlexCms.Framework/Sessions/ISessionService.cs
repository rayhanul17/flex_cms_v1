namespace FlexCms.Framework.Sessions;

public interface ISessionService
{
    Task<FcmsUserSession> RecordLoginAsync(Guid userId, string sessionId, string ip, string userAgent, string deviceLabel, CancellationToken ct = default);

    Task<List<FcmsUserSession>> GetActiveAsync(Guid userId, CancellationToken ct = default);

    Task TouchAsync(string sessionId, CancellationToken ct = default);

    /// <summary>True if the session is recorded AND not revoked. Used by the validation middleware on every request.</summary>
    Task<bool> IsValidAsync(string sessionId, CancellationToken ct = default);

    Task RevokeAsync(string sessionId, Guid? revokedByUserId, string? reason, CancellationToken ct = default);

    /// <summary>Revoke every active session for this user (e.g. password change → log out everywhere).</summary>
    Task<int> RevokeAllForUserAsync(Guid userId, Guid? revokedByUserId, string? reason, CancellationToken ct = default);
}
