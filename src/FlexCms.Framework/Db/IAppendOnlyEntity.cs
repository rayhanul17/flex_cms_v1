namespace FlexCms.Framework.Db;

/// <summary>
/// Marker interface for entities that are append-only (audit logs):
/// <list type="bullet">
///   <item>Never updated, never soft-deleted.</item>
///   <item>EF strips inherited <c>Status</c>/<c>DeletedAt</c>/<c>UpdatedAt</c>
///         columns and skips the <c>HasQueryFilter</c> for them.</item>
///   <item>Mongo's repository skips the <c>Status != Deleted</c> filter so
///         queries return ALL rows (matching EF semantics).</item>
/// </list>
///
/// <para>
/// Without this interface, an append-only entity stored in Mongo with any
/// <c>Status</c> value would silently disappear from query results — see
/// the EF/Mongo divergence audit, B3.
/// </para>
/// </summary>
public interface IAppendOnlyEntity { }
