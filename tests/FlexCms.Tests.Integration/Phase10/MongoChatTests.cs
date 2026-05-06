using FlexCms.Framework.Chat;
using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Helpers;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace FlexCms.Tests.Integration.Phase10;

/// <summary>
/// Verifies chat entities round-trip through Mongo with the expected typed
/// fields, and that the service can run end-to-end against the Mongo repo.
/// </summary>
public class MongoChatTests : IAsyncLifetime
{
    private MongoDbContainer _mongo = null!;
    private IMongoDatabase _db = null!;
    private MongoUnitOfWork _uow = null!;
    private MongoClient _client = null!;

    public async Task InitializeAsync()
    {
        _mongo = new MongoDbBuilder("mongo:7").Build();
        await _mongo.StartAsync();
        MongoDbSerializerSetup.Register();

        _client = new MongoClient(_mongo.GetConnectionString());
        _db = _client.GetDatabase("flexcms_phase10_test");
        _uow = new MongoUnitOfWork(_client, _db);
    }

    public async Task DisposeAsync()
    {
        await _uow.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    private ChatService Build()
        => new(new MongoRepository<FcmsChatThread>(_db),
               new MongoRepository<FcmsChatMessage>(_db),
               _uow);

    [Fact]
    public async Task GetOrCreateOpenThread_persists_thread_document()
    {
        var svc = Build();
        var u = Guid.NewGuid();

        var t = await svc.GetOrCreateOpenThreadAsync(u, "Alice");

        var coll = _db.GetCollection<BsonDocument>(FcmsHelper.GetTableName<FcmsChatThread>("fcms"));
        var bytes = t.Id.ToByteArray(bigEndian: true);
        var doc = await coll.Find(Builders<BsonDocument>.Filter.Eq(
            "_id", new BsonBinaryData(bytes, BsonBinarySubType.UuidStandard))).FirstOrDefaultAsync();

        Assert.NotNull(doc);
        Assert.Equal("Alice", doc["userDisplayName"].AsString);
    }

    [Fact]
    public async Task AddMessageAsync_persists_message_and_bumps_thread_preview()
    {
        var svc = Build();
        var u = Guid.NewGuid();
        var t = await svc.GetOrCreateOpenThreadAsync(u, "Alice");

        await svc.AddMessageAsync(new FcmsChatMessage
        {
            ThreadId = t.Id,
            SenderUserId = u,
            SenderRole = ChatSenderRole.User,
            SenderDisplayName = "Alice",
            Body = "Hello Mongo"
        });

        var reloaded = await svc.GetThreadAsync(t.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Hello Mongo", reloaded!.LastMessagePreview);
        Assert.NotNull(reloaded.LastMessageAt);

        var msgs = await svc.GetMessagesAsync(t.Id);
        Assert.Single(msgs);
        Assert.Equal("Hello Mongo", msgs[0].Body);
    }

    [Fact]
    public async Task ResolveThreadAsync_flips_status_and_records_resolver()
    {
        var svc = Build();
        var u = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var t = await svc.GetOrCreateOpenThreadAsync(u, "Alice");

        await svc.ResolveThreadAsync(t.Id, admin);

        var reloaded = await svc.GetThreadAsync(t.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(ChatThreadStatus.Resolved, reloaded!.ThreadStatus);
        Assert.Equal(admin, reloaded.ResolvedByUserId);
    }
}
