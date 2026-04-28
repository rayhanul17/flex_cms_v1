using FlexCms.Framework.Extensions;
using FlexCms.Framework.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");

builder.Services.AddFlexCms(new FlexCmsOptions
{
    AppDataPath = appDataPath,
    UseMySQL = builder.Configuration.GetValue<bool>("FlexCms:UseMySQL"),
    MySqlConnectionString = builder.Configuration.GetConnectionString("MySQL") ?? string.Empty,
    UseMongoDB = builder.Configuration.GetValue<bool>("FlexCms:UseMongoDB"),
    MongoConnectionString = builder.Configuration.GetConnectionString("MongoDB") ?? "mongodb://localhost:27017",
    MongoDatabaseName = builder.Configuration.GetValue<string>("FlexCms:MongoDatabaseName") ?? "flexcms",
    EnforceIpFilter = builder.Configuration.GetValue<bool>("FlexCms:EnforceIpFilter"),
    AllowedIps = builder.Configuration.GetSection("FlexCms:AllowedIps").Get<string[]>() ?? []
});

var app = builder.Build();

app.UseMiddleware<FcmsExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<IpFilterMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

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
