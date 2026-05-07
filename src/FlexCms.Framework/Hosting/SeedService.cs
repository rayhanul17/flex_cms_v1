using FlexCms.Framework.Auth;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Models;
using FlexCms.Framework.Modules;
using FlexCms.Framework.Services;
using FlexCms.Framework.Setup;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

        try
        {
            await SeedMenuItemsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedService: failed to seed menu items.");
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
                if (record.Version != module.Manifest.Version || record.ActivationStatus != expectedStatus)
                {
                    record.Version = module.Manifest.Version;
                    record.ActivationStatus = expectedStatus;
                    if (expectedStatus == "Active" && record.ActivatedAt is null)
                        record.ActivatedAt = FcmsTime.Now;
                    await repo.UpdateAsync(record, ct);
                    anyChange = true;
                }
                continue;
            }

            await repo.AddAsync(new FcmsModuleRecord
            {
                ModuleId = module.ModuleId,
                Version = module.Manifest.Version,
                ActivationStatus = expectedStatus,
                ActivatedAt = expectedStatus == "Active" ? FcmsTime.Now : null
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
            if (record.Status == EntityStatus.Deleted) continue;
            record.Status = EntityStatus.Deleted;
            record.DeletedAt ??= FcmsTime.Now;
            await repo.UpdateAsync(record, ct);
            anyChange = true;
            _logger.LogInformation("SeedService: marked module record {Id} as removed (folder gone).",
                record.ModuleId);
        }

        if (anyChange) await uow.SaveChangesAsync(ct);
    }

    private static readonly FcmsPermission[] CorePermissions =
    [
        new() { Key = FcmsPermissions.PagesCreate,       DisplayName = "Pages: Create",              Group = "Pages" },
        new() { Key = FcmsPermissions.PagesEdit,         DisplayName = "Pages: Edit",                Group = "Pages" },
        new() { Key = FcmsPermissions.PagesDelete,       DisplayName = "Pages: Delete",              Group = "Pages" },
        new() { Key = FcmsPermissions.PostsCreate,       DisplayName = "Posts: Create",              Group = "Posts" },
        new() { Key = FcmsPermissions.PostsEdit,         DisplayName = "Posts: Edit",                Group = "Posts" },
        new() { Key = FcmsPermissions.PostsDelete,       DisplayName = "Posts: Delete",              Group = "Posts" },
        new() { Key = FcmsPermissions.CategoriesCreate,  DisplayName = "Categories: Create",         Group = "Posts" },
        new() { Key = FcmsPermissions.CategoriesEdit,    DisplayName = "Categories: Edit",           Group = "Posts" },
        new() { Key = FcmsPermissions.CategoriesDelete,  DisplayName = "Categories: Delete",         Group = "Posts" },
        new() { Key = FcmsPermissions.MediaView,         DisplayName = "Media: View Library",        Group = "Media" },
        new() { Key = FcmsPermissions.MediaUpload,       DisplayName = "Media: Upload",              Group = "Media" },
        new() { Key = FcmsPermissions.MediaEdit,         DisplayName = "Media: Move/Edit",           Group = "Media" },
        new() { Key = FcmsPermissions.MediaDelete,       DisplayName = "Media: Delete",              Group = "Media" },
        new() { Key = FcmsPermissions.MediaFolders,      DisplayName = "Media: Manage Folders",      Group = "Media" },
        new() { Key = FcmsPermissions.RedirectsCreate,   DisplayName = "Redirects: Create",          Group = "Redirects" },
        new() { Key = FcmsPermissions.RedirectsEdit,     DisplayName = "Redirects: Edit",            Group = "Redirects" },
        new() { Key = FcmsPermissions.RedirectsDelete,   DisplayName = "Redirects: Delete",          Group = "Redirects" },
        new() { Key = FcmsPermissions.RolesCreate,       DisplayName = "Roles: Create",              Group = "Admin" },
        new() { Key = FcmsPermissions.RolesEdit,         DisplayName = "Roles: Edit",                Group = "Admin" },
        new() { Key = FcmsPermissions.RolesDelete,       DisplayName = "Roles: Delete",              Group = "Admin" },
        new() { Key = FcmsPermissions.RolesManage,       DisplayName = "Roles: Manage",              Group = "Admin" },
        new() { Key = FcmsPermissions.RolesPermissions,  DisplayName = "Roles: Assign Permissions",  Group = "Admin" },
        new() { Key = FcmsPermissions.UsersCreate,       DisplayName = "Users: Create",              Group = "Admin" },
        new() { Key = FcmsPermissions.UsersEdit,         DisplayName = "Users: Edit",                Group = "Admin" },
        new() { Key = FcmsPermissions.UsersDelete,       DisplayName = "Users: Delete",              Group = "Admin" },
        new() { Key = FcmsPermissions.UsersManage,       DisplayName = "Users: Manage",              Group = "Admin" },
        new() { Key = FcmsPermissions.AuditView,         DisplayName = "Audit Log: View",            Group = "Admin" },
        new() { Key = FcmsPermissions.AuditManage,       DisplayName = "Audit Log: Manage",          Group = "Admin" },
        new() { Key = FcmsPermissions.SettingsManage,    DisplayName = "Settings: Manage",           Group = "Admin" },
        new() { Key = FcmsPermissions.MessagingView,     DisplayName = "Messaging: View",            Group = "Messaging" },
        new() { Key = FcmsPermissions.MessagingBroadcast,DisplayName = "Messaging: Broadcast",       Group = "Messaging" },
        new() { Key = Chat.ChatPermissions.Send,         DisplayName = "Chat: Send (user)",          Group = "Chat" },
        new() { Key = Chat.ChatPermissions.Reply,        DisplayName = "Chat: Reply (admin)",        Group = "Chat" },
        new() { Key = FcmsPermissions.PaymentsView,      DisplayName = "Payments: View",             Group = "Payments" },
        new() { Key = FcmsPermissions.PaymentsManage,    DisplayName = "Payments: Manage",           Group = "Payments" },
        new() { Key = FcmsPermissions.ExportsRequest,    DisplayName = "Exports: Request",           Group = "Exports" },
        new() { Key = FcmsPermissions.ExportsView,       DisplayName = "Exports: View",              Group = "Exports" },
        new() { Key = FcmsPermissions.ApiTokensManage,   DisplayName = "API tokens: Manage",         Group = "API" },
        new() { Key = FcmsPermissions.WebhooksManage,    DisplayName = "Webhooks: Manage",           Group = "API" },
        new() { Key = FcmsPermissions.CommentsModerate,  DisplayName = "Comments: Moderate",         Group = "Engagement" },
        new() { Key = FcmsPermissions.SubscribersManage, DisplayName = "Subscribers: Manage",        Group = "Engagement" },
    ];

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var permService = scope.ServiceProvider.GetService<IPermissionService>();
        if (permService is null) return;

        await permService.SeedPermissionsAsync(CorePermissions, ct);
    }

    private static readonly List<FcmsMenuItemDef> CoreMenuItems =
    [
        new() { DefaultName = "Dashboard",  Icon = "bi bi-speedometer2", Url = "/admin",       Order = 0 },

        // Blog group
        new() { DefaultName = "Blog",       Icon = "bi bi-journal-richtext", Url = "#blog",   Order = 10 },
        new() { DefaultName = "Posts",      Icon = "bi bi-newspaper",   Url = "/admin/posts",      Order = 11, ParentDefaultName = "Blog", RequiredPermission = FcmsPermissions.PostsEdit },
        new() { DefaultName = "Categories", Icon = "bi bi-folder",      Url = "/admin/categories", Order = 12, ParentDefaultName = "Blog", RequiredPermission = FcmsPermissions.CategoriesEdit },

        // Standalone content
        new() { DefaultName = "Pages",      Icon = "bi bi-file-earmark", Url = "/admin/pages",     Order = 20, RequiredPermission = FcmsPermissions.PagesEdit },
        new() { DefaultName = "Media",      Icon = "bi bi-images",       Url = "/admin/media",     Order = 30, RequiredPermission = FcmsPermissions.MediaView },
        new() { DefaultName = "Trash",      Icon = "bi bi-trash",        Url = "/admin/trash",     Order = 35 },

        // People group
        new() { DefaultName = "People",     Icon = "bi bi-people-fill",  Url = "#people",          Order = 40 },
        new() { DefaultName = "Users",      Icon = "bi bi-person",       Url = "/admin/users",       Order = 41, ParentDefaultName = "People", RequiredPermission = FcmsPermissions.UsersManage },
        new() { DefaultName = "Roles",      Icon = "bi bi-shield-lock",  Url = "/admin/roles",       Order = 42, ParentDefaultName = "People", RequiredPermission = FcmsPermissions.RolesManage },
        new() { DefaultName = "Permissions",Icon = "bi bi-key",          Url = "/admin/permissions", Order = 43, ParentDefaultName = "People", RequiredPermission = FcmsPermissions.RolesPermissions },

        // System group
        new() { DefaultName = "System",     Icon = "bi bi-sliders",      Url = "#system",            Order = 80 },
        new() { DefaultName = "Modules",    Icon = "bi bi-puzzle",       Url = "/admin/modules",     Order = 81, ParentDefaultName = "System" },
        new() { DefaultName = "Menu",       Icon = "bi bi-list-ul",      Url = "/admin/menu",        Order = 82, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.SettingsManage },
        new() { DefaultName = "Redirects",  Icon = "bi bi-sign-turn-right", Url = "/admin/redirects", Order = 83, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.RedirectsEdit },
        new() { DefaultName = "Audit Log",  Icon = "bi bi-journal-text", Url = "/admin/audit-log",   Order = 84, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.AuditView },
        new() { DefaultName = "Settings",   Icon = "bi bi-gear",         Url = "/admin/settings",    Order = 85, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.SettingsManage },

        // Messaging group (Phase 8)
        new() { DefaultName = "Messaging",  Icon = "bi bi-envelope",     Url = "#messaging",         Order = 70 },
        new() { DefaultName = "Broadcast",  Icon = "bi bi-megaphone",    Url = "/admin/broadcast",   Order = 71, ParentDefaultName = "Messaging", RequiredPermission = FcmsPermissions.MessagingView },
        new() { DefaultName = "SMTP / SMS", Icon = "bi bi-gear-wide-connected", Url = "/admin/messaging-settings", Order = 72, ParentDefaultName = "Messaging", RequiredPermission = FcmsPermissions.SettingsManage },

        // Chat (Phase 10)
        new() { DefaultName = "Chat", Icon = "bi bi-chat-dots", Url = "/admin/chat", Order = 75, ParentDefaultName = "Messaging", RequiredPermission = Chat.ChatPermissions.Reply },

        // Payments (Phase 12)
        new() { DefaultName = "Payments", Icon = "bi bi-credit-card-2-front", Url = "/admin/payments-settings", Order = 86, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.PaymentsView },
    ];

    private async Task SeedMenuItemsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var menuService = scope.ServiceProvider.GetService<IMenuService>();
        if (menuService is null) return;

        try
        {
            await menuService.SeedAsync("core", CoreMenuItems, ct);
        }
        catch (Exception ex) when (
            ex.Message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
        {
            // fcms_menu_items table missing on an existing pre-menu install.
            // Try to create it via the relational creator (EF Core idempotent).
            try
            {
                var ctx = scope.ServiceProvider.GetService<Db.Ef.FcmsDbContext>();
                if (ctx is not null)
                {
                    var creator = ctx.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
                    await creator.CreateTablesAsync(ct);
                    await menuService.SeedAsync("core", CoreMenuItems, ct);
                    _logger.LogInformation("SeedService: created fcms_menu_items table and seeded core items.");
                    return;
                }
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx,
                    "SeedService: fcms_menu_items table missing and auto-create failed. " +
                    "Drop+recreate the DB or add the table manually.");
                return;
            }

            _logger.LogError(ex, "SeedService: menu seed failed (table missing, no DbContext available).");
        }
    }
}
