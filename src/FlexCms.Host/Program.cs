using FlexCms.Framework.Extensions;
using FlexCms.Framework.Middleware;
using FlexCms.Framework.Setup;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");

// ── Logging (Serilog) ─────────────────────────────────────────────────────────
// Defaults: rolling File sink (30-day retention) + Console in Dev. Operators
// who want centralized logging (Seq / Elasticsearch / Application Insights /
// Datadog) drop a "Serilog" section into appsettings.{Environment}.json plus
// the matching `Serilog.Sinks.<Name>` NuGet package — Phase 15 / Issue 88.
// Sinks declared in config layer ON TOP of the file sink, not replacing it.
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

// ReadFrom.Configuration only attaches sinks if a "Serilog" section exists.
// Catches the case where the config references a sink NuGet that isn't
// installed — log to the file sink + carry on rather than crash startup.
try
{
    if (builder.Configuration.GetSection("Serilog").Exists())
        logConfig.ReadFrom.Configuration(builder.Configuration);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[Serilog] Failed to apply Serilog config from appsettings — falling back to file sink only. {ex.Message}");
}

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
builder.Services.AddControllersWithViews(mvc =>
    {
        // Custom binder for jQuery DataTables 2.x bracket-notation form data
        mvc.ModelBinderProviders.Insert(0, new FlexCms.Framework.Models.DataTablesRequestModelBinderProvider());

        // .NET 6+ defaults to treating non-nullable reference types as
        // implicitly required, which makes optional form fields (e.g.
        // SiteTagline = "" with placeholder="Optional") silently fail
        // ModelState validation when the user posts an empty value. Disable
        // that — controllers must use [Required] explicitly when they really
        // need a value. (Found via Settings page: empty Tagline / BaseUrl
        // failed silently with no toast and no field error.)
        mvc.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddRazorOptions(o =>
    {
        // Admin controllers live under Controllers/Admin/ but Razor only looks
        // at /Views/{Controller}/{Action} by default. Add /Views/Admin/{...}
        // so admin views can be grouped alongside their controllers.
        o.ViewLocationFormats.Add("/Views/Admin/{1}/{0}.cshtml");
    })
    // Themes ship cshtml under /themes/{Id}/Views/ — outside the default
    // /Views/ tree the Razor SDK precompiles, so the runtime view engine
    // needs RuntimeCompilation to discover them.
    .AddRazorRuntimeCompilation();

// SignalR (Phase 10 — chat). Default in-memory backplane is fine for
// single-instance deploys; multi-node would swap in Redis backplane here.
builder.Services.AddSignalR();

// Register the themes/ folder as an additional file provider for the Razor
// runtime view engine — the ThemeViewLocationExpander emits paths like
// /themes/{Id}/Views/Shared/_PublicLayout.cshtml that resolve against the
// project content root once registered here.
var themesPhysical = Path.Combine(builder.Environment.ContentRootPath, "themes");
if (Directory.Exists(themesPhysical))
{
    builder.Services.Configure<Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation.MvcRazorRuntimeCompilationOptions>(o =>
    {
        o.FileProviders.Add(new Microsoft.Extensions.FileProviders.PhysicalFileProvider(builder.Environment.ContentRootPath));
    });
}

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

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseMiddleware<FcmsExceptionMiddleware>();   // logs + generic page in production
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<IpFilterMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");

app.UseHttpsRedirection();
// Hotlink protection runs BEFORE static files — otherwise the static-file
// middleware would serve /uploads/* and we'd never see the request.
app.UseMiddleware<FlexCms.Framework.Middleware.HotlinkProtectionMiddleware>();
app.UseStaticFiles();
// CORS runs before routing + auth so preflight OPTIONS replies fast even
// when the eventual endpoint requires authentication.
app.UseMiddleware<FlexCms.Framework.Middleware.CorsFromSettingsMiddleware>();
app.UseMiddleware<RedirectMiddleware>();   // after static files — no DB hit per asset
app.UseMiddleware<FlexCms.Framework.I18n.LanguageMiddleware>();   // sets culture + strips /{lang}/ prefix BEFORE routing
app.UseRouting();
app.UseSession();
app.UseRateLimiter();
app.UseAuthentication();
// Session-revocation enforcement runs between authentication (so we have a
// principal) and authorization (so a revoked session is treated as anonymous
// before [Authorize] checks fire). Bearer-token requests skip naturally —
// they don't carry the fcms.session_id claim.
app.UseMiddleware<FlexCms.Framework.Sessions.FcmsSessionValidationMiddleware>();
// Maintenance mode: must run AFTER authentication (so role-based bypass
// works) but BEFORE authorization (so the maintenance page renders for
// non-bypassed users without going through [Authorize] checks).
app.UseMiddleware<FlexCms.Framework.Maintenance.MaintenanceModeMiddleware>();
app.UseAuthorization();
app.UseMiddleware<ForcePasswordChangeMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Phase 10 — chat hub
app.MapHub<FlexCms.Framework.Chat.ChatHub>("/hubs/chat");
// Phase 16 — admin notification hub (real-time bell push, replaces 60s polling).
app.MapHub<FlexCms.Framework.Notifications.AdminNotificationHub>("/hubs/admin-notifications");

// CMS page slug catch-all — must come after all other conventional routes
// so attribute-routed controllers (admin, auth, blog) take priority.
app.MapControllerRoute(
    name: "cms-page",
    pattern: "{slug}",
    defaults: new { controller = "Frontend", action = "Page" });

app.Run();

// Needed for WebApplicationFactory in integration tests
public partial class Program { }
