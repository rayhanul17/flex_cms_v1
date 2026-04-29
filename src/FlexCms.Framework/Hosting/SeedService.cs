using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Modules;
using FlexCms.Framework.Services;
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
        try
        {
            // Seed module records on every startup (cheap, idempotent)
            await SeedModuleRecordsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedService: failed to seed module records.");
        }

        try
        {
            await SeedPermissionsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedService: failed to seed permissions.");
        }

        var config = _setupHelper.Read();
        if (config is null || !config.IsSetupComplete || config.AdminSeeded)
            return;

        if (string.IsNullOrEmpty(config.AdminEmail) || string.IsNullOrEmpty(config.AdminPasswordEncrypted))
        {
            _logger.LogWarning("SeedService: admin email or password missing in setup.json — skipping seed.");
            return;
        }

        try
        {
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
            var user = await userManager.FindByEmailAsync(config.AdminEmail);
            if (user is null)
            {
                string plainPassword;
                try { plainPassword = _setupHelper.DecryptPassword(config.AdminPasswordEncrypted); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SeedService: failed to decrypt admin password — skipping seed.");
                    return;
                }

                user = new FcmsUser
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
            }

            // Ensure user is in SuperAdmin role
            if (!await userManager.IsInRoleAsync(user, FcmsRoles.SuperAdmin))
            {
                await userManager.AddToRoleAsync(user, FcmsRoles.SuperAdmin);
                _logger.LogInformation("SeedService: admin user {Email} added to SuperAdmin.", config.AdminEmail);
            }

            // 3. Mark seeded + clear stored password
            config.AdminSeeded = true;
            config.AdminPasswordEncrypted = string.Empty;
            _setupHelper.Write(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedService: failed during admin/role seeding.");
        }
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

        var presentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anyChange = false;

        foreach (var module in registry.Modules)
        {
            presentIds.Add(module.ModuleId);
            var expectedStatus = module.IsDeactivated ? "Inactive" : "Active";

            if (existing.TryGetValue(module.ModuleId, out var record))
            {
                if (record.Version != module.Manifest.Version || record.Status != expectedStatus)
                {
                    record.Version = module.Manifest.Version;
                    record.Status = expectedStatus;
                    if (expectedStatus == "Active" && record.ActivatedAt is null)
                        record.ActivatedAt = DateTime.UtcNow;
                    await repo.UpdateAsync(record, ct);
                    anyChange = true;
                }
                continue;
            }

            await repo.AddAsync(new FcmsModuleRecord
            {
                ModuleId = module.ModuleId,
                Version = module.Manifest.Version,
                Status = expectedStatus,
                ActivatedAt = expectedStatus == "Active" ? DateTime.UtcNow : null
            }, ct);
            anyChange = true;
            _logger.LogInformation("SeedService: registered module {Id} v{Version} ({Status}).",
                module.ModuleId, module.Manifest.Version, expectedStatus);
        }

        // Soft-delete records for modules whose folder no longer exists
        // (admin removed via Uninstall — folder + DLL gone before scan).
        foreach (var record in existing.Values)
        {
            if (presentIds.Contains(record.ModuleId)) continue;
            if (record.IsDeleted) continue;
            record.IsDeleted = true;
            await repo.UpdateAsync(record, ct);
            anyChange = true;
            _logger.LogInformation("SeedService: marked module record {Id} as removed (folder gone).",
                record.ModuleId);
        }

        if (anyChange) await uow.SaveChangesAsync(ct);
    }

    private static readonly FcmsPermission[] CorePermissions =
    [
        new() { Key = "pages.create",      DisplayName = "Pages: Create",              Group = "Pages" },
        new() { Key = "pages.edit",        DisplayName = "Pages: Edit",                Group = "Pages" },
        new() { Key = "pages.delete",      DisplayName = "Pages: Delete",              Group = "Pages" },
        new() { Key = "posts.create",      DisplayName = "Posts: Create",              Group = "Posts" },
        new() { Key = "posts.edit",        DisplayName = "Posts: Edit",                Group = "Posts" },
        new() { Key = "posts.delete",      DisplayName = "Posts: Delete",              Group = "Posts" },
        new() { Key = "categories.create", DisplayName = "Categories: Create",         Group = "Posts" },
        new() { Key = "categories.edit",   DisplayName = "Categories: Edit",           Group = "Posts" },
        new() { Key = "categories.delete", DisplayName = "Categories: Delete",         Group = "Posts" },
        new() { Key = "media.view",        DisplayName = "Media: View Library",        Group = "Media" },
        new() { Key = "media.upload",      DisplayName = "Media: Upload",              Group = "Media" },
        new() { Key = "media.edit",        DisplayName = "Media: Move/Edit",           Group = "Media" },
        new() { Key = "media.delete",      DisplayName = "Media: Delete",              Group = "Media" },
        new() { Key = "media.folders",     DisplayName = "Media: Manage Folders",      Group = "Media" },
        new() { Key = "redirects.create",  DisplayName = "Redirects: Create",         Group = "Redirects" },
        new() { Key = "redirects.edit",    DisplayName = "Redirects: Edit",           Group = "Redirects" },
        new() { Key = "redirects.delete",  DisplayName = "Redirects: Delete",         Group = "Redirects" },
        new() { Key = "roles.manage",      DisplayName = "Roles: Manage",             Group = "Admin" },
        new() { Key = "roles.permissions", DisplayName = "Roles: Assign Permissions", Group = "Admin" },
        new() { Key = "users.manage",      DisplayName = "Users: Manage",             Group = "Admin" },
        new() { Key = "audit.view",        DisplayName = "Audit Log: View",           Group = "Admin" },
        new() { Key = "audit.manage",      DisplayName = "Audit Log: Manage",         Group = "Admin" },
        new() { Key = "settings.manage",   DisplayName = "Settings: Manage",          Group = "Admin" },
    ];

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var permService = scope.ServiceProvider.GetService<IPermissionService>();
        if (permService is null) return;

        await permService.SeedPermissionsAsync(CorePermissions, ct);
    }
}
