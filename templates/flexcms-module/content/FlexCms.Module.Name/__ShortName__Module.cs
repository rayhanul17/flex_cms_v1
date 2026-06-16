using FlexCms.Framework.Models;
using FlexCms.Framework.Modules;
using FlexCms.Module.Name.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Module.Name;

/// <summary>
/// Module entry point. Inherits no-op lifecycle defaults from <see cref="BaseModule"/>
/// and overrides only what the module needs.
///
/// <para>
/// Activation order on every host startup (see ModuleActivationService):
/// 1. <see cref="RegisterServices"/> — DI registration
/// 2. <see cref="CreateMigrationContext"/> → <c>Database.MigrateAsync()</c>
/// 3. Permissions seeded from <see cref="GetPermissions"/>
/// 4. Menu items seeded from <see cref="GetMenuItems"/>
/// 5. <see cref="SeedDataAsync"/> — first activation only
/// 6. <see cref="OnUpgradeAsync"/> — when version changes
/// </para>
/// </summary>
public class __ShortName__Module : BaseModule
{
    /// <summary>Compile-time constant for callers (controllers, services) that need to pass this module id to LogAsync etc.</summary>
    public const string ModuleIdValue = "FlexCms.Module.Name";

    public override string ModuleId    => ModuleIdValue;
    public override string ModuleName  => "__ShortName__";
    public override string Version     => "1.0.0";
    public override string TablePrefix => "mod_prefix";

    public override void RegisterServices(IServiceCollection services)
    {
        // Anything marked [FcmsScoped] / [FcmsSingleton] / [FcmsHostedService] in this
        // assembly is auto-registered by AttributeScanner — no manual line needed.
        // Use this hook for typed HttpClients, options binding, or third-party libraries.
    }

    public override DbContext? CreateMigrationContext(string connectionString, string provider)
    {
        var builder = new DbContextOptionsBuilder<__ShortName__DbContext>();
        switch (provider)
        {
            case "mysql":
                builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                break;
            case "mssql":
                builder.UseSqlServer(connectionString);
                break;
            case "postgresql":
                builder.UseNpgsql(connectionString);
                break;
        }
        return new __ShortName__DbContext(builder.Options);
    }

    public override async Task SeedDataAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        // Module DbContexts aren't auto-registered in host DI — construct the
        // same context the framework used to run migrations, so the seed lives
        // on the exact schema we just applied. Idempotent: only runs when
        // FcmsModuleRecord.SeedCompleted is false.
        var opts = sp.GetRequiredService<ModuleActivationOptions>();
        var ctx = CreateMigrationContext(opts.ConnectionString, opts.Provider) as __ShortName__DbContext;
        if (ctx is null) return;
        await using (ctx)
        {
            // Don't crash on first run if the developer hasn't generated migrations yet —
            // surface a helpful message in the logs and skip. After `dotnet ef migrations
            // add InitialSchema` + restart, this branch is skipped and the real seed runs.
            if (!await ctx.Database.CanConnectAsync(ct)) return;
            var tables = await ctx.Database.GetAppliedMigrationsAsync(ct);
            if (!tables.Any())
            {
                Console.WriteLine($"[{ModuleId}] No EF migrations found — run `dotnet ef migrations add InitialSchema` to enable seeding.");
                return;
            }

            if (!await ctx.Items.AnyAsync(ct))
            {
                ctx.Items.Add(new __ShortName__Item
                {
                    Title = "Welcome",
                    Description = "Edit or delete this sample row."
                });
                await ctx.SaveChangesAsync(ct);
            }
        }
    }

    public override async Task OnUpgradeAsync(string fromVersion, IServiceProvider sp, CancellationToken ct = default)
    {
        // Called once when the module record's Version differs from the manifest version.
        // Use for data backfills tied to a specific upgrade path.
        await Task.CompletedTask;
    }

    public override async Task DropTablesAsync(string connectionString, string provider, CancellationToken ct = default)
    {
        var ctx = CreateMigrationContext(connectionString, provider);
        if (ctx is null) return;
        await using (ctx)
            await ctx.Database.EnsureDeletedAsync(ct);
    }

    public override List<FcmsMenuItemDef> GetMenuItems() =>
    [
        new FcmsMenuItemDef
        {
            DefaultName = "__ShortName__",
            Icon = "bi bi-box",
            Url = "/mod_prefix/admin",
            Order = 500,
            RequiredPermission = __ShortName__Permissions.View
        }
    ];

    public override List<FcmsPermissionDef> GetPermissions() =>
    [
        new(__ShortName__Permissions.ViewKey,   "View __ShortName__ items",   "__ShortName__"),
        new(__ShortName__Permissions.CreateKey, "Create __ShortName__ items", "__ShortName__"),
        new(__ShortName__Permissions.EditKey,   "Edit __ShortName__ items",   "__ShortName__"),
        new(__ShortName__Permissions.DeleteKey, "Delete __ShortName__ items", "__ShortName__"),
    ];
}

/// <summary>
/// Permission key constants. ModuleActivationService prefixes each
/// <see cref="FcmsPermissionDef.Key"/> with <c>{ModuleId}.</c> (lowercase) on seed,
/// so the keys stored in fcms_permissions and checked at runtime end up as
/// <c>flexcms.module.name.__shortname__.view</c> etc.
/// </summary>
public static class __ShortName__Permissions
{
    public const string ViewKey   = "__shortname__.view";
    public const string CreateKey = "__shortname__.create";
    public const string EditKey   = "__shortname__.edit";
    public const string DeleteKey = "__shortname__.delete";

    public const string View   = "flexcms.module.name." + ViewKey;
    public const string Create = "flexcms.module.name." + CreateKey;
    public const string Edit   = "flexcms.module.name." + EditKey;
    public const string Delete = "flexcms.module.name." + DeleteKey;
}
