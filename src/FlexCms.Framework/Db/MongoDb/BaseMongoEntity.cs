using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCms.Framework.Db.MongoDb;

public abstract class BaseMongoEntity : IBaseEntity
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    // Stored as Unix milliseconds via FcmsDateTimeSerializer
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
