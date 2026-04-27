using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.Ef;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Auth.MongoDb;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Db.Migration;
using FlexCms.Framework.Db.MongoDb;
using FlexCms.Framework.Middleware;
using FlexCms.Framework.Setup;
using FlexCms.Framework.Validators;
using Microsoft.AspNetCore.Authentication.Cookies;
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

        // Setup helper
        services.AddScoped<SetupHelper>(sp =>
        {
            var dp = sp.GetRequiredService<IDataProtectionProvider>();
            return new SetupHelper(dp, options.AppDataPath);
        });

        // Cookie authentication (8h sliding window)
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(opts =>
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
        services.AddSignalR();

        // Identity core
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
            .AddSignInManager<SignInManager<FcmsUser>>()
            .AddPasswordValidator<FcmsPasswordValidator>()
            .AddDefaultTokenProviders();

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

        // Register DB provider based on options
        if (options.UseMySQL)
        {
            services.AddDbContext<FcmsDbContext>(o =>
                o.UseMySql(
                    options.MySqlConnectionString,
                    Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(options.MySqlConnectionString),
                    m =>
                    {
                        m.EnableRetryOnFailure(3);
                        m.CommandTimeout(30);
                    }));

            services.AddScoped<DbContext>(sp => sp.GetRequiredService<FcmsDbContext>());
            services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
            services.AddScoped<IFcmsUnitOfWork, EfUnitOfWork>();

            identityBuilder.AddEntityFrameworkStores<FcmsDbContext>();
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

            if (!options.UseMySQL)
            {
                // MongoDB-only mode: register Mongo repositories and identity stores
                services.AddScoped(typeof(IRepository<>), typeof(MongoRepository<>));
                services.AddScoped<IFcmsUnitOfWork>(sp =>
                    new MongoUnitOfWork(
                        sp.GetRequiredService<IMongoClient>(),
                        sp.GetRequiredService<IMongoDatabase>()));

                services.AddScoped<IUserStore<FcmsUser>, MongoUserStore>();
                services.AddScoped<IRoleStore<FcmsRole>, MongoRoleStore>();
            }
        }

        return services;
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}

public class FlexCmsOptions
{
    public string AppDataPath { get; set; } = "App_Data";
    public bool UseMySQL { get; set; }
    public string MySqlConnectionString { get; set; } = string.Empty;
    public bool UseMongoDB { get; set; }
    public string MongoConnectionString { get; set; } = "mongodb://localhost:27017";
    public string MongoDatabaseName { get; set; } = "flexcms";
    public string[] AllowedIps { get; set; } = [];
    public bool EnforceIpFilter { get; set; }
    /// <summary>IANA or Windows timezone ID. Default: Asia/Dhaka (+06:00).</summary>
    public string TimeZoneId { get; set; } = "Asia/Dhaka";
}
