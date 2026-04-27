using FlexCms.Framework.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Db.Ef;

public class FcmsDbContext : IdentityDbContext<FcmsUser, FcmsRole, Guid>
{
    public FcmsDbContext(DbContextOptions<FcmsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FcmsUser>().ToTable("fcmsusers");
        modelBuilder.Entity<FcmsRole>().ToTable("fcmsroles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("fcmsuserroles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("fcmsuserclaims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("fcmsuserlogins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("fcmsroleclaims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("fcmsusertokens");

        // Roles list is embedded in Mongo only; ignore in EF
        modelBuilder.Entity<FcmsUser>().Ignore(u => u.Roles);

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
