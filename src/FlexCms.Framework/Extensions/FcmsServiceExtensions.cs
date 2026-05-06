using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.Ef;
using FlexCms.Framework.Auth.History;
using FlexCms.Framework.Chat;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Health;
using FlexCms.Framework.Sessions;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Storage;
using FlexCms.Framework.Auth.MongoDb;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Db.Migration;
using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Documents;
using FlexCms.Framework.Exports;
using FlexCms.Framework.Hosting;
using FlexCms.Framework.I18n;
using FlexCms.Framework.Messaging;
using FlexCms.Framework.Messaging.Gateways;
using FlexCms.Framework.Messaging.Services;
using FlexCms.Framework.Middleware;
using FlexCms.Framework.Notifications;
using FlexCms.Framework.Rendering;
using FlexCms.Framework.Security;
using FlexCms.Framework.Themes;
using FlexCms.Framework.Widgets;
using FlexCms.Framework.Modules;
using FlexCms.Framework.Services;
using FlexCms.Framework.Setup;
using FlexCms.Framework.Validators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace FlexCms.Framework.Extensions;

public static class FcmsServiceExtensions
{
    public static IServiceCollection AddFlexCms(
        this IServiceCollection services,
        FlexCmsOptions options)
    {
        // Clock — UTC storage, site-timezone display
        var timeZone = ResolveTimeZone(options.TimeZoneId);
        var clock = new FcmsClock(timeZone);
        FcmsTime.Clock = clock;                          // update static access point too
        services.AddSingleton<IFcmsClock>(clock);

        // DataProtection — keyring persisted to App_Data/keys
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.AppDataPath, "keys")))
            .SetApplicationName("FlexCms");

        // Migration coordinator (NoOp for single-instance)
        services.AddSingleton<IFcmsMigrationCoordinator, NoOpMigrationCoordinator>();

        // Setup helper — Singleton: no scoped state, safe to share (also lets
        // SeedService consume it directly without a scope wrapper).
        services.AddSingleton<SetupHelper>(sp =>
        {
            var dp = sp.GetRequiredService<IDataProtectionProvider>();
            return new SetupHelper(dp, options.AppDataPath);
        });

        // Settings service (DB-backed; only useful when a DB provider is configured)
        services.AddScoped<ISettingsService, SettingsService>();

        // i18n — singleton translator preloaded with embedded Framework JSONs.
        // LanguageMiddleware writes the per-request current + site-default
        // language into HttpContext.Items so the translator never has to call
        // the (scoped) ISettingsService directly.
        services.AddSingleton<IFcmsTranslator>(sp =>
        {
            var http = sp.GetService<IHttpContextAccessor>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<FcmsTranslator>>();
            return new FcmsTranslator(http, logger);
        });

        // File storage — local by default; swap for cloud implementation without changing services
        services.AddScoped<IFcmsFileStorage, LocalFileStorage>();

        // CMS services — IFcmsUnitOfWork is injected by DI (registered below per provider)
        services.AddScoped<IPageService, PageService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IMediaFolderService, MediaFolderService>();
        services.AddHostedService<ScheduledPublishService>();
        services.AddSingleton(new TrashCleanupOptions { RetentionDays = options.TrashRetentionDays });
        services.AddHostedService<TrashCleanupService>();
        services.AddScoped<IFcmsLogService, FcmsLogService>();
        services.AddHostedService<LogArchiveService>();

        // ── Messaging (Phase 8) ──────────────────────────────────────────────
        // Settings facades wrap ISettingsService with IDataProtector to keep
        // SMTP password / SMS API key encrypted at rest.
        services.AddScoped<ISmtpSettingsService, SmtpSettingsService>();
        services.AddScoped<ISmsSettingsService, SmsSettingsService>();
        services.AddScoped<IFcmsEmailService, SmtpEmailService>();

        // SMS gateway pool — DispatchingSmsSender picks one per send via SmsSettings.Gateway.
        services.AddHttpClient<AlphaSmsGateway>();
        services.AddHttpClient<MramSmsGateway>();
        services.AddHttpClient<OnnorokomSmsGateway>();
        services.AddScoped<ISmsGateway>(sp => sp.GetRequiredService<AlphaSmsGateway>());
        services.AddScoped<ISmsGateway>(sp => sp.GetRequiredService<MramSmsGateway>());
        services.AddScoped<ISmsGateway>(sp => sp.GetRequiredService<OnnorokomSmsGateway>());
        services.AddScoped<IFcmsSmsSender, DispatchingSmsSender>();

        // In-memory bounded queue + worker (instant fire-and-forget).
        services.AddSingleton(new FcmsBackgroundQueueOptions { Capacity = 1000 });
        services.AddSingleton<IFcmsBackgroundQueue, FcmsBackgroundQueue>();
        services.AddHostedService<FcmsQueueProcessor>();

        // Restart-safe pending-message drainer (30s poll, retry 3x).
        services.AddSingleton(new MessageProcessorOptions());
        services.AddHostedService<MessageProcessorService>();

        services.AddScoped<IBroadcastService, BroadcastService>();

        // ── Phase 9: Notifications + widgets + honeypot + view rendering ─────
        services.AddScoped<IFcmsNotificationService, FcmsNotificationService>();
        services.AddScoped<IFcmsWidgetManager, FcmsWidgetManager>();
        services.AddSingleton<IFcmsHoneypotService, FcmsHoneypotService>();
        services.AddScoped<IFcmsViewRenderService, FcmsViewRenderService>();

        // ── Phase 10: Chat (SignalR is added at the host level via AddSignalR) ─
        services.AddScoped<IChatService, ChatService>();

        // ── Phase 12: Payments + PDF/Excel + Async Exports ───────────────────
        services.AddScoped<Payments.Services.IPaymentSettingsService, Payments.Services.PaymentSettingsService>();
        services.AddHttpClient<Payments.Gateways.BkashPaymentGateway>();
        services.AddHttpClient<Payments.Gateways.SslcommerzPaymentGateway>();
        services.AddHttpClient<Payments.Gateways.NagadPaymentGateway>();
        services.AddScoped<Payments.IFcmsPaymentGateway>(sp => sp.GetRequiredService<Payments.Gateways.BkashPaymentGateway>());
        services.AddScoped<Payments.IFcmsPaymentGateway>(sp => sp.GetRequiredService<Payments.Gateways.SslcommerzPaymentGateway>());
        services.AddScoped<Payments.IFcmsPaymentGateway>(sp => sp.GetRequiredService<Payments.Gateways.NagadPaymentGateway>());
        services.AddScoped<Payments.DispatchingPaymentGateway>();

        services.AddSingleton<IFcmsPdfService, PdfSharpPdfService>();
        services.AddSingleton<IFcmsExcelService, ClosedXmlExcelService>();

        services.AddSingleton(new ExportProcessorOptions());
        services.AddHostedService<ExportProcessorService>();

        // ── Phase 13: Auth hardening ─────────────────────────────────────────
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ILoginHistoryService, LoginHistoryService>();
        services.AddScoped<ILoginRedirectService, LoginRedirectService>();

        // Health checks — built-ins. Modules add more via AddSingleton<IFcmsHealthCheck, ...>().
        services.AddScoped<IFcmsHealthCheck, EfDatabaseHealthCheck>();
        services.AddSingleton<IFcmsHealthCheck>(sp => new BackgroundQueueHealthCheck(
            sp.GetRequiredService<IFcmsBackgroundQueue>(),
            sp.GetRequiredService<FcmsBackgroundQueueOptions>()));
        services.AddSingleton<IFcmsHealthCheck>(sp => new DiskSpaceHealthCheck(options.AppDataPath));

        // ── Phase 11: Themes ─────────────────────────────────────────────────
        var themesRoot = Path.Combine(options.AppDataPath, "..", "themes");
        services.AddSingleton<IThemeManager>(sp =>
            new ThemeManager(themesRoot, sp.GetService<Microsoft.Extensions.Logging.ILogger<ThemeManager>>()));
        services.Configure<Microsoft.AspNetCore.Mvc.Razor.RazorViewEngineOptions>(o =>
            o.ViewLocationExpanders.Add(new ThemeViewLocationExpander()));

        // Permission service (15min IMemoryCache — requires IRepository<> to be registered)
        services.AddMemoryCache();
        services.AddScoped<IPermissionService, PermissionService>();

        // Menu service — loads items from DB, filters by permission, caches 15 min
        services.AddScoped<IMenuService, MenuService>();

        // Context service — current user + IP + browser/OS via UAParser
        services.AddScoped<IFcmsContextService, FcmsContextService>();

        // Seed admin user + SuperAdmin role on first production-mode startup
        services.AddHostedService<SeedService>();

        // Run module EF migrations + SeedDataAsync on every startup (idempotent)
        services.AddSingleton(new ModuleActivationOptions
        {
            ConnectionString = options.UsesRelationalDb
                ? (options.UseMySQL ? options.MySqlConnectionString
                    : options.UseMsSql ? options.MsSqlConnectionString
                    : options.PostgreSqlConnectionString)
                : "",
            Provider = options.UseMySQL ? "mysql"
                : options.UseMsSql ? "mssql"
                : options.UsePostgreSQL ? "postgresql"
                : "mongodb"
        });
        services.AddHostedService<ModuleActivationService>();

        // ── Module discovery + wiring ────────────────────────────────────────
        // Scan the modules/ directory (sibling of App_Data). Each discovered module gets:
        //   1. RegisterServices(services) called
        //   2. AttributeScanner runs over its assembly for [FcmsScoped]/etc
        //   3. Its assembly added as an MVC ApplicationPart so its controllers
        //      and Razor views become routable
        // ModuleLoader/Manager/StateService are available for admin UI queries.
        services.AddSingleton<ModuleLoader>();
        services.AddSingleton<ModuleManager>();
        services.AddSingleton<ModuleStateService>();

        var modulesRoot = Path.Combine(options.AppDataPath, "..", "modules");
        var registry = BuildModuleRegistry(services, modulesRoot);
        services.AddSingleton(registry);

        // Cookie authentication (8h sliding window).
        // Scheme name MUST be IdentityConstants.ApplicationScheme so that
        // SignInManager.PasswordSignInAsync (which targets that scheme) works
        // with AddIdentityCore (which does NOT auto-register Identity cookies).
        services.Configure<SecurityStampValidatorOptions>(o =>
            o.ValidationInterval = TimeSpan.FromMinutes(30));

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, opts =>
            {
                opts.Cookie.HttpOnly = true;
                opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                opts.Cookie.SameSite = SameSiteMode.Strict;
                opts.SlidingExpiration = true;
                opts.ExpireTimeSpan = TimeSpan.FromHours(8);
                opts.LoginPath = "/auth/login";
                opts.LogoutPath = "/auth/logout";
            });

        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddAntiforgery(o => o.HeaderName = "X-FlexCms-Csrf");
        services.AddSession(o => { o.Cookie.HttpOnly = true; o.Cookie.IsEssential = true; o.IdleTimeout = TimeSpan.FromMinutes(30); });

        // Rate limiting — partitioned by IP (M19: prevents one IP from blocking others)
        services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // "login" policy: 10 attempts/min per IP
                if (ctx.Request.Path.StartsWithSegments("/auth/login"))
                    return RateLimitPartition.GetFixedWindowLimiter($"login:{ip}", _ =>
                        new FixedWindowRateLimiterOptions
                        {
                            Window = TimeSpan.FromMinutes(1),
                            PermitLimit = 10,
                            AutoReplenishment = true
                        });

                // "otp" policy: 5 attempts/min per IP
                if (ctx.Request.Path.StartsWithSegments("/auth/forgot-password") ||
                    ctx.Request.Path.StartsWithSegments("/auth/verify-otp"))
                    return RateLimitPartition.GetFixedWindowLimiter($"otp:{ip}", _ =>
                        new FixedWindowRateLimiterOptions
                        {
                            Window = TimeSpan.FromMinutes(1),
                            PermitLimit = 5,
                            AutoReplenishment = true
                        });

                return RateLimitPartition.GetNoLimiter("none");
            });

            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        // IP filter options
        services.Configure<IpFilterOptions>(opts =>
        {
            opts.AllowedIps = options.AllowedIps;
            opts.EnforceIpFilter = options.EnforceIpFilter;
        });

        // Register DB provider + Identity stores (only when a provider is configured)
        if (options.UsesRelationalDb || options.UseMongoDB)
        {
            var identityBuilder = services
                .AddIdentityCore<FcmsUser>(opts =>
                {
                    opts.Password.RequireDigit = true;
                    opts.Password.RequireLowercase = true;
                    opts.Password.RequireUppercase = true;
                    opts.Password.RequireNonAlphanumeric = true;
                    opts.Password.RequiredLength = 8;
                    opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                    opts.Lockout.MaxFailedAccessAttempts = 5;
                    opts.Lockout.AllowedForNewUsers = true;
                    opts.User.RequireUniqueEmail = true;
                })
                .AddRoles<FcmsRole>()
                .AddClaimsPrincipalFactory<UserClaimsPrincipalFactory<FcmsUser, FcmsRole>>()
                .AddSignInManager<SignInManager<FcmsUser>>()
                .AddPasswordValidator<FcmsPasswordValidator>()
                .AddDefaultTokenProviders();

            if (options.UseMySQL)
            {
                services.AddDbContext<FcmsDbContext>((sp, o) =>
                    o.UseMySql(
                        options.MySqlConnectionString,
                        Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(options.MySqlConnectionString),
                        m => { m.EnableRetryOnFailure(3); m.CommandTimeout(30); }));

                RegisterEfServices(services, identityBuilder);
            }
            else if (options.UseMsSql)
            {
                services.AddDbContext<FcmsDbContext>((sp, o) =>
                    o.UseSqlServer(
                        options.MsSqlConnectionString,
                        m => { m.EnableRetryOnFailure(3); m.CommandTimeout(30); }));

                RegisterEfServices(services, identityBuilder);
            }
            else if (options.UsePostgreSQL)
            {
                services.AddDbContext<FcmsDbContext>((sp, o) =>
                    o.UseNpgsql(
                        options.PostgreSqlConnectionString,
                        m => { m.EnableRetryOnFailure(3); m.CommandTimeout(30); }));

                RegisterEfServices(services, identityBuilder);
            }

            if (options.UseMongoDB)
            {
                MongoDbSerializerSetup.Register();

                services.AddSingleton<IMongoClient>(_ => new MongoClient(options.MongoConnectionString));
                services.AddSingleton<IMongoDatabase>(sp =>
                {
                    var client = sp.GetRequiredService<IMongoClient>();
                    return client.GetDatabase(options.MongoDatabaseName);
                });

                if (!options.UsesRelationalDb)
                {
                    // MongoDB-only mode: register Mongo repositories and identity stores
                    services.AddScoped(typeof(IRepository<>), typeof(MongoRepository<>));
                    services.AddScoped<IFcmsUnitOfWork>(sp =>
                        new MongoUnitOfWork(
                            sp.GetRequiredService<IMongoClient>(),
                            sp.GetRequiredService<IMongoDatabase>()));

                    identityBuilder.AddUserStore<MongoUserStore>();
                    identityBuilder.AddRoleStore<MongoRoleStore>();

                    // Create indexes mirroring EF unique constraints / FKs
                    services.AddHostedService<MongoIndexService>();
                }
            }
        }

        return services;
    }

    private static void RegisterEfServices(
        IServiceCollection services,
        IdentityBuilder identityBuilder)
    {
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FcmsDbContext>());
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IFcmsUnitOfWork, EfUnitOfWork>();
        identityBuilder.AddEntityFrameworkStores<FcmsDbContext>();
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }

    /// <summary>
    /// Scan the modules root, instantiate each loaded module, and wire it
    /// into the host: call its <c>RegisterServices</c>, scan its assembly
    /// for attribute-marked types, and add it as an MVC ApplicationPart.
    /// </summary>
    private static ModuleRegistry BuildModuleRegistry(IServiceCollection services, string modulesRoot)
    {
        // We can't pull a logger from DI here (container isn't built yet);
        // the manager / loader log via NullLogger when invoked statically.
        var loaderLog = Microsoft.Extensions.Logging.Abstractions.NullLogger<ModuleLoader>.Instance;
        var managerLog = Microsoft.Extensions.Logging.Abstractions.NullLogger<ModuleManager>.Instance;
        var loader = new ModuleLoader(loaderLog);
        var manager = new ModuleManager(loader, managerLog);

        var loaded = manager.ScanAndLoad(modulesRoot);
        var mvcBuilder = services.AddMvcCore();   // idempotent — returns existing builder if already added

        foreach (var module in loaded)
        {
            // Deactivated modules stay in the registry (so admin UI can list
            // them) but their services and routes are never registered.
            if (module.IsDeactivated) continue;

            module.Instance.RegisterServices(services);
            AttributeScanner.RegisterAttributedTypes(services, module.Assembly);
            mvcBuilder.AddApplicationPart(module.Assembly);
        }

        return new ModuleRegistry(loaded);
    }
}

public class FlexCmsOptions
{
    public string AppDataPath { get; set; } = "App_Data";

    // ── Relational providers (mutually exclusive — only one active at a time) ──
    public bool UseMySQL { get; set; }
    public string MySqlConnectionString { get; set; } = string.Empty;

    public bool UseMsSql { get; set; }
    public string MsSqlConnectionString { get; set; } = string.Empty;

    public bool UsePostgreSQL { get; set; }
    public string PostgreSqlConnectionString { get; set; } = string.Empty;

    // ── MongoDB (can run alongside a relational provider for Mongo-specific data) ──
    public bool UseMongoDB { get; set; }
    public string MongoConnectionString { get; set; } = "mongodb://localhost:27017";
    public string MongoDatabaseName { get; set; } = "flexcms";

    public string[] AllowedIps { get; set; } = [];
    public bool EnforceIpFilter { get; set; }
    /// <summary>Days before trashed items are permanently deleted. Default: 30.</summary>
    public int TrashRetentionDays { get; set; } = 30;
    /// <summary>IANA or Windows timezone ID. Default: Asia/Dhaka (+06:00).</summary>
    public string TimeZoneId { get; set; } = "Asia/Dhaka";

    public bool UsesRelationalDb => UseMySQL || UseMsSql || UsePostgreSQL;
}
