using FlexCms.Framework.Models;
using FlexCms.Framework.Modules;
using FlexCms.Sample.Hello.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Sample.Hello;

/// <summary>
/// Sample module — demonstrates the full FlexCms module pattern end-to-end:
/// entity + EF migrations, permissions, menu items, admin CRUD controller,
/// public JSON endpoint, and the seed / upgrade / drop lifecycle hooks.
/// Copy this layout when starting a new module.
/// </summary>
public class HelloModule : BaseModule
{
    public override string ModuleId => "FlexCms.Sample.Hello";
    public override string ModuleName => "Hello";
    public override string Version => "1.0.0";
    public override string TablePrefix => "hello";

    // Attribute-marked services ([FcmsScoped]) are auto-registered by
    // AttributeScanner — keep this empty unless you need typed HttpClients,
    // options binding, or library setup that the attributes can't express.
    public override void RegisterServices(IServiceCollection services) { }

    public override DbContext? CreateMigrationContext(string connectionString, string provider)
    {
        var builder = new DbContextOptionsBuilder<HelloDbContext>();
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
        return new HelloDbContext(builder.Options);
    }

    public override async Task SeedDataAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        // Module DbContexts aren't auto-registered in host DI — construct the
        // same context the framework used to run migrations, so the seed lives
        // on the exact schema we just applied.
        var opts = sp.GetRequiredService<FlexCms.Framework.Modules.ModuleActivationOptions>();
        var ctx = CreateMigrationContext(opts.ConnectionString, opts.Provider) as HelloDbContext;
        if (ctx is null) return;
        await using (ctx)
        {
            if (!await ctx.Greetings.AnyAsync(ct))
            {
                ctx.Greetings.Add(new HelloGreeting { Audience = "world", Message = "Hello, world!" });
                await ctx.SaveChangesAsync(ct);
            }
        }
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
            DefaultName = "Hello",
            Icon = "bi bi-emoji-smile",
            Url = "/admin/hello",
            Order = 900,
            RequiredPermission = HelloPermissions.View
        }
    ];

    public override List<FcmsPermissionDef> GetPermissions() =>
    [
        new(HelloPermissions.ViewKey,   "View Hello greetings",   "Hello"),
        new(HelloPermissions.CreateKey, "Create Hello greetings", "Hello"),
        new(HelloPermissions.EditKey,   "Edit Hello greetings",   "Hello"),
        new(HelloPermissions.DeleteKey, "Delete Hello greetings", "Hello"),
    ];
}

public static class HelloPermissions
{
    public const string ViewKey   = "greeting.view";
    public const string CreateKey = "greeting.create";
    public const string EditKey   = "greeting.edit";
    public const string DeleteKey = "greeting.delete";

    // Fully-qualified keys — must mirror the {ModuleId}. prefix that
    // ModulePermissionSeeder writes to fcms_permissions (lowercased).
    public const string View   = "flexcms.sample.hello." + ViewKey;
    public const string Create = "flexcms.sample.hello." + CreateKey;
    public const string Edit   = "flexcms.sample.hello." + EditKey;
    public const string Delete = "flexcms.sample.hello." + DeleteKey;
}
