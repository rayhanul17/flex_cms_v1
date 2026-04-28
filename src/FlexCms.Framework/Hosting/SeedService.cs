using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Modules;
using FlexCms.Framework.Setup;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Hosting;

// Runs once on production-mode startup.
// Creates SuperAdmin role + initial admin user from setup.json, then clears the stored password.
public class SeedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SetupHelper _setupHelper;
    private readonly ILogger<SeedService> _logger;

    public SeedService(
        IServiceScopeFactory scopeFactory,
        SetupHelper setupHelper,
        ILogger<SeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _setupHelper = setupHelper;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // Seed module records on every startup (cheap, idempotent) regardless
        // of whether admin seeding still needs to run.
        await SeedModuleRecordsAsync(ct);

        var config = _setupHelper.Read();
        if (config is null || !config.IsSetupComplete || config.AdminSeeded)
            return;

        if (string.IsNullOrEmpty(config.AdminEmail) || string.IsNullOrEmpty(config.AdminPasswordEncrypted))
        {
            _logger.LogWarning("SeedService: admin email or password missing in setup.json — skipping seed.");
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FcmsUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<FcmsRole>>();

        // 1. Ensure SuperAdmin role exists
        if (!await roleManager.RoleExistsAsync(FcmsRoles.SuperAdmin))
        {
            var roleResult = await roleManager.CreateAsync(new FcmsRole { Name = FcmsRoles.SuperAdmin });
            if (!roleResult.Succeeded)
            {
                _logger.LogError("SeedService: failed to create SuperAdmin role — {Errors}",
                    string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                return;
            }
        }

        // 2. Create admin user if not exists
        var existingUser = await userManager.FindByEmailAsync(config.AdminEmail);
        if (existingUser is null)
        {
            string plainPassword;
            try { plainPassword = _setupHelper.DecryptPassword(config.AdminPasswordEncrypted); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SeedService: failed to decrypt admin password — skipping seed.");
                return;
            }

            var user = new FcmsUser
            {
                UserName = config.AdminEmail,
                Email = config.AdminEmail,
                EmailConfirmed = true,
                ForcePasswordChange = false
            };

            var createResult = await userManager.CreateAsync(user, plainPassword);
            if (!createResult.Succeeded)
            {
                _logger.LogError("SeedService: failed to create admin user — {Errors}",
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
                return;
            }

            await userManager.AddToRoleAsync(user, FcmsRoles.SuperAdmin);
            _logger.LogInformation("SeedService: admin user {Email} created and added to SuperAdmin.", config.AdminEmail);
        }

        // 3. Mark seeded + clear stored password
        config.AdminSeeded = true;
        config.AdminPasswordEncrypted = string.Empty;
        _setupHelper.Write(config);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// For every loaded module, ensure an <see cref="FcmsModuleRecord"/> exists
    /// in the DB. Records are created with Status="Active" since the module's
    /// services and routes are already wired by <c>AddFlexCms</c>. The version
    /// field is updated when a module's manifest version changes.
    /// </summary>
    private async Task SeedModuleRecordsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetService<ModuleRegistry>();
        if (registry is null || registry.Modules.Count == 0) return;

        var repo = scope.ServiceProvider.GetService<IRepository<FcmsModuleRecord>>();
        var uow = scope.ServiceProvider.GetService<IFcmsUnitOfWork>();
        if (repo is null || uow is null) return;

        var existing = (await repo.GetAllAsync(ct))
            .ToDictionary(r => r.ModuleId, StringComparer.OrdinalIgnoreCase);

        var anyChange = false;
        foreach (var module in registry.Modules)
        {
            if (existing.TryGetValue(module.ModuleId, out var record))
            {
                if (record.Version != module.Manifest.Version)
                {
                    record.Version = module.Manifest.Version;
                    await repo.UpdateAsync(record, ct);
                    anyChange = true;
                }
                continue;
            }

            await repo.AddAsync(new FcmsModuleRecord
            {
                ModuleId = module.ModuleId,
                Version = module.Manifest.Version,
                Status = "Active",
                ActivatedAt = DateTime.UtcNow
            }, ct);
            anyChange = true;
            _logger.LogInformation("SeedService: registered module {Id} v{Version}.",
                module.ModuleId, module.Manifest.Version);
        }

        if (anyChange) await uow.SaveChangesAsync(ct);
    }
}
