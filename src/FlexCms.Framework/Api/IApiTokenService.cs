namespace FlexCms.Framework.Api;

public sealed record ApiTokenIssued(FcmsApiToken Token, string PlaintextToken);

public interface IApiTokenService
{
    /// <summary>Issue a new token. Returns the persisted row plus the plaintext (shown to the user once).</summary>
    Task<ApiTokenIssued> IssueAsync(Guid userId, string name, string scopes, DateTime? expiresAt = null, CancellationToken ct = default);

    /// <summary>Validate a Bearer token. Returns the active token row + bumps LastUsedAt; null if missing/revoked/expired.</summary>
    Task<FcmsApiToken?> ValidateAsync(string plaintextToken, CancellationToken ct = default);

    Task<List<FcmsApiToken>> GetUserTokensAsync(Guid userId, CancellationToken ct = default);

    Task RevokeAsync(Guid tokenId, Guid? actingUserId, CancellationToken ct = default);
}
