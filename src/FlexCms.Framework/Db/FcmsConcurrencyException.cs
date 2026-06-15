namespace FlexCms.Framework.Db;

/// <summary>
/// Thrown when an optimistic-concurrency token (RowVersion) check fails
/// during an update. Wrapped from EF's <c>DbUpdateConcurrencyException</c>
/// by callers that want a backend-neutral catch.
///
/// <para>
/// Editor controllers should catch this + show "Another editor saved
/// first; refresh to merge" — the Phase 15 / Issue 96 contract.
/// </para>
/// </summary>
public sealed class FcmsConcurrencyException : Exception
{
    public FcmsConcurrencyException(string message) : base(message) { }
    public FcmsConcurrencyException(string message, Exception innerException) : base(message, innerException) { }
}
