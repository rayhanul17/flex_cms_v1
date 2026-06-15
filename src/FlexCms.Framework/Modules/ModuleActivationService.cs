using FlexCms.Framework.Cms;
using FlexCms.Framework.Clock;
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
///   <item>Upserts the module's declared permissions into <c>fcms_permissions</c>.</item>
///   <item>Seeds menu items (idempotent).</item>
///   <item>Calls <see cref="IFcmsModule.SeedDataAsync"/> when <c>FcmsModuleRecord.SeedCompleted</c> is false, then marks it done.</item>
///   <item>Calls <see cref="IFcmsModule.OnUpgradeAsync"/> when the manifest version changed.</item>
/// </list>
///
/// <para>
/// Idempotent — safe to run on every restart. Any per-step failure is captured
/// on <see cref="FcmsModuleRecord.ActivationError"/> so the admin module list
/// can surface it; the rest of the pipeline keeps running so one bad module
/// doesn't block the others.
/// </para>
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
        var permissionSeeder = scope.ServiceProvider.GetRequiredService<ModulePermissionSeeder>();

        foreach (var loaded in activeModules)
        {
            var module = loaded.Instance;
            var errors = new List<string>();

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
                    errors.Add("migration: " + ex.Message);
                }
                finally
                {
                    await migrationCtx.DisposeAsync();
                }
            }

            // ── 2. Ensure a FcmsModuleRecord exists ──────────────────────────
            var record = await repo.FirstOrDefaultAsync(r => r.ModuleId == module.ModuleId, ct);
            if (record is null)
            {
                record = new FcmsModuleRecord
                {
                    ModuleId = module.ModuleId,
                    Version = module.Version,
                    ActivationStatus = "Active",
                    ActivatedAt = FcmsTime.Now
                };
                await repo.AddAsync(record, ct);
                await uow.SaveChangesAsync(ct);
            }

            // ── 3. Permissions — upsert on every restart (idempotent) ────────
            try
            {
                await permissionSeeder.SeedAsync(module, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {Id}: permission seed failed.", module.ModuleId);
                errors.Add("permissions: " + ex.Message);
            }

            // ── 4. Menu items — idempotent; restores soft-deleted on re-activate
            try
            {
                var menuItems = module.GetMenuItems();
                if (menuItems.Count > 0)
                {
                    var menuService = scope.ServiceProvider.GetService<IMenuService>();
                    if (menuService is not null)
                        await menuService.SeedAsync(module.ModuleId, menuItems, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {Id}: menu seed failed.", module.ModuleId);
                errors.Add("menu: " + ex.Message);
            }

            // ── 5. Seed data (first activation only) ─────────────────────────
            if (!record.SeedCompleted)
            {
                try
                {
                    await module.SeedDataAsync(scope.ServiceProvider, ct);
                    record.SeedCompleted = true;
                    record.Version = module.Version;
                    _logger.LogInformation("Module {Id}: seed completed.", module.ModuleId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Module {Id}: seed failed.", module.ModuleId);
                    errors.Add("seed: " + ex.Message);
                }
            }

            // ── 6. OnUpgrade — version changed since last successful seed ────
            if (record.SeedCompleted && record.Version != module.Version)
            {
                try
                {
                    var fromVersion = record.Version;
                    await module.OnUpgradeAsync(fromVersion, scope.ServiceProvider, ct);
                    record.Version = module.Version;
                    _logger.LogInformation("Module {Id}: upgraded {From} → {To}.",
                        module.ModuleId, fromVersion, module.Version);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Module {Id}: upgrade failed.", module.ModuleId);
                    errors.Add("upgrade: " + ex.Message);
                }
            }

            // ── 7. Persist activation status + error message ──────────────────
            record.LastActivationAttemptAt = FcmsTime.Now;
            record.ActivationError = errors.Count == 0
                ? null
                : Truncate(string.Join(" | ", errors), 2000);
            record.ActivationStatus = errors.Count == 0 ? "Active" : "Error";
            await repo.UpdateAsync(record, ct);
            await uow.SaveChangesAsync(ct);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max];
}

/// <summary>Holds DB connection info needed by <see cref="ModuleActivationService"/>.</summary>
public class ModuleActivationOptions
{
    public string ConnectionString { get; init; } = "";
    public string Provider { get; init; } = "mysql";
}
