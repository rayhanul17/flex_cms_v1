using FlexCms.Framework.Auth;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using FlexCms.Framework.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Db.Ef;

public class FcmsDbContext : IdentityDbContext<FcmsUser, FcmsRole, Guid>
{
    /// <summary>Framework table prefix — applied to every Core/Identity table.</summary>
    public const string FrameworkPrefix = "fcms";

    public FcmsDbContext(DbContextOptions<FcmsDbContext> options) : base(options) { }

    public DbSet<FcmsSettings> Settings => Set<FcmsSettings>();
    public DbSet<FcmsPermission> Permissions => Set<FcmsPermission>();
    public DbSet<FcmsRolePermission> RolePermissions => Set<FcmsRolePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Identity tables — generic type names don't snake_case nicely, so set
        // them explicitly. Convention: fcms_ + plural_snake_case_noun.
        modelBuilder.Entity<FcmsUser>().ToTable("fcms_users");
        modelBuilder.Entity<FcmsRole>().ToTable("fcms_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("fcms_user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("fcms_user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("fcms_user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("fcms_role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("fcms_user_tokens");

        // Roles list is embedded in Mongo only; ignore in EF
        modelBuilder.Entity<FcmsUser>().Ignore(u => u.Roles);

        // Unique index: one permission key per role
        modelBuilder.Entity<FcmsRolePermission>()
            .HasIndex(rp => new { rp.RoleId, rp.PermissionKey })
            .IsUnique();

        // FK: deleting a role hard-removes its permission rows. Soft-delete
        // (IsDeleted=true) is a column update and does NOT trigger this cascade.
        modelBuilder.Entity<FcmsRolePermission>()
            .HasOne<FcmsRole>()
            .WithMany()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique index: permission key must be unique globally
        modelBuilder.Entity<FcmsPermission>()
            .HasIndex(p => p.Key)
            .IsUnique();

        // Apply soft-delete filter + auto-name table for every BaseEfEntity.
        // Module entities will follow the same convention with their own prefix
        // once the module loader (Phase 4 sub-PR 2) is in place.
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
        builder.Entity<T>().ToTable(FcmsHelper.GetEntityName<T>(FrameworkPrefix));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEfEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = FcmsTime.Now;
        }
        return await base.SaveChangesAsync(ct);
    }
}
