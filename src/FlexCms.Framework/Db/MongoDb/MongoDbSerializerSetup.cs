using Microsoft.AspNetCore.Identity;
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

            // Map BaseEfEntity Id to BsonId since it doesn't have the [BsonId] attribute
            if (!BsonClassMap.IsClassMapRegistered(typeof(Db.Ef.BaseEfEntity)))
            {
                BsonClassMap.RegisterClassMap<Db.Ef.BaseEfEntity>(cm =>
                {
                    cm.AutoMap();
                    cm.MapIdMember(c => c.Id);
                });
            }

            // Map Identity base classes to ensure Id is handled correctly in inheritance
            if (!BsonClassMap.IsClassMapRegistered(typeof(IdentityUser<Guid>)))
            {
                BsonClassMap.RegisterClassMap<IdentityUser<Guid>>(cm =>
                {
                    cm.AutoMap();
                    cm.MapIdMember(c => c.Id);
                });
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(IdentityRole<Guid>)))
            {
                BsonClassMap.RegisterClassMap<IdentityRole<Guid>>(cm =>
                {
                    cm.AutoMap();
                    cm.MapIdMember(c => c.Id);
                });
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(Auth.FcmsUser)))
            {
                BsonClassMap.RegisterClassMap<Auth.FcmsUser>(cm =>
                {
                    cm.AutoMap();
                });
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(Auth.FcmsRole)))
            {
                BsonClassMap.RegisterClassMap<Auth.FcmsRole>(cm =>
                {
                    cm.AutoMap();
                });
            }

            _registered = true;
        }
    }
}
