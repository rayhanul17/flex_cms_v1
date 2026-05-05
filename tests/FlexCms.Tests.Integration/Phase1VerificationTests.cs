using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Extensions;
using FlexCms.Framework.Setup;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Testcontainers.MySql;
using Xunit;

namespace FlexCms.Tests.Integration;

// -- Test entities ----------------------------------------------------------

public class EfTestEntity : BaseEfEntity
{
    public string Name { get; set; } = string.Empty;
}

public class MongoTestEntity : BaseMongoEntity
{
    public string Name { get; set; } = string.Empty;
}

// Test-specific DbContext that knows about EfTestEntity
public class TestDbContext : FcmsDbContext
{
    public TestDbContext(DbContextOptions<FcmsDbContext> options) : base(options) { }
    public DbSet<EfTestEntity> EfTestEntities => Set<EfTestEntity>();
}

// ---------------------------------------------------------------------------
// MySQL / EF tests
// ---------------------------------------------------------------------------

public class EfPhase1Tests : IAsyncLifetime
{
    private MySqlContainer _mysql = null!;
    private TestDbContext _ctx = null!;

    public async Task InitializeAsync()
    {
        _mysql = new MySqlBuilder("mysql:8.4")
            .WithDatabase("flexcms_test")
            .WithUsername("root")
            .WithPassword("root")
            .Build();

        await _mysql.StartAsync();

        var connStr = _mysql.GetConnectionString();
        var options = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseMySql(connStr, Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(connStr),
                o => { o.EnableRetryOnFailure(3); o.CommandTimeout(30); })
            .Options;

        _ctx = new TestDbContext(options);
        await _ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _mysql.DisposeAsync();
    }

    [Fact]
    public async Task EfRepository_Insert_RowExistsInDb()
    {
        var repo = new EfRepository<EfTestEntity>(_ctx);

        var entity = new EfTestEntity { Name = "Hello EF" };
        await repo.AddAsync(entity);
        await _ctx.SaveChangesAsync();

        var found = await repo.GetByIdAsync(entity.Id);
        Assert.NotNull(found);
        Assert.Equal("Hello EF", found.Name);
    }

    [Fact]
    public async Task EfUnitOfWork_RollbackOnException_BothEntitiesAbsent()
    {
        await using var uow = new EfUnitOfWork(_ctx);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await uow.BeginTransactionAsync();
        try
        {
            var repo = uow.Repository<EfTestEntity>();
            await repo.AddAsync(new EfTestEntity { Id = id1, Name = "First" });
            await repo.AddAsync(new EfTestEntity { Id = id2, Name = "Second" });
            await uow.SaveChangesAsync();

            throw new InvalidOperationException("Simulated failure after insert");
        }
        catch
        {
            await uow.RollbackAsync();
        }

        // Both must be absent after rollback — fresh context to bypass EF identity cache
        var connStr = _mysql.GetConnectionString();
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseMySql(connStr, Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(connStr))
            .Options;
        await using var verifyCtx = new TestDbContext(opts);
        var repo2 = new EfRepository<EfTestEntity>(verifyCtx);
        Assert.Null(await repo2.GetByIdAsync(id1));
        Assert.Null(await repo2.GetByIdAsync(id2));
    }
}

// ---------------------------------------------------------------------------
// MongoDB tests
// ---------------------------------------------------------------------------

public class MongoPhase1Tests : IAsyncLifetime
{
    private MongoDbContainer _mongo = null!;
    private IMongoDatabase _database = null!;
    private MongoClient _client = null!;

    public async Task InitializeAsync()
    {
        _mongo = new MongoDbBuilder("mongo:7").Build();
        await _mongo.StartAsync();

        MongoDbSerializerSetup.Register();

        _client = new MongoClient(_mongo.GetConnectionString());
        _database = _client.GetDatabase("flexcms_test");
    }

    public async Task DisposeAsync()
    {
        await _mongo.DisposeAsync();
    }

    // Helper: binary UUID filter for raw BsonDocument queries
    private static FilterDefinition<BsonDocument> IdFilter(Guid id)
    {
        var bytes = GuidToStandardBytes(id);
        var bsonId = new BsonBinaryData(bytes, BsonBinarySubType.UuidStandard);
        return Builders<BsonDocument>.Filter.Eq("_id", bsonId);
    }

    // GuidRepresentation.Standard uses RFC 4122 byte order (no shuffling)
    private static byte[] GuidToStandardBytes(Guid id) => id.ToByteArray(bigEndian: true);

    [Fact]
    public async Task MongoRepository_Insert_DocumentExistsWithGuidSubtype4()
    {
        var repo = new MongoRepository<MongoTestEntity>(_database);
        var entity = new MongoTestEntity { Name = "Hello Mongo" };

        await repo.AddAsync(entity);

        // Verify GUID stored as binary UUID subtype 4 (Standard)
        var collName = FlexCms.Framework.Helpers.FcmsHelper.GetTableName<MongoTestEntity>("fcms");
        var collection = _database.GetCollection<BsonDocument>(collName);
        var doc = await collection.Find(IdFilter(entity.Id)).FirstOrDefaultAsync();

        Assert.NotNull(doc);
        Assert.Equal(BsonType.Binary, doc["_id"].BsonType);
        Assert.Equal(BsonBinarySubType.UuidStandard, doc["_id"].AsBsonBinaryData.SubType);
    }

    [Fact]
    public async Task MongoRepository_DateTime_StoredAsUnixMilliseconds()
    {
        var repo = new MongoRepository<MongoTestEntity>(_database);
        var entity = new MongoTestEntity { Name = "DateTime Test" };

        await repo.AddAsync(entity);

        var collection = _database.GetCollection<BsonDocument>(FlexCms.Framework.Helpers.FcmsHelper.GetTableName<MongoTestEntity>("fcms"));
        var doc = await collection.Find(IdFilter(entity.Id)).FirstOrDefaultAsync();

        Assert.NotNull(doc);
        // createdAt must be stored as Int64 (Unix ms), NOT BsonType.DateTime
        Assert.Equal(BsonType.Int64, doc["createdAt"].BsonType);
    }

    [Fact]
    public async Task MongoRepository_DateTime_StoredAsUtcReadBackAsUtc()
    {
        var repo = new MongoRepository<MongoTestEntity>(_database);

        var beforeUtc = DateTime.UtcNow;
        var entity = new MongoTestEntity { Name = "UTC Roundtrip" };
        await repo.AddAsync(entity);
        var afterUtc = DateTime.UtcNow;

        // 1. Raw BSON: Int64 Unix ms (UTC-epoch based)
        var collection = _database.GetCollection<BsonDocument>(FlexCms.Framework.Helpers.FcmsHelper.GetTableName<MongoTestEntity>("fcms"));
        var doc = await collection.Find(IdFilter(entity.Id)).FirstOrDefaultAsync();
        Assert.NotNull(doc);
        Assert.Equal(BsonType.Int64, doc["createdAt"].BsonType);

        var storedUtc = DateTimeOffset.FromUnixTimeMilliseconds(doc["createdAt"].AsInt64).UtcDateTime;
        Assert.InRange(storedUtc, beforeUtc.AddSeconds(-1), afterUtc.AddSeconds(1));

        // 2. Read back via repository: Kind must be Utc, ticks must match stored value
        var found = await repo.GetByIdAsync(entity.Id);
        Assert.NotNull(found);
        Assert.Equal(DateTimeKind.Utc, found.CreatedAt.Kind);
        Assert.Equal(storedUtc.Ticks, found.CreatedAt.Ticks);
    }

    [Fact]
    public async Task MongoRepository_SoftDelete_NotReturnedByGetAll()
    {
        var repo = new MongoRepository<MongoTestEntity>(_database);
        var entity = new MongoTestEntity { Name = "To Delete" };
        await repo.AddAsync(entity);

        await repo.SoftDeleteAsync(entity);

        var all = await repo.GetAllAsync();
        Assert.DoesNotContain(all, e => e.Id == entity.Id);

        // But document still physically exists in collection
        var collection = _database.GetCollection<BsonDocument>(FlexCms.Framework.Helpers.FcmsHelper.GetTableName<MongoTestEntity>("fcms"));
        var doc = await collection.Find(IdFilter(entity.Id)).FirstOrDefaultAsync();
        Assert.NotNull(doc);
        Assert.True(doc["isDeleted"].AsBoolean);
    }
}

// ---------------------------------------------------------------------------
// Setup.json roundtrip test
// ---------------------------------------------------------------------------

public class SetupHelperTests
{
    [Fact]
    public void SetupHelper_WriteRead_RoundtripWithEncryptedPassword()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "flexcms_setup_test_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(tempDir, "keys")))
            .SetApplicationName("FlexCms");
        var sp = services.BuildServiceProvider();

        var helper = new SetupHelper(sp.GetRequiredService<IDataProtectionProvider>(), tempDir);

        var config = new SetupConfig
        {
            IsSetupComplete = true,
            DbProvider = "mysql",
            DbConnectionString = "Server=localhost;Database=flexcms;User=root;",
            DbPasswordEncrypted = "MySecretPassword",
            SiteName = "Test Site",
            AdminEmail = "admin@test.com",
            SetupVersion = "1.0"
        };

        helper.Write(config);

        var loaded = helper.Read();
        Assert.NotNull(loaded);
        Assert.True(loaded.IsSetupComplete);
        Assert.Equal("Test Site", loaded.SiteName);
        Assert.Equal("admin@test.com", loaded.AdminEmail);

        // Password must be encrypted in file
        Assert.NotEqual("MySecretPassword", loaded.DbPasswordEncrypted);
        Assert.StartsWith("CfDJ8", loaded.DbPasswordEncrypted);

        // Decrypt should return original
        var decrypted = helper.DecryptPassword(loaded.DbPasswordEncrypted);
        Assert.Equal("MySecretPassword", decrypted);

        // Cleanup
        Directory.Delete(tempDir, recursive: true);
        sp.Dispose();
    }

    // Regression: SetupHelper.IsSetupComplete (static) used to look for a camelCase
    // property name while JsonSerializer wrote it as PascalCase, so it always
    // returned false — making the app loop back to setup mode on every restart.
    [Fact]
    public void IsSetupComplete_static_returns_true_after_Write_with_complete_flag()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "flexcms_iscomplete_test_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(tempDir, "keys")))
            .SetApplicationName("FlexCms");
        var sp = services.BuildServiceProvider();

        var helper = new SetupHelper(sp.GetRequiredService<IDataProtectionProvider>(), tempDir);
        helper.Write(new SetupConfig
        {
            IsSetupComplete = true,
            DbProvider = "mysql",
            AdminEmail = "admin@test.com"
        });

        Assert.True(SetupHelper.IsSetupComplete(tempDir));

        Directory.Delete(tempDir, recursive: true);
        sp.Dispose();
    }

    [Fact]
    public void IsSetupComplete_static_returns_false_when_file_missing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "flexcms_iscomplete_missing_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        Assert.False(SetupHelper.IsSetupComplete(tempDir));

        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public void IsSetupComplete_static_returns_false_when_flag_false()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "flexcms_iscomplete_false_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(tempDir, "keys")))
            .SetApplicationName("FlexCms");
        var sp = services.BuildServiceProvider();

        var helper = new SetupHelper(sp.GetRequiredService<IDataProtectionProvider>(), tempDir);
        helper.Write(new SetupConfig { IsSetupComplete = false });

        Assert.False(SetupHelper.IsSetupComplete(tempDir));

        Directory.Delete(tempDir, recursive: true);
        sp.Dispose();
    }
}
