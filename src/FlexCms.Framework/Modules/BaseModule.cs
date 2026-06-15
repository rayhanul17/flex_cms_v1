using FlexCms.Framework.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Convenience base class — modules typically inherit from this rather than
/// implement <see cref="IFcmsModule"/> directly. All lifecycle hooks have
/// no-op defaults; override only what the module needs.
/// </summary>
public abstract class BaseModule : IFcmsModule
{
    public abstract string ModuleId { get; }
    public abstract string ModuleName { get; }
    public abstract string Version { get; }
    public abstract string TablePrefix { get; }

    public virtual void RegisterServices(IServiceCollection services) { }

    /// <inheritdoc/>
    public virtual DbContext? CreateMigrationContext(string connectionString, string provider) => null;

    /// <inheritdoc/>
    public virtual Task SeedDataAsync(IServiceProvider sp, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public virtual Task OnUpgradeAsync(string fromVersion, IServiceProvider sp, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public virtual Task DropTablesAsync(string connectionString, string provider, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public virtual List<FcmsMenuItemDef> GetMenuItems() => [];

    /// <inheritdoc/>
    public virtual List<FcmsPermissionDef> GetPermissions() => [];
}
