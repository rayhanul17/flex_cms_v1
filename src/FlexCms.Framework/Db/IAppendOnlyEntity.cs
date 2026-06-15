namespace FlexCms.Framework.Db;

/// <summary>
/// Marker interface for entities that are append-only (audit logs):
/// <list type="bullet">
///   <item>Never updated, never soft-deleted.</item>
///   <item>EF strips inherited <c>Status</c>/<c>DeletedAt</c>/<c>UpdatedAt</c>
///         columns and skips the <c>HasQueryFilter</c> for them.</item>
/// </list>
/// </summary>
public interface IAppendOnlyEntity { }
