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
        // Stage logging — IHostedServices block Kestrel port binding until
        // StartAsync returns. If a seed step hangs (DI cycle, DB unreachable),
        // these markers show in the log file exactly where it stopped.
        _logger.LogInformation("[SEED] StartAsync entered");
        try
        {
            _logger.LogInformation("[SEED] modules");
            await SeedModuleRecordsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedService: failed to seed module records.");
        }

        try
        {
            _logger.LogInformation("[SEED] permissions");
            await SeedPermissionsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedService: failed to seed permissions.");
        }

        try
        {
            _logger.LogInformation("[SEED] menu items");
            await SeedMenuItemsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedService: failed to seed menu items.");
        }

        try
        {
            _logger.LogInformation("[SEED] visitor role");
            await SeedVisitorRoleAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedService: failed to seed Visitor role.");
        }

        try
        {
            _logger.LogInformation("[SEED] sample content");
            await SeedSampleContentAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedService: failed to seed sample content.");
        }


        _logger.LogInformation("[SEED] admin user (if needed)");
        var config = _setupHelper.Read();
        if (config is null || !config.IsSetupComplete || config.AdminSeeded)
        {
            _logger.LogInformation("[SEED] StartAsync complete (admin already seeded)");
            return;
        }

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
                    FullName = string.IsNullOrWhiteSpace(config.AdminFullName) ? "Administrator" : config.AdminFullName,
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
        _logger.LogInformation("[SEED] StartAsync complete");
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
        new() { Key = FcmsPermissions.SettingsView,      DisplayName = "Settings: View",             Group = "Admin" },
        new() { Key = FcmsPermissions.SettingsManage,    DisplayName = "Settings: Manage",           Group = "Admin" },
        new() { Key = FcmsPermissions.SystemManage,      DisplayName = "System: Manage",             Group = "Admin" },
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
        new() { Key = FcmsPermissions.CommentsSubmit,    DisplayName = "Comments: Submit",           Group = "Engagement" },
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
        new() { DefaultName = "Dashboard",  Icon = "bi bi-speedometer2", Url = "/admin",       Order = 0, RequiredPermission = FcmsPermissions.SettingsView },

        // Blog group
        new() { DefaultName = "Blog",       Icon = "bi bi-journal-richtext", Url = "#blog",   Order = 10 },
        new() { DefaultName = "Posts",      Icon = "bi bi-newspaper",   Url = "/admin/posts",      Order = 11, ParentDefaultName = "Blog", RequiredPermission = FcmsPermissions.PostsEdit },
        new() { DefaultName = "Categories", Icon = "bi bi-folder",      Url = "/admin/categories", Order = 12, ParentDefaultName = "Blog", RequiredPermission = FcmsPermissions.CategoriesEdit },
        new() { DefaultName = "Comments",   Icon = "bi bi-chat-left-text", Url = "/admin/comments", Order = 13, ParentDefaultName = "Blog", RequiredPermission = FcmsPermissions.CommentsModerate },

        // Standalone content
        new() { DefaultName = "Pages",      Icon = "bi bi-file-earmark", Url = "/admin/pages",     Order = 20, RequiredPermission = FcmsPermissions.PagesEdit },
        new() { DefaultName = "Media",      Icon = "bi bi-images",       Url = "/admin/media",     Order = 30, RequiredPermission = FcmsPermissions.MediaView },
        new() { DefaultName = "Trash",      Icon = "bi bi-trash",        Url = "/admin/trash",     Order = 35, RequiredPermission = FcmsPermissions.PostsEdit },

        // People group
        new() { DefaultName = "People",     Icon = "bi bi-people-fill",  Url = "#people",          Order = 40 },
        new() { DefaultName = "Users",      Icon = "bi bi-person",       Url = "/admin/users",       Order = 41, ParentDefaultName = "People", RequiredPermission = FcmsPermissions.UsersManage },
        new() { DefaultName = "Roles",      Icon = "bi bi-shield-lock",  Url = "/admin/roles",       Order = 42, ParentDefaultName = "People", RequiredPermission = FcmsPermissions.RolesManage },
        new() { DefaultName = "Permissions",Icon = "bi bi-key",          Url = "/admin/permissions", Order = 43, ParentDefaultName = "People", RequiredPermission = FcmsPermissions.RolesPermissions },

        // System group
        new() { DefaultName = "System",         Icon = "bi bi-sliders",             Url = "#system",                    Order = 80 },
        new() { DefaultName = "Modules",        Icon = "bi bi-puzzle",              Url = "/admin/modules",             Order = 81, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.SystemManage },
        new() { DefaultName = "Menu",           Icon = "bi bi-list-ul",             Url = "/admin/menu",                Order = 82, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.SettingsManage },
        new() { DefaultName = "Redirects",      Icon = "bi bi-sign-turn-right",     Url = "/admin/redirects",           Order = 83, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.RedirectsEdit },
        new() { DefaultName = "Audit Log",      Icon = "bi bi-journal-text",        Url = "/admin/audit-log",           Order = 84, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.AuditView },
        new() { DefaultName = "Payments",       Icon = "bi bi-credit-card-2-front", Url = "/admin/payments-settings",   Order = 85, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.PaymentsView },
        new() { DefaultName = "Settings",       Icon = "bi bi-gear",                Url = "/admin/settings",            Order = 86, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.SettingsManage },
        new() { DefaultName = "System Admin",   Icon = "bi bi-terminal",            Url = "/admin/system",              Order = 87, ParentDefaultName = "System", RequiredPermission = FcmsPermissions.SystemManage },

        // Messaging group (Phase 8)
        new() { DefaultName = "Messaging",  Icon = "bi bi-envelope",                    Url = "#messaging",                 Order = 70 },
        new() { DefaultName = "Broadcast",  Icon = "bi bi-megaphone",                   Url = "/admin/broadcast",           Order = 71, ParentDefaultName = "Messaging", RequiredPermission = FcmsPermissions.MessagingView },
        new() { DefaultName = "SMTP / SMS", Icon = "bi bi-gear-wide-connected",         Url = "/admin/messaging-settings",  Order = 72, ParentDefaultName = "Messaging", RequiredPermission = FcmsPermissions.SettingsManage },

        // Chat (Phase 10)
        new() { DefaultName = "Chat", Icon = "bi bi-chat-dots", Url = "/admin/chat", Order = 75, ParentDefaultName = "Messaging", RequiredPermission = Chat.ChatPermissions.Reply },
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
            // RELATIONAL ONLY — fcms_menu_items table missing on an existing
            // pre-menu install. Try the EF-only auto-create path.
            // Mongo deployments never hit this branch because collections are
            // created on first insert; the original SeedAsync would have just
            // succeeded. If we DO land here without an EF DbContext (Mongo-
            // only setup mis-throwing a "not found" error), there's nothing
            // useful to recover so we log + return cleanly.
            var ctx = scope.ServiceProvider.GetService<Db.Ef.FcmsDbContext>();
            if (ctx is null)
            {
                _logger.LogError(ex,
                    "SeedService: menu seed failed and no EF DbContext available — " +
                    "if running on Mongo this is unexpected (collections auto-create on insert).");
                return;
            }
            try
            {
                var creator = ctx.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
                await creator.CreateTablesAsync(ct);
                await menuService.SeedAsync("core", CoreMenuItems, ct);
                _logger.LogInformation("SeedService: created fcms_menu_items table and seeded core items.");
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx,
                    "SeedService: fcms_menu_items table missing and auto-create failed. " +
                    "Drop+recreate the DB or add the table manually.");
            }
        }
    }

    private const string VisitorRoleName = "Visitor";
    private static readonly string[] VisitorPermissions = [FcmsPermissions.CommentsSubmit];

    private async Task SeedVisitorRoleAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FcmsUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<FcmsRole>>();
        var permService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        // Ensure Visitor role exists
        if (!await roleManager.RoleExistsAsync(VisitorRoleName))
            await roleManager.CreateAsync(new FcmsRole { Name = VisitorRoleName });

        var role = await roleManager.FindByNameAsync(VisitorRoleName);
        if (role is null) return;

        // Assign permissions to Visitor role (idempotent — AssignAsync is a no-op if already assigned)
        foreach (var perm in VisitorPermissions)
            await permService.AssignAsync(role.Id, perm, ct);

        // Ensure demo visitor account exists
        const string visitorEmail = "visitor@flexcms.local";
        const string visitorPass = "Visitor@123";
        var visitor = await userManager.FindByEmailAsync(visitorEmail);
        if (visitor is null)
        {
            visitor = new FcmsUser
            {
                UserName = visitorEmail,
                Email = visitorEmail,
                EmailConfirmed = true,
                FullName = "Demo Visitor",
                ForcePasswordChange = false,
            };
            var result = await userManager.CreateAsync(visitor, visitorPass);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(visitor, VisitorRoleName);
                _logger.LogInformation("SeedService: created demo Visitor account ({Email}).", visitorEmail);
            }
        }
    }

    private async Task SeedSampleContentAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetService<IRepository<FcmsPost>>();
        var uow = scope.ServiceProvider.GetService<IFcmsUnitOfWork>();
        if (repo is null || uow is null) return;

        // Only seed if no posts exist yet (fresh install)
        var existing = await repo.FirstOrDefaultAsync(_ => true, ct);
        if (existing is not null) return;

        var post = new FcmsPost
        {
            Title = "ইসলামের মহত্ত্ব ও তাৎপর্য",
            Slug = "islamer-mohotto-o-tatporjo",
            Excerpt = "ইসলাম একটি পূর্ণাঙ্গ জীবনব্যবস্থা। এই পোস্টে ইসলামের মূল্যবোধ, নৈতিকতা এবং মানবজীবনে তার গভীর প্রভাব নিয়ে আলোচনা করা হয়েছে।",
            Content = """
<h2>ভূমিকা</h2>
<p>ইসলাম শুধু একটি ধর্ম নয় — এটি একটি সম্পূর্ণ জীবনব্যবস্থা যা মানুষের ব্যক্তিগত, সামাজিক, অর্থনৈতিক ও আধ্যাত্মিক জীবনকে পরিচালিত করে। আরবি শব্দ "ইসলাম"-এর অর্থ হলো আত্মসমর্পণ — আল্লাহর ইচ্ছার কাছে নিজেকে সমর্পণ করা।</p>

<h2>ইসলামের মূল স্তম্ভ</h2>
<p>ইসলামের পাঁচটি মূল স্তম্ভ রয়েছে যা প্রতিটি মুসলমানের জীবনের ভিত্তি:</p>
<ol>
  <li><strong>শাহাদাহ (বিশ্বাসের ঘোষণা):</strong> "লা ইলাহা ইল্লাল্লাহ মুহাম্মাদুর রাসূলুল্লাহ" — আল্লাহ ছাড়া কোনো ইলাহ নেই এবং মুহাম্মদ (সা.) আল্লাহর রাসূল।</li>
  <li><strong>সালাত (নামাজ):</strong> দিনে পাঁচবার নামাজ আদায় — আল্লাহর সাথে সরাসরি যোগাযোগের মাধ্যম।</li>
  <li><strong>যাকাত (দান):</strong> সম্পদের একটি নির্দিষ্ট অংশ গরিবদের মধ্যে বিতরণ — সমাজের ভারসাম্য রক্ষার অনন্য ব্যবস্থা।</li>
  <li><strong>সওম (রোজা):</strong> রমজান মাসে সূর্যোদয় থেকে সূর্যাস্ত পর্যন্ত উপবাস — আত্মশুদ্ধি ও কৃতজ্ঞতার অনুশীলন।</li>
  <li><strong>হজ্ব:</strong> জীবনে একবার মক্কায় হজ্ব পালন — বিশ্বের সকল মুসলমানের ঐক্যের প্রতীক।</li>
</ol>

<h2>ইসলামের নৈতিক মূল্যবোধ</h2>
<p>ইসলাম মানুষকে শেখায়:</p>
<ul>
  <li><strong>সততা ও ন্যায়বিচার:</strong> প্রতিটি কাজে সত্যবাদিতা ও ন্যায়পরায়ণতা।</li>
  <li><strong>করুণা ও ক্ষমা:</strong> আল্লাহ অত্যন্ত দয়ালু ও ক্ষমাশীল — মানুষকেও তেমন হতে উৎসাহিত করা হয়।</li>
  <li><strong>পারিবারিক বন্ধন:</strong> পিতামাতার প্রতি সম্মান, স্বামী-স্ত্রীর পারস্পরিক দায়িত্ব এবং সন্তানের লালন-পালনকে ইবাদত হিসেবে গণ্য করা হয়।</li>
  <li><strong>সামাজিক দায়বদ্ধতা:</strong> প্রতিবেশীর প্রতি দায়িত্ব, দুর্বলের পাশে দাঁড়ানো।</li>
</ul>

<h2>ইসলামের বৈশ্বিক প্রভাব</h2>
<p>ইসলামি সভ্যতা মধ্যযুগে বিজ্ঞান, গণিত, চিকিৎসা ও দর্শনে অভূতপূর্ব অবদান রেখেছে। আল-খোয়ারিজমি বীজগণিত উদ্ভাবন করেছিলেন, ইবনে সিনা চিকিৎসাবিজ্ঞানের ভিত্তি স্থাপন করেছিলেন। ইসলামি পণ্ডিতরা গ্রিক জ্ঞানকে সংরক্ষণ করে ইউরোপের নবজাগরণের পথ তৈরি করেছিলেন।</p>

<h2>উপসংহার</h2>
<p>ইসলাম মানবজাতিকে শান্তি, ন্যায়বিচার ও ভ্রাতৃত্বের পথে আহ্বান করে। এর শিক্ষা কালজয়ী — ১৪০০ বছরেরও বেশি সময় ধরে লক্ষ কোটি মানুষের জীবনকে আলোকিত করে আসছে। ইসলামের মহত্ত্ব শুধু তার বিধানে নয়, বরং সেই বিধানগুলো মানুষের হৃদয়ে যে পরিবর্তন আনে তাতেই।</p>
<blockquote>
  <p>"নিশ্চয়ই আল্লাহর কাছে মনোনীত দীন হলো ইসলাম।" — সূরা আল-ইমরান, আয়াত ১৯</p>
</blockquote>
""",
            IsPublished = true,
            PublishedAt = FcmsTime.Now,
            FeaturedImageUrl = "/images/seed/sample-post-hero.svg",
            MetaTitle = "ইসলামের মহত্ত্ব ও তাৎপর্য | FlexCMS Blog",
            MetaDescription = "ইসলামের মূল স্তম্ভ, নৈতিক মূল্যবোধ এবং মানবসভ্যতায় ইসলামের অবদান নিয়ে একটি বিস্তারিত আলোচনা।",
        };

        await repo.AddAsync(post, ct);
        await uow.SaveChangesAsync(ct);
        _logger.LogInformation("SeedService: seeded sample post '{Title}'.", post.Title);
    }
}
