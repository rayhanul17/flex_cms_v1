using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Exports;
using FlexCms.Framework.Helpers;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace FlexCms.Tests.Integration.Phase12;

public class MongoPendingExportTests : IAsyncLifetime
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
        _db = client.GetDatabase("flexcms_phase12_test");
    }

    public async Task DisposeAsync() => await _mongo.DisposeAsync();

    [Fact]
    public async Task Add_persists_with_typed_fields()
    {
        var repo = new MongoRepository<FcmsPendingExport>(_db);
        var job = new FcmsPendingExport
        {
            HandlerId = "students.results",
            Format = ExportFormat.Excel,
            ParametersJson = "{\"termId\":\"t1\"}",
            Title = "Q4 results"
        };

        await repo.AddAsync(job);

        var coll = _db.GetCollection<BsonDocument>(FcmsHelper.GetTableName<FcmsPendingExport>("fcms"));
        var bytes = job.Id.ToByteArray(bigEndian: true);
        var doc = await coll.Find(Builders<BsonDocument>.Filter.Eq(
            "_id", new BsonBinaryData(bytes, BsonBinarySubType.UuidStandard))).FirstOrDefaultAsync();

        Assert.NotNull(doc);
        Assert.Equal("students.results", doc["handlerId"].AsString);
        Assert.Equal("Q4 results", doc["title"].AsString);
    }

    [Fact]
    public async Task Update_round_trips_terminal_state_fields()
    {
        var repo = new MongoRepository<FcmsPendingExport>(_db);
        var job = new FcmsPendingExport { HandlerId = "x", Format = ExportFormat.Pdf, Title = "x" };
        await repo.AddAsync(job);

        job.ExportStatus = ExportStatus.Done;
        job.DownloadUrl = "/exports/abc.pdf";
        job.FileSizeBytes = 12345;
        job.CompletedAt = DateTime.UtcNow;
        await repo.UpdateAsync(job);

        var loaded = await repo.GetByIdAsync(job.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ExportStatus.Done, loaded!.ExportStatus);
        Assert.Equal("/exports/abc.pdf", loaded.DownloadUrl);
        Assert.Equal(12345, loaded.FileSizeBytes);
    }
}
