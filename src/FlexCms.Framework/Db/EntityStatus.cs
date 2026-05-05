namespace FlexCms.Framework.Db;

/// <summary>
/// Lifecycle status for every <see cref="IBaseEntity"/>. Replaces the old
/// boolean <c>IsDeleted</c> flag with an explicit three-state model.
/// </summary>
/// <remarks>
/// Values are stable integers chosen to be visually self-documenting
/// (e.g. <c>404</c> for Deleted mirrors the HTTP "not found" intuition).
/// Stored as <c>int</c> in EF columns and as <c>Int32</c> in MongoDB
/// (registered via <c>MongoDbSerializerSetup</c>).
/// </remarks>
public enum EntityStatus
{
    InActive = 0,
    Active = 1,
    Deleted = 404
}
