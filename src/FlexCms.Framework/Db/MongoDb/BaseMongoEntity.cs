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
    public bool IsDeleted { get; set; }
}
