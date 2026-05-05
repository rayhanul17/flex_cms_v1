using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCms.Framework.Db.MongoDb;

public abstract class BaseMongoEntity : IBaseEntity
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();

    // Stored as Unix milliseconds via FcmsDateTimeSerializer
    public DateTime CreatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;
    public DateTime UpdatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Stored as <see cref="BsonType.Int32"/> in MongoDB (overrides the global
    /// enum-as-string convention from <c>MongoDbSerializerSetup</c>). Stable
    /// across enum renames + smaller payload + matches int-based queries.
    /// </summary>
    [BsonRepresentation(BsonType.Int32)]
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public DateTime? DeletedAt { get; set; }
}
