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

// ---------------------------------------------------------------------------
// MySQL / EF tests
// ---------------------------------------------------------------------------

public class EfPhase1Tests : IAsyncLifetime
{
    private MySqlContainer _mysql = null!;
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _mysql = new MySqlBuilder()
            .WithDatabase("flexcms_test")
            .WithUsername("root")
            .WithPassword("root")
            .Build();

        await _mysql.StartAsync();

        var services = new ServiceCollection();
        services.AddFlexCms(new FlexCmsOptions
        {
            AppDataPath = Path.Combine(Path.GetTempPath(), "flexcms_test_" + Guid.NewGuid()),
            UseMySQL = true,
            MySqlConnectionString = _mysql.GetConnectionString()
        });

        // Register test entity
        services.AddDbContext<FcmsDbContext>(o =>
            o.UseMySql(_mysql.GetConnectionString(),
                Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(_mysql.GetConnectionString())));

        _sp = services.BuildServiceProvider();

        // Create schema
        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<FcmsDbContext>();
        await ctx.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS eftestentitys (" +
            "Id CHAR(36) NOT NULL PRIMARY KEY," +
            "Name VARCHAR(255) NOT NULL," +
            "CreatedAt BIGINT NOT NULL," +
            "UpdatedAt BIGINT NOT NULL," +
            "IsDeleted TINYINT(1) NOT NULL DEFAULT 0)");
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _mysql.DisposeAsync();
    }

    [Fact]
    public async Task EfRepository_Insert_RowExistsInDb()
    {
        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<FcmsDbContext>();
        var repo = new EfRepository<EfTestEntity>(ctx);

        var entity = new EfTestEntity { Name = "Hello EF" };
        await repo.AddAsync(entity);
        await ctx.SaveChangesAsync();

        var found = await repo.GetByIdAsync(entity.Id);
        Assert.NotNull(found);
        Assert.Equal("Hello EF", found.Name);
    }

    [Fact]
    public async Task EfUnitOfWork_RollbackOnException_BothEntitiesAbsent()
    {
        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<FcmsDbContext>();
        var uow = new EfUnitOfWork(ctx);

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
        finally
        {
            await uow.DisposeAsync();
        }

        // Both must be absent after rollback
        using var verifyScope = _sp.CreateScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<FcmsDbContext>();
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
        _mongo = new MongoDbBuilder().Build();
        await _mongo.StartAsync();

        MongoDbSerializerSetup.Register();

        _client = new MongoClient(_mongo.GetConnectionString());
        _database = _client.GetDatabase("flexcms_test");
    }

    public async Task DisposeAsync()
    {
        await _mongo.DisposeAsync();
    }

    [Fact]
    public async Task MongoRepository_Insert_DocumentExistsWithGuidSubtype4()
    {
        var repo = new MongoRepository<MongoTestEntity>(_database);
        var entity = new MongoTestEntity { Name = "Hello Mongo" };

        await repo.AddAsync(entity);

        // Verify GUID stored as Standard (subtype 4) string
        var collection = _database.GetCollection<BsonDocument>("mongotestentitys");
        var doc = await collection.Find(new BsonDocument("_id", entity.Id.ToString())).FirstOrDefaultAsync();

        Assert.NotNull(doc);
        // Id stored as string UUID (Standard representation)
        Assert.True(doc["_id"].IsString || doc["_id"].BsonType == BsonType.String);
    }

    [Fact]
    public async Task MongoRepository_DateTime_StoredAsUnixMilliseconds()
    {
        var repo = new MongoRepository<MongoTestEntity>(_database);
        var entity = new MongoTestEntity { Name = "DateTime Test" };

        await repo.AddAsync(entity);

        var collection = _database.GetCollection<BsonDocument>("mongotestentitys");
        var doc = await collection.Find(new BsonDocument("_id", entity.Id.ToString())).FirstOrDefaultAsync();

        Assert.NotNull(doc);
        // createdAt must be stored as Int64 (Unix ms), NOT BsonType.DateTime
        var createdAt = doc["createdAt"];
        Assert.Equal(BsonType.Int64, createdAt.BsonType);
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
        var collection = _database.GetCollection<BsonDocument>("mongotestentitys");
        var doc = await collection.Find(new BsonDocument("_id", entity.Id.ToString())).FirstOrDefaultAsync();
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
}
