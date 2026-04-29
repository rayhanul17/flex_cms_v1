using FlexCms.Framework.Extensions;
using FlexCms.Framework.Middleware;
using FlexCms.Framework.Setup;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");

// ── Logging (Serilog) ─────────────────────────────────────────────────────────
var logPath = Path.Combine(appDataPath, "logs", "flexcms-.log");
var logConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File(logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

if (builder.Environment.IsDevelopment())
    logConfig.WriteTo.Console();

Log.Logger = logConfig.CreateLogger();
builder.Host.UseSerilog();

// ── Two-path startup ──────────────────────────────────────────────────────────
// Setup mode runs a minimal pipeline (SetupController only) until the wizard
// completes and calls StopApplication(). The process then restarts under the
// production-mode path where full DI (Identity, DB, etc.) is registered.
// ─────────────────────────────────────────────────────────────────────────────

if (!SetupHelper.IsSetupComplete(appDataPath))
{
    // ── SETUP MODE ────────────────────────────────────────────────────────────
    builder.Services.AddSetupModeServices(appDataPath);

    var setupApp = builder.Build();

    setupApp.UseStaticFiles();
    setupApp.UseRouting();
    setupApp.UseSession();
    // All Setup routes use attribute routing — no conventional route needed.
    // MapFallback catches everything else (e.g. /auth/login) and sends it to /Setup.
    setupApp.MapControllers();
    setupApp.MapFallback(ctx =>
    {
        ctx.Response.Redirect("/Setup");
        return Task.CompletedTask;
    });

    setupApp.Run();
    return;
}

// ── PRODUCTION MODE ───────────────────────────────────────────────────────────
// DB config comes from setup.json (written by setup wizard).
// appsettings.json values are used as fallback for non-DB options (IP filter, etc.)
// and for developer overrides during local development.
builder.Services.AddControllersWithViews()
    .AddRazorOptions(o =>
    {
        // Admin controllers live under Controllers/Admin/ but Razor only looks
        // at /Views/{Controller}/{Action} by default. Add /Views/Admin/{...}
        // so admin views can be grouped alongside their controllers.
        o.ViewLocationFormats.Add("/Views/Admin/{1}/{0}.cshtml");
    });

var setup = SetupHelper.ReadStatic(appDataPath);
var cfg = builder.Configuration;

builder.Services.AddFlexCms(new FlexCmsOptions
{
    AppDataPath = appDataPath,

    // Relational provider — setup.json is authoritative; appsettings.json as dev fallback
    UseMySQL = setup?.DbProvider == "mysql" || cfg.GetValue<bool>("FlexCms:UseMySQL"),
    UseMsSql = setup?.DbProvider == "mssql" || cfg.GetValue<bool>("FlexCms:UseMsSql"),
    UsePostgreSQL = setup?.DbProvider == "postgresql" || cfg.GetValue<bool>("FlexCms:UsePostgreSQL"),

    MySqlConnectionString = setup?.DbProvider == "mysql" ? (setup.DbConnectionString) : cfg.GetConnectionString("MySQL") ?? string.Empty,
    MsSqlConnectionString = setup?.DbProvider == "mssql" ? (setup.DbConnectionString) : cfg.GetConnectionString("MsSQL") ?? string.Empty,
    PostgreSqlConnectionString = setup?.DbProvider == "postgresql" ? (setup.DbConnectionString) : cfg.GetConnectionString("PostgreSQL") ?? string.Empty,

    // MongoDB
    UseMongoDB = setup?.DbProvider == "mongodb" || cfg.GetValue<bool>("FlexCms:UseMongoDB"),
    MongoConnectionString = setup?.DbProvider == "mongodb" ? (setup.MongoConnectionString) : cfg.GetConnectionString("MongoDB") ?? "mongodb://localhost:27017",
    MongoDatabaseName = setup?.DbProvider == "mongodb" ? (setup.MongoDatabase ?? "flexcms") : cfg.GetValue<string>("FlexCms:MongoDatabaseName") ?? "flexcms",

    // Site options — setup.json first, appsettings.json fallback
    TimeZoneId = setup?.TimeZoneId ?? cfg.GetValue<string>("FlexCms:TimeZoneId") ?? "Asia/Dhaka",
    EnforceIpFilter = cfg.GetValue<bool>("FlexCms:EnforceIpFilter"),
    AllowedIps = cfg.GetSection("FlexCms:AllowedIps").Get<string[]>() ?? []
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();   // full stack trace in browser

app.UseMiddleware<FcmsExceptionMiddleware>();   // logs + generic page in production
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RedirectMiddleware>();
app.UseMiddleware<IpFilterMiddleware>();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSession();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ForcePasswordChangeMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// CMS page slug catch-all — must come after all other conventional routes
// so attribute-routed controllers (admin, auth, blog) take priority.
app.MapControllerRoute(
    name: "cms-page",
    pattern: "{slug}",
    defaults: new { controller = "Frontend", action = "Page" });

app.Run();

// Needed for WebApplicationFactory in integration tests
public partial class Program { }
