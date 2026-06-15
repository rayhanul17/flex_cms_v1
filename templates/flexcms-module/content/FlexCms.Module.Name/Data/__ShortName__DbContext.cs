using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Helpers;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Module.Name.Data;

/// <summary>
/// Module-owned DbContext. Only contains this module's entities.
/// FlexCms calls <c>Database.MigrateAsync()</c> on this context at startup via
/// <c>CreateMigrationContext()</c>.
/// </summary>
public class __ShortName__DbContext : DbContext
{
    public const string Prefix = "mod_prefix";

    public __ShortName__DbContext(DbContextOptions<__ShortName__DbContext> options) : base(options) { }

    public DbSet<__ShortName__Item> Items => Set<__ShortName__Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Auto-name tables using the FlexCms convention: {prefix}_{entity_snake_plural}
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEfEntity).IsAssignableFrom(entityType.ClrType)) continue;
            var method = typeof(__ShortName__DbContext)
                .GetMethod(nameof(ApplyNaming),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);
            method.Invoke(null, [modelBuilder]);
        }
    }

    private static void ApplyNaming<T>(ModelBuilder builder) where T : BaseEfEntity
        => builder.Entity<T>().ToTable(FcmsHelper.GetTableName<T>(Prefix));
}

public class __ShortName__Item : BaseEfEntity
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsPublished { get; set; }
}
