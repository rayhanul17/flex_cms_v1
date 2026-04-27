using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Db.Ef;

public class FcmsDbContext : DbContext
{
    public FcmsDbContext(DbContextOptions<FcmsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply global soft-delete query filter for all BaseEfEntity types
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEfEntity).IsAssignableFrom(entityType.ClrType)) continue;

            var method = typeof(FcmsDbContext)
                .GetMethod(nameof(ApplySoftDeleteFilter),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);

            method.Invoke(null, [modelBuilder]);
        }
    }

    private static void ApplySoftDeleteFilter<T>(ModelBuilder builder) where T : BaseEfEntity
    {
        builder.Entity<T>().HasQueryFilter(e => !e.IsDeleted);
        // Plural snake_case table naming
        builder.Entity<T>().ToTable(typeof(T).Name.ToLowerInvariant() + "s");
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEfEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return await base.SaveChangesAsync(ct);
    }
}
