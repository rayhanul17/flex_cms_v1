using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace FlexCms.Framework.Db.MongoDb;

/// <summary>
/// Stores DateTime as Unix milliseconds (Int64) in MongoDB — NOT BSON Date type.
/// This ensures consistent cross-platform timestamp handling.
/// </summary>
public sealed class FcmsDateTimeSerializer : SerializerBase<DateTime>
{
    public static readonly FcmsDateTimeSerializer Instance = new();

    public override DateTime Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var type = context.Reader.GetCurrentBsonType();
        return type switch
        {
            BsonType.Int64 => DateTimeOffset.FromUnixTimeMilliseconds(context.Reader.ReadInt64()).UtcDateTime,
            BsonType.DateTime => BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(context.Reader.ReadDateTime()),
            _ => throw new BsonSerializationException($"Cannot deserialize DateTime from BsonType {type}")
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateTime value)
    {
        var ms = new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();
        context.Writer.WriteInt64(ms);
    }
}
