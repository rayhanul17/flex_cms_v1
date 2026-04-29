using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Implemented by every module DLL. The framework discovers types implementing
/// this interface during the module scan phase, instantiates them, and uses
/// the metadata to register services and (later) activate the module.
/// </summary>
public interface IFcmsModule
{
    /// <summary>
    /// Globally unique module identifier — usually the assembly name.
    /// Example: "FlexCms.Blog".
    /// </summary>
    string ModuleId { get; }

    /// <summary>Human-friendly name shown in admin UI. Example: "Blog".</summary>
    string ModuleName { get; }

    /// <summary>SemVer string. Example: "1.0.0".</summary>
    string Version { get; }

    /// <summary>
    /// Table-name prefix for this module's entities (e.g. "blog" → "blog_posts").
    /// Combined with <see cref="Helpers.FcmsHelper.GetEntityName{T}(string)"/>.
    /// </summary>
    string TablePrefix { get; }

    /// <summary>
    /// Register the module's services with the host DI container.
    /// Called by the framework once during startup, before the container is built.
    /// </summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>
    /// Return a configured <see cref="DbContext"/> that owns this module's EF
    /// migrations, or <c>null</c> if the module has no relational tables.
    /// The framework calls <c>Database.MigrateAsync()</c> on the returned context
    /// at startup (after <see cref="RegisterServices"/> runs).
    /// </summary>
    DbContext? CreateMigrationContext(string connectionString, string provider);

    /// <summary>
    /// Seed initial data after first activation. Guaranteed idempotent by
    /// convention — the framework calls this only when
    /// <c>FcmsModuleRecord.SeedCompleted == false</c>, then flips the flag.
    /// </summary>
    Task SeedDataAsync(IServiceProvider sp, CancellationToken ct = default);

    /// <summary>
    /// Drop all tables owned by this module. Called by the framework when an
    /// uninstall request includes the "drop tables" option.
    /// </summary>
    Task DropTablesAsync(string connectionString, string provider, CancellationToken ct = default);
}
