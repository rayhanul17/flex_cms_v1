using FlexCms.Framework.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Module.Name;

public class FlexCms_Module_NameModule : BaseModule
{
    public override string ModuleId    => "FlexCms.Module.Name";
    public override string ModuleName  => "Name";
    public override string Version     => "1.0.0";
    public override string TablePrefix => "mod_prefix";

    public override void RegisterServices(IServiceCollection services)
    {
        // Register your scoped/singleton services here
    }

    public override DbContext? CreateMigrationContext(string connectionString, string provider)
    {
        // Return your module's DbContext so FlexCms runs MigrateAsync() at startup.
        // Example (MySQL):
        //   var opts = new DbContextOptionsBuilder<NameDbContext>()
        //       .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
        //       .Options;
        //   return new NameDbContext(opts);
        return null;
    }

    public override async Task SeedDataAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        // Called once after first activation (SeedCompleted=false).
        // Use sp.GetRequiredService<YourDbContext>() to insert initial data.
        await Task.CompletedTask;
    }

    public override async Task OnUpgradeAsync(string fromVersion, IServiceProvider sp, CancellationToken ct = default)
    {
        // Called when module version in DB differs from Version above.
        // Apply data migrations here.
        await Task.CompletedTask;
    }

    public override async Task DropTablesAsync(string connectionString, string provider, CancellationToken ct = default)
    {
        // Called on uninstall with "Drop Tables" option.
        // Drop your module's tables here using raw SQL or EF.
        await Task.CompletedTask;
    }
}
