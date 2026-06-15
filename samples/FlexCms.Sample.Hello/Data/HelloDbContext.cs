using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Helpers;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Sample.Hello.Data;

/// <summary>
/// Module-owned DbContext. Only contains this module's entities.
/// FlexCms calls <c>Database.MigrateAsync()</c> on this context at startup via
/// <c>CreateMigrationContext()</c>.
/// </summary>
public class HelloDbContext : DbContext
{
    public const string Prefix = "hello";

    public HelloDbContext(DbContextOptions<HelloDbContext> options) : base(options) { }

    public DbSet<HelloGreeting> Greetings => Set<HelloGreeting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEfEntity).IsAssignableFrom(entityType.ClrType)) continue;
            var method = typeof(HelloDbContext)
                .GetMethod(nameof(ApplyNaming),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);
            method.Invoke(null, [modelBuilder]);
        }
    }

    private static void ApplyNaming<T>(ModelBuilder builder) where T : BaseEfEntity
        => builder.Entity<T>().ToTable(FcmsHelper.GetTableName<T>(Prefix));
}

public class HelloGreeting : BaseEfEntity
{
    public string Audience { get; set; } = "";
    public string Message { get; set; } = "";
}
