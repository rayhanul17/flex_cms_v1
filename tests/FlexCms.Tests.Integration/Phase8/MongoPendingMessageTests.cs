using FlexCms.Framework.Db;
using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Helpers;
using FlexCms.Framework.Messaging;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace FlexCms.Tests.Integration.Phase8;

/// <summary>
/// Phase 8 — restart-safe pending-message persistence works end-to-end against
/// a real MongoDB container: insert, query by status, and round-trip the
/// retry-count + last-error fields.
/// </summary>
public class MongoPendingMessageTests : IAsyncLifetime
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
        _db = client.GetDatabase("flexcms_phase8_test");
    }

    public async Task DisposeAsync() => await _mongo.DisposeAsync();

    private MongoRepository<FcmsPendingMessage> Repo() => new(_db);

    [Fact]
    public async Task Add_persists_document_with_typed_fields()
    {
        var repo = Repo();
        var msg = new FcmsPendingMessage
        {
            Channel = MessageChannel.Email,
            To = "x@y.z",
            Subject = "Hi",
            Body = "<p>Body</p>",
            IsHtml = true,
            BroadcastId = Guid.NewGuid()
        };

        await repo.AddAsync(msg);

        var coll = _db.GetCollection<BsonDocument>(FcmsHelper.GetTableName<FcmsPendingMessage>("fcms"));
        var bytes = msg.Id.ToByteArray(bigEndian: true);
        var doc = await coll.Find(Builders<BsonDocument>.Filter.Eq(
            "_id", new BsonBinaryData(bytes, BsonBinarySubType.UuidStandard))).FirstOrDefaultAsync();

        Assert.NotNull(doc);
        Assert.Equal("x@y.z", doc["to"].AsString);
        Assert.Equal("Hi", doc["subject"].AsString);
        Assert.True(doc["isHtml"].AsBoolean);
        // DeliveryStatus + Channel use the global enum-as-string convention since they're not [BsonRepresentation Int32]'d.
        // Both representations should round-trip via the typed repo regardless.
    }

    [Fact]
    public async Task Find_filters_by_DeliveryStatus_and_RetryCount()
    {
        var repo = Repo();
        await repo.AddAsync(new FcmsPendingMessage { Channel = MessageChannel.Email, To = "a@b.c", Body = "1", DeliveryStatus = MessageDeliveryStatus.Pending, RetryCount = 0 });
        await repo.AddAsync(new FcmsPendingMessage { Channel = MessageChannel.Email, To = "d@e.f", Body = "2", DeliveryStatus = MessageDeliveryStatus.Sent, RetryCount = 1 });
        await repo.AddAsync(new FcmsPendingMessage { Channel = MessageChannel.Email, To = "g@h.i", Body = "3", DeliveryStatus = MessageDeliveryStatus.Failed, RetryCount = 1 });

        var pendingOrRetriable = await repo.FindAsync(
            m => m.DeliveryStatus == MessageDeliveryStatus.Pending
                 || (m.DeliveryStatus == MessageDeliveryStatus.Failed && m.RetryCount < 3));

        Assert.Equal(2, pendingOrRetriable.Count);
        Assert.Contains(pendingOrRetriable, m => m.To == "a@b.c");
        Assert.Contains(pendingOrRetriable, m => m.To == "g@h.i");
    }

    [Fact]
    public async Task Update_round_trips_LastError_and_RetryCount()
    {
        var repo = Repo();
        var msg = new FcmsPendingMessage { Channel = MessageChannel.Sms, To = "01700000000", Body = "x" };
        await repo.AddAsync(msg);

        msg.RetryCount = 2;
        msg.LastError = "rate limited";
        msg.DeliveryStatus = MessageDeliveryStatus.Pending;
        await repo.UpdateAsync(msg);

        var loaded = await repo.GetByIdAsync(msg.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.RetryCount);
        Assert.Equal("rate limited", loaded.LastError);
        Assert.Equal(MessageDeliveryStatus.Pending, loaded.DeliveryStatus);
    }
}
