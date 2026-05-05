using FlexCms.Framework.Clock;
using FlexCms.Framework.Db.Ef;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Runs once at production startup. For every active (non-deactivated) module:
/// <list type="number">
///   <item>Calls <see cref="IFcmsModule.CreateMigrationContext"/> and runs EF migrations.</item>
///   <item>Calls <see cref="IFcmsModule.SeedDataAsync"/> when <c>FcmsModuleRecord.SeedCompleted</c> is false, then marks it done.</item>
/// </list>
/// Idempotent — safe to run on every restart.
/// </summary>
public class ModuleActivationService : IHostedService
{
    private readonly ModuleRegistry _registry;
    private readonly ModuleActivationOptions _opts;
    private readonly ModuleStateService _state;
    private readonly IWebHostEnvironment _env;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ModuleActivationService> _logger;

    public ModuleActivationService(
        ModuleRegistry registry,
        ModuleActivationOptions opts,
        ModuleStateService state,
        IWebHostEnvironment env,
        IServiceScopeFactory scopes,
        ILogger<ModuleActivationService> logger)
    {
        _registry = registry;
        _opts = opts;
        _state = state;
        _env = env;
        _scopes = scopes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var activeModules = _registry.Modules.Where(m => !m.IsDeactivated).ToList();
        if (activeModules.Count == 0) return;

        await using var scope = _scopes.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetService<Db.IRepository<FcmsModuleRecord>>();
        if (repo is null) return;
        var uow = scope.ServiceProvider.GetRequiredService<Db.IFcmsUnitOfWork>();

        foreach (var loaded in activeModules)
        {
            var module = loaded.Instance;

            // ── 0. Sync wwwroot static assets ─────────────────────────────────
            _state.SyncWwwroot(loaded.FolderPath, _env.WebRootPath, module.ModuleId);

            // ── 1. Run EF migrations ──────────────────────────────────────────
            var migrationCtx = module.CreateMigrationContext(_opts.ConnectionString, _opts.Provider);
            if (migrationCtx is not null)
            {
                try
                {
                    await migrationCtx.Database.MigrateAsync(ct);
                    _logger.LogInformation("Module {Id}: migrations applied.", module.ModuleId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Module {Id}: migration failed.", module.ModuleId);
                }
                finally
                {
                    await migrationCtx.DisposeAsync();
                }
            }

            // ── 2. Seed data (first activation only) ─────────────────────────
            var record = await repo.FirstOrDefaultAsync(r => r.ModuleId == module.ModuleId, ct);

            if (record is null)
            {
                record = new FcmsModuleRecord
                {
                    ModuleId = module.ModuleId,
                    Version = module.Version,
                    Status = "Active",
                    ActivatedAt = FcmsTime.Now
                };
                await repo.AddAsync(record, ct);
                await uow.SaveChangesAsync(ct);
            }

            if (!record.SeedCompleted)
            {
                try
                {
                    await module.SeedDataAsync(scope.ServiceProvider, ct);
                    record.SeedCompleted = true;
                    record.Version = module.Version;
                    await repo.UpdateAsync(record, ct);
                    await uow.SaveChangesAsync(ct);
                    _logger.LogInformation("Module {Id}: seed completed.", module.ModuleId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Module {Id}: seed failed.", module.ModuleId);
                }
            }

            // ── 3. OnUpgrade — version changed since last run ─────────────────
            if (record.SeedCompleted && record.Version != module.Version)
            {
                try
                {
                    var fromVersion = record.Version;
                    await module.OnUpgradeAsync(fromVersion, scope.ServiceProvider, ct);
                    record.Version = module.Version;
                    await repo.UpdateAsync(record, ct);
                    await uow.SaveChangesAsync(ct);
                    _logger.LogInformation("Module {Id}: upgraded {From} → {To}.",
                        module.ModuleId, fromVersion, module.Version);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Module {Id}: upgrade failed.", module.ModuleId);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Holds DB connection info needed by <see cref="ModuleActivationService"/>.</summary>
public class ModuleActivationOptions
{
    public string ConnectionString { get; init; } = "";
    public string Provider { get; init; } = "mysql";
}
