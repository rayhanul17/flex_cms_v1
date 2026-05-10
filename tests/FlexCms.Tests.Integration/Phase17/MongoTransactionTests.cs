using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Helpers;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace FlexCms.Tests.Integration.Phase17;

/// <summary>
/// Verifies that <see cref="MongoUnitOfWork"/> commits and rolls back
/// transactions correctly against a real replica-set deployment (the dev
/// docker-compose <c>mongodb</c> container).
///
/// <para>
/// These tests are <b>skipped</b> when no replica-set Mongo is reachable on
/// localhost:27017 with the dev credentials — that way CI without docker
/// (or a contributor on a fresh laptop) still gets a green build, but anyone
/// running <c>docker compose up -d</c> sees the live verification.
/// </para>
/// </summary>
public class MongoTransactionTests : IAsyncLifetime
{
    // dev docker-compose: replica set rs0, root creds dev/Dev@123456
    private const string DevConnString =
        "mongodb://dev:Dev%40123456@localhost:27017/?authSource=admin&directConnection=true";

    private MongoClient _client = null!;
    private IMongoDatabase _db = null!;
    private string _dbName = null!;
    private bool _available;

    public async Task InitializeAsync()
    {
        MongoDbSerializerSetup.Register();

        var settings = MongoClientSettings.FromConnectionString(DevConnString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);
        _client = new MongoClient(settings);
        _dbName = "flexcms_tx_test_" + Guid.NewGuid().ToString("N")[..8];
        _db = _client.GetDatabase(_dbName);

        // Probe — if dev Mongo is not running, mark as unavailable and skip tests.
        try
        {
            await _db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            _available = true;
        }
        catch
        {
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_available)
        {
            try { await _client.DropDatabaseAsync(_dbName); } catch { }
        }
    }

    [Fact]
    public async Task Mongo_CommitTransaction_ChangesPersist()
    {
        if (!_available) return; // Dev Mongo not reachable — skip silently.

        await using var uow = new MongoUnitOfWork(_client, _db);
        var repo = uow.Repository<TxTestEntity>();

        var id = Guid.NewGuid();
        await uow.BeginTransactionAsync();
        try
        {
            await repo.AddAsync(new TxTestEntity { Id = id, Name = "Committed" });
            await uow.CommitAsync();
        }
        catch
        {
            await uow.RollbackAsync();
            throw;
        }

        // Verify with a fresh repo (new session) — committed data must be visible.
        var verifyRepo = new MongoRepository<TxTestEntity>(_db);
        var found = await verifyRepo.GetByIdAsync(id);
        Assert.NotNull(found);
        Assert.Equal("Committed", found.Name);
    }

    [Fact]
    public async Task Mongo_RollbackTransaction_ChangesAbsent()
    {
        if (!_available) return; // Dev Mongo not reachable — skip silently.

        await using var uow = new MongoUnitOfWork(_client, _db);
        var repo = uow.Repository<TxTestEntity>();

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await uow.BeginTransactionAsync();
        try
        {
            await repo.AddAsync(new TxTestEntity { Id = id1, Name = "First" });
            await repo.AddAsync(new TxTestEntity { Id = id2, Name = "Second" });
            throw new InvalidOperationException("simulated failure");
        }
        catch
        {
            await uow.RollbackAsync();
        }

        // Both must be absent — rollback discarded the writes.
        var verifyRepo = new MongoRepository<TxTestEntity>(_db);
        Assert.Null(await verifyRepo.GetByIdAsync(id1));
        Assert.Null(await verifyRepo.GetByIdAsync(id2));
    }

    [FcmsTable("tx_test_entities")]
    public class TxTestEntity : BaseMongoEntity
    {
        public string Name { get; set; } = "";
    }
}
