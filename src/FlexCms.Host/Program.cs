using FlexCms.Framework.Extensions;

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
    MongoDatabaseName = builder.Configuration.GetValue<string>("FlexCms:MongoDatabaseName") ?? "flexcms"
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// Needed for WebApplicationFactory in integration tests
public partial class Program { }
