using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Helpers;
using FlexCms.Framework.Notifications;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace FlexCms.Tests.Integration.Phase9;

/// <summary>
/// Phase 9 — notifications persist correctly through the Mongo repo: insert,
/// query by user, query by unread+user, update flips IsRead atomically.
/// </summary>
public class MongoNotificationTests : IAsyncLifetime
{
    private MongoDbContainer _mongo = null!;
    private IMongoDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _mongo = new MongoDbBuilder("mongo:7").Build();
        await _mongo.StartAsync();
        MongoDbSerializerSetup.Register();
#pragma warning disable CA2000
        var client = new MongoClient(_mongo.GetConnectionString());
#pragma warning restore CA2000
        _db = client.GetDatabase("flexcms_phase9_test");
    }

    public async Task DisposeAsync() => await _mongo.DisposeAsync();

    private MongoRepository<FcmsNotification> Repo() => new(_db);

    [Fact]
    public async Task Add_persists_with_typed_fields()
    {
        var repo = Repo();
        var userId = Guid.NewGuid();
        var n = new FcmsNotification
        {
            UserId = userId,
            Title = "Hi",
            Body = "Body",
            Level = NotificationLevel.Warning,
            Url = "/admin",
            Icon = "bi bi-bell"
        };

        await repo.AddAsync(n);

        var coll = _db.GetCollection<BsonDocument>(FcmsHelper.GetTableName<FcmsNotification>("fcms"));
        var bytes = n.Id.ToByteArray(bigEndian: true);
        var doc = await coll.Find(Builders<BsonDocument>.Filter.Eq(
            "_id", new BsonBinaryData(bytes, BsonBinarySubType.UuidStandard))).FirstOrDefaultAsync();

        Assert.NotNull(doc);
        Assert.Equal("Hi", doc["title"].AsString);
        Assert.Equal("/admin", doc["url"].AsString);
        Assert.False(doc["isRead"].AsBoolean);
    }

    [Fact]
    public async Task Find_by_user_and_unread_returns_only_owner_unread_rows()
    {
        var repo = Repo();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await repo.AddAsync(new FcmsNotification { UserId = alice, Title = "a1" });
        await repo.AddAsync(new FcmsNotification { UserId = alice, Title = "a2", IsRead = true });
        await repo.AddAsync(new FcmsNotification { UserId = bob, Title = "b1" });

        var aliceUnread = await repo.FindAsync(n => n.UserId == alice && !n.IsRead);

        Assert.Single(aliceUnread);
        Assert.Equal("a1", aliceUnread[0].Title);
    }

    [Fact]
    public async Task Update_flips_IsRead_to_true_with_ReadAt()
    {
        var repo = Repo();
        var n = new FcmsNotification { UserId = Guid.NewGuid(), Title = "x" };
        await repo.AddAsync(n);

        n.IsRead = true;
        n.ReadAt = DateTime.UtcNow;
        await repo.UpdateAsync(n);

        var loaded = await repo.GetByIdAsync(n.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsRead);
        Assert.NotNull(loaded.ReadAt);
    }
}
