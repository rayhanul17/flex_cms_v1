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
    /// <summary>
    /// Hard cap on per-restart seed retries. Three is enough to absorb a
    /// transient DB hiccup (mid-publish window etc.) but cheap enough that
    /// a genuine bug surfaces within minutes instead of growing a giant
    /// repeating stack trace in the audit log.
    /// </summary>
    public const int MaxSeedAttempts = 3;

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

            _state.SyncWwwroot(loaded.FolderPath, _env.WebRootPath, module.ModuleId);

            // Refuse to migrate + seed if the module's manifest doesn't
            // declare support for the host's configured DB provider.
            // Surfaces in admin UI as a clear "provider mismatch" instead of
            // a cryptic EF error mid-migration. See security-audit-fix-plan §6.1.
            if (!string.IsNullOrEmpty(_opts.Provider) &&
                loaded.Manifest.SupportedDbProviders.Length > 0 &&
                !loaded.Manifest.SupportedDbProviders.Contains(_opts.Provider, StringComparer.OrdinalIgnoreCase))
            {
                var msg = $"provider: module {module.ModuleId} does not declare support for '{_opts.Provider}' " +
                          $"(declared: {string.Join(", ", loaded.Manifest.SupportedDbProviders)})";
                _logger.LogError("Module {Id}: skipping activation — {Msg}", module.ModuleId, msg);
                errors.Add(msg);
                await PersistActivationStateAsync(repo, uow, module, errors, false, ct);
                continue;
            }

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

            // Cap the retry-on-restart loop. Repeating a buggy SeedDataAsync
            // every reboot hides the real failure: the module never reaches
            // a usable state, but logs fill with the same exception and an
            // operator can't tell whether the third retry might recover. After
            // MaxSeedAttempts we leave SeedCompleted = false but stop trying
            // and surface "give up — manual fix needed" on the module list.
            if (!record.SeedCompleted && record.SeedAttemptCount < MaxSeedAttempts)
            {
                record.SeedAttemptCount++;
                try
                {
                    await module.SeedDataAsync(scope.ServiceProvider, ct);
                    record.SeedCompleted = true;
                    record.SeedAttemptCount = 0;
                    record.Version = module.Version;
                    _logger.LogInformation("Module {Id}: seed completed.", module.ModuleId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Module {Id}: seed failed (attempt {Attempt}/{Max}).",
                        module.ModuleId, record.SeedAttemptCount, MaxSeedAttempts);
                    errors.Add($"seed (attempt {record.SeedAttemptCount}/{MaxSeedAttempts}): {ex.Message}");
                }
            }
            else if (!record.SeedCompleted)
            {
                // Cap exceeded — keep flagging the error but don't try again
                // unless the operator resets the counter.
                errors.Add($"seed gave up after {MaxSeedAttempts} attempts — fix the module then click Retry seed in admin.");
            }

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

    /// <summary>
    /// Find-or-create the module record and stamp its activation status. Used
    /// for the early-bail paths (e.g. provider-mismatch §6.1) so the admin
    /// module list shows the error instead of just silently skipping.
    /// </summary>
    private static async Task PersistActivationStateAsync(
        Db.IRepository<FcmsModuleRecord> repo,
        Db.IFcmsUnitOfWork uow,
        IFcmsModule module,
        List<string> errors,
        bool active,
        CancellationToken ct)
    {
        var record = await repo.FirstOrDefaultAsync(r => r.ModuleId == module.ModuleId, ct);
        if (record is null)
        {
            record = new FcmsModuleRecord
            {
                ModuleId = module.ModuleId,
                Version = module.Version,
                ActivationStatus = active ? "Active" : "Error",
                ActivatedAt = active ? FcmsTime.Now : null,
            };
            await repo.AddAsync(record, ct);
        }
        record.LastActivationAttemptAt = FcmsTime.Now;
        record.ActivationStatus = active ? "Active" : "Error";
        record.ActivationError = errors.Count == 0 ? null : Truncate(string.Join(" | ", errors), 2000);
        await repo.UpdateAsync(record, ct);
        await uow.SaveChangesAsync(ct);
    }
}

/// <summary>Holds DB connection info needed by <see cref="ModuleActivationService"/>.</summary>
public class ModuleActivationOptions
{
    public string ConnectionString { get; init; } = "";
    public string Provider { get; init; } = "mysql";
}
