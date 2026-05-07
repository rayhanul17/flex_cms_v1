namespace FlexCms.Framework.Gdpr;

/// <summary>
/// GDPR / data-protection helpers (Phase 15 — Issue 100):
/// <list type="bullet">
///   <item><b>Right of access</b> (<see cref="ExportUserDataAsync"/>): bundles every entity owned by a user into a downloadable JSON.</item>
///   <item><b>Right to be forgotten</b> (<see cref="DeleteAccountAsync"/>): soft-deletes the user, anonymizes PII columns, and revokes all sessions/tokens.</item>
///   <item><b>Cookie consent</b> + <b>terms version</b> tracking are handled in cookie + claim shape (see helpers below).</item>
/// </list>
///
/// <para>
/// Soft-delete + anonymize (rather than hard-delete) keeps referential
/// integrity — comments + posts authored by the user retain their
/// <c>AuthorId</c> but the user record itself shows "Deleted user" in
/// every UI. Hard-deleting would orphan rows and break reports.
/// </para>
/// </summary>
public interface IFcmsGdprService
{
    /// <summary>
    /// Build a single-file JSON dump of the user's data (profile + posts +
    /// comments + sessions + login history + custom fields they've set).
    /// Returns the raw bytes; controllers wrap in a <c>File()</c> response.
    /// </summary>
    Task<byte[]> ExportUserDataAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Anonymize + soft-delete the user. Email becomes
    /// <c>deleted-{userId}@example.invalid</c>, name becomes "Deleted user",
    /// password is invalidated, all sessions revoked. Owned content stays
    /// (unless <paramref name="deleteOwnedContent"/> = true, which soft-
    /// deletes their pages/posts/comments too).
    /// </summary>
    Task<DeleteAccountResult> DeleteAccountAsync(Guid userId, bool deleteOwnedContent, CancellationToken ct = default);
}

public sealed record DeleteAccountResult(bool Success, int SessionsRevoked, int ContentItemsDeleted, string? Error = null);
