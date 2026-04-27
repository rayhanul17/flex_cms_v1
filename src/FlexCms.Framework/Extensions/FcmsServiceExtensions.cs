using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Db.Migration;
using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Setup;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace FlexCms.Framework.Extensions;

public static class FcmsServiceExtensions
{
    public static IServiceCollection AddFlexCms(
        this IServiceCollection services,
        FlexCmsOptions options)
    {
        // DataProtection — keyring persisted to App_Data/keys
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.AppDataPath, "keys")))
            .SetApplicationName("FlexCms");

        // Migration coordinator (NoOp for single-instance)
        services.AddSingleton<IFcmsMigrationCoordinator, NoOpMigrationCoordinator>();

        // Setup helper
        services.AddScoped<SetupHelper>(sp =>
        {
            var dp = sp.GetRequiredService<IDataProtectionProvider>();
            return new SetupHelper(dp, options.AppDataPath);
        });

        // Register DB provider based on options
        if (options.UseMySQL)
        {
            services.AddDbContext<FcmsDbContext>(o =>
                o.UseMySql(
                    options.MySqlConnectionString,
                    Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(options.MySqlConnectionString),
                    m =>
                    {
                        m.EnableRetryOnFailure(3);
                        m.CommandTimeout(30);
                    }));

            services.AddScoped<DbContext>(sp => sp.GetRequiredService<FcmsDbContext>());
            services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
            services.AddScoped<IFcmsUnitOfWork, EfUnitOfWork>();
        }

        if (options.UseMongoDB)
        {
            MongoDbSerializerSetup.Register();

            services.AddSingleton<IMongoClient>(_ => new MongoClient(options.MongoConnectionString));
            services.AddSingleton<IMongoDatabase>(sp =>
            {
                var client = sp.GetRequiredService<IMongoClient>();
                return client.GetDatabase(options.MongoDatabaseName);
            });

            if (!options.UseMySQL)
            {
                // MongoDB-only mode: register Mongo repositories as default
                services.AddScoped(typeof(IRepository<>), typeof(MongoRepository<>));
                services.AddScoped<IFcmsUnitOfWork>(sp =>
                    new MongoUnitOfWork(
                        sp.GetRequiredService<IMongoClient>(),
                        sp.GetRequiredService<IMongoDatabase>()));
            }
        }

        return services;
    }
}

public class FlexCmsOptions
{
    public string AppDataPath { get; set; } = "App_Data";
    public bool UseMySQL { get; set; }
    public string MySqlConnectionString { get; set; } = string.Empty;
    public bool UseMongoDB { get; set; }
    public string MongoConnectionString { get; set; } = "mongodb://localhost:27017";
    public string MongoDatabaseName { get; set; } = "flexcms";
}
