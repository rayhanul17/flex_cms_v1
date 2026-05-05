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
    /// Stored as <see cref="MongoDB.Bson.BsonType.Int32"/> via
    /// <c>MongoDbSerializerSetup.Register()</c>.
    /// </summary>
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public DateTime? DeletedAt { get; set; }
}
