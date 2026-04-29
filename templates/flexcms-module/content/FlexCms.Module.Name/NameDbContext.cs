using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Helpers;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Module.Name;

/// <summary>
/// Module-owned DbContext. Only contains this module's entities.
/// FlexCms calls MigrateAsync() on this context at startup via CreateMigrationContext().
/// </summary>
public class NameDbContext : DbContext
{
    public const string Prefix = "mod_prefix";

    public NameDbContext(DbContextOptions<NameDbContext> options) : base(options) { }

    // Add your DbSets here:
    // public DbSet<NamePost> Posts => Set<NamePost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Auto-name tables using FlexCms convention: {prefix}_{entity_snake_plural}
        // Example: FcmsHelper.GetEntityName<NamePost>(Prefix) → "mod_prefix_name_posts"
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEfEntity).IsAssignableFrom(entityType.ClrType)) continue;
            var method = typeof(NameDbContext)
                .GetMethod(nameof(ApplyNaming),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);
            method.Invoke(null, [modelBuilder]);
        }
    }

    private static void ApplyNaming<T>(ModelBuilder builder) where T : BaseEfEntity
        => builder.Entity<T>().ToTable(FcmsHelper.GetEntityName<T>(Prefix));
}
