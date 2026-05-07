namespace FlexCms.Framework.Cms.Preview;

/// <summary>
/// Issues + validates one-shot share tokens that let an editor preview
/// (or share) an unpublished page / post without granting the recipient
/// any admin access. Token is a long URL-safe random string stored on
/// the entity; the frontend slug controller checks it before rendering
/// an unpublished item.
///
/// <para>
/// Tokens have a configurable lifetime (default 7 days). Each call to
/// <see cref="IssueAsync"/> rotates the token — sharing a new link
/// invalidates the previously-shared one.
/// </para>
/// </summary>
public interface IPreviewTokenService
{
    /// <summary>Issue (or rotate) a fresh preview token for the entity. Returns the new token string.</summary>
    Task<string> IssueAsync(string entityType, Guid entityId, TimeSpan? lifetime = null, CancellationToken ct = default);

    /// <summary>Validate a token against an entity. Returns true only if the entity has the same token AND it hasn't expired.</summary>
    Task<bool> ValidateAsync(string entityType, Guid entityId, string? token, CancellationToken ct = default);

    /// <summary>Manually revoke any active token. Idempotent.</summary>
    Task RevokeAsync(string entityType, Guid entityId, CancellationToken ct = default);
}
