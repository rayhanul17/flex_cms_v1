using FlexCms.Framework.Extensions;
using FlexCms.Framework.Middleware;
using FlexCms.Framework.Setup;

var builder = WebApplication.CreateBuilder(args);

var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");

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
    setupApp.MapControllerRoute("default", "{controller=Setup}/{action=Index}/{id?}");
    setupApp.MapFallback(ctx =>
    {
        ctx.Response.Redirect("/Setup");
        return Task.CompletedTask;
    });

    setupApp.Run();
    return;
}

// ── PRODUCTION MODE ───────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

builder.Services.AddFlexCms(new FlexCmsOptions
{
    AppDataPath = appDataPath,
    UseMySQL = builder.Configuration.GetValue<bool>("FlexCms:UseMySQL"),
    MySqlConnectionString = builder.Configuration.GetConnectionString("MySQL") ?? string.Empty,
    UseMsSql = builder.Configuration.GetValue<bool>("FlexCms:UseMsSql"),
    MsSqlConnectionString = builder.Configuration.GetConnectionString("MsSQL") ?? string.Empty,
    UsePostgreSQL = builder.Configuration.GetValue<bool>("FlexCms:UsePostgreSQL"),
    PostgreSqlConnectionString = builder.Configuration.GetConnectionString("PostgreSQL") ?? string.Empty,
    UseMongoDB = builder.Configuration.GetValue<bool>("FlexCms:UseMongoDB"),
    MongoConnectionString = builder.Configuration.GetConnectionString("MongoDB") ?? "mongodb://localhost:27017",
    MongoDatabaseName = builder.Configuration.GetValue<string>("FlexCms:MongoDatabaseName") ?? "flexcms",
    TimeZoneId = builder.Configuration.GetValue<string>("FlexCms:TimeZoneId") ?? "Asia/Dhaka",
    EnforceIpFilter = builder.Configuration.GetValue<bool>("FlexCms:EnforceIpFilter"),
    AllowedIps = builder.Configuration.GetSection("FlexCms:AllowedIps").Get<string[]>() ?? []
});

var app = builder.Build();

app.UseMiddleware<FcmsExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<IpFilterMiddleware>();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ForcePasswordChangeMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// Needed for WebApplicationFactory in integration tests
public partial class Program { }
