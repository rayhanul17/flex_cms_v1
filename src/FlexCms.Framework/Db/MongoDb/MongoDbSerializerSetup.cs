using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace FlexCms.Framework.Db.MongoDb;

public static class MongoDbSerializerSetup
{
    private static bool _registered;
    private static readonly Lock _lock = new();

    public static void Register()
    {
        lock (_lock)
        {
            if (_registered) return;

            // GUID as standard string (subtype 4 — "GUID")
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            // DateTime as Unix milliseconds Int64 (NOT BSON Date)
            BsonSerializer.RegisterSerializer(typeof(DateTime), FcmsDateTimeSerializer.Instance);

            // Camel-case element names, ignore extra elements
            var pack = new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true),
                new EnumRepresentationConvention(BsonType.String)
            };
            ConventionRegistry.Register("FcmsConventions", pack, _ => true);

            _registered = true;
        }
    }
}
