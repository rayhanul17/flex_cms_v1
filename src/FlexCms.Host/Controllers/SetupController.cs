using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Setup;
using FlexCms.Host.Models.Setup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace FlexCms.Host.Controllers;

public class SetupController : Controller
{
    private readonly SetupHelper _setup;
    private readonly IHostApplicationLifetime _lifetime;

    private const string S1 = "setup_step1";
    private const string S2 = "setup_step2";

    public SetupController(SetupHelper setup, IHostApplicationLifetime lifetime)
    {
        _setup = setup;
        _lifetime = lifetime;
    }

    // ── Step 1 — Database ─────────────────────────────────────────────────────

    [HttpGet("/Setup")]
    public IActionResult Index() => View(new SetupStep1ViewModel());

    [HttpPost("/Setup/Step1")]
    [ValidateAntiForgeryToken]
    public IActionResult Step1Post(SetupStep1ViewModel model)
    {
        if (!ModelState.IsValid) return View("Index", model);
        HttpContext.Session.SetString(S1, System.Text.Json.JsonSerializer.Serialize(model));
        return RedirectToAction(nameof(Step2));
    }

    // ── Test Connection (AJAX) ────────────────────────────────────────────────

    [HttpPost("/Setup/TestConnection")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection([FromBody] SetupStep1ViewModel model, CancellationToken ct)
    {
        try
        {
            if (model.DbProvider == "mongodb")
            {
                var connStr = model.MongoConnectionString ?? "mongodb://localhost:27017";
                using var client = new MongoDB.Driver.MongoClient(connStr);
                var db = client.GetDatabase(model.MongoDatabase ?? "flexcms");
                await db.RunCommandAsync<MongoDB.Bson.BsonDocument>(
                    new MongoDB.Bson.BsonDocument("ping", 1), cancellationToken: ct);
            }
            else
            {
                var optBuilder = new DbContextOptionsBuilder<FcmsDbContext>();
                ConfigureRelationalProvider(optBuilder, model);
                await using var ctx = new FcmsDbContext(optBuilder.Options);
                if (!await ctx.Database.CanConnectAsync(ct))
                    return Ok(new { ok = false, error = "Cannot connect to the database server." });
            }

            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, error = ex.Message });
        }
    }

    // ── Step 2 — Site Info ────────────────────────────────────────────────────

    [HttpGet("/Setup/Step2")]
    public IActionResult Step2()
    {
        if (HttpContext.Session.GetString(S1) is null)
            return RedirectToAction(nameof(Index));
        return View(new SetupStep2ViewModel());
    }

    [HttpPost("/Setup/Step2")]
    [ValidateAntiForgeryToken]
    public IActionResult Step2Post(SetupStep2ViewModel model)
    {
        if (!ModelState.IsValid) return View("Step2", model);
        HttpContext.Session.SetString(S2, System.Text.Json.JsonSerializer.Serialize(model));
        return RedirectToAction(nameof(Step3));
    }

    // ── Step 3 — Admin Account ────────────────────────────────────────────────

    [HttpGet("/Setup/Step3")]
    public IActionResult Step3()
    {
        if (HttpContext.Session.GetString(S2) is null)
            return RedirectToAction(nameof(Step2));
        return View(new SetupStep3ViewModel());
    }

    [HttpPost("/Setup/Step3")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Step3Post(SetupStep3ViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Step3", model);

        var pwd = model.Password;
        if (pwd.Length < 8 || !pwd.Any(char.IsUpper) || !pwd.Any(char.IsLower) ||
            !pwd.Any(char.IsDigit) || !pwd.Any(c => !char.IsLetterOrDigit(c)))
        {
            ModelState.AddModelError(nameof(model.Password),
                "Password must be 8+ characters with uppercase, lowercase, digit, and special character.");
            return View("Step3", model);
        }

        var step1 = System.Text.Json.JsonSerializer.Deserialize<SetupStep1ViewModel>(
            HttpContext.Session.GetString(S1)!)!;
        var step2 = System.Text.Json.JsonSerializer.Deserialize<SetupStep2ViewModel>(
            HttpContext.Session.GetString(S2)!)!;

        var config = BuildSetupConfig(step1, step2, model);

        // Run DB migration before writing setup.json (so tables exist on restart)
        try
        {
            await MigrateDatabaseAsync(config, ct);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Database setup failed: {ex.Message}");
            return View("Step3", model);
        }

        _setup.Write(config);   // encrypts passwords + persists setup.json
        return RedirectToAction(nameof(Complete));
    }

    // ── Step 4 — Complete ─────────────────────────────────────────────────────

    [HttpGet("/Setup/Complete")]
    public IActionResult Complete() => View();

    [HttpPost("/Setup/Restart")]
    [ValidateAntiForgeryToken]
    public IActionResult Restart()
    {
        Response.OnCompleted(() =>
        {
            _lifetime.StopApplication();
            return Task.CompletedTask;
        });
        return Ok();
    }

    // ── DB Migration ──────────────────────────────────────────────────────────

    private static async Task MigrateDatabaseAsync(SetupConfig config, CancellationToken ct)
    {
        if (config.DbProvider == "mongodb")
        {
            // MongoDB: just verify the connection; collections are created on first write
            using var client = new MongoDB.Driver.MongoClient(config.MongoConnectionString);
            await client.GetDatabase(config.MongoDatabase ?? "flexcms")
                .RunCommandAsync<MongoDB.Bson.BsonDocument>(
                    new MongoDB.Bson.BsonDocument("ping", 1), cancellationToken: ct);
            return;
        }

        var optBuilder = new DbContextOptionsBuilder<FcmsDbContext>();
        switch (config.DbProvider)
        {
            case "mysql":
                optBuilder.UseMySql(config.DbConnectionString,
                    Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(config.DbConnectionString),
                    o => o.CommandTimeout(60));
                break;
            case "mssql":
                optBuilder.UseSqlServer(config.DbConnectionString,
                    o => o.CommandTimeout(60));
                break;
            case "postgresql":
                optBuilder.UseNpgsql(config.DbConnectionString,
                    o => o.CommandTimeout(60));
                break;
        }

        await using var ctx = new FcmsDbContext(optBuilder.Options);

        // EnsureCreatedAsync is a no-op when the database already exists, so
        // an empty pre-existing DB (e.g. from a failed earlier setup) would
        // skip table creation. Detect that and rebuild from scratch.
        var created = await ctx.Database.EnsureCreatedAsync(ct);
        if (created) return;

        bool hasSchema;
        try
        {
            await ctx.Roles.AnyAsync(ct);
            hasSchema = true;
        }
        catch
        {
            hasSchema = false;
        }

        if (!hasSchema)
        {
            await ctx.Database.EnsureDeletedAsync(ct);
            await ctx.Database.EnsureCreatedAsync(ct);
        }
    }

    // ── Connection string builders ────────────────────────────────────────────

    private static string BuildMySqlConnectionString(SetupStep1ViewModel m)
        => $"Server={m.MySqlHost};Port={m.MySqlPort};Database={m.MySqlDatabase};" +
           $"User={m.MySqlUsername};Password={m.MySqlPassword};";

    private static string BuildMsSqlConnectionString(SetupStep1ViewModel m)
        => $"Server={m.MsSqlHost},{m.MsSqlPort};Database={m.MsSqlDatabase};" +
           $"User Id={m.MsSqlUsername};Password={m.MsSqlPassword};TrustServerCertificate=True;";

    private static string BuildPostgreSqlConnectionString(SetupStep1ViewModel m)
        => $"Host={m.PgHost};Port={m.PgPort};Database={m.PgDatabase};" +
           $"Username={m.PgUsername};Password={m.PgPassword};";

    private static void ConfigureRelationalProvider(
        DbContextOptionsBuilder<FcmsDbContext> builder,
        SetupStep1ViewModel model)
    {
        switch (model.DbProvider)
        {
            case "mysql":
                var cs = BuildMySqlConnectionString(model);
                builder.UseMySql(cs,
                    Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(cs),
                    o => o.CommandTimeout(10));
                break;
            case "mssql":
                builder.UseSqlServer(BuildMsSqlConnectionString(model),
                    o => o.CommandTimeout(10));
                break;
            case "postgresql":
                builder.UseNpgsql(BuildPostgreSqlConnectionString(model),
                    o => o.CommandTimeout(10));
                break;
        }
    }

    // ── Setup config builder ──────────────────────────────────────────────────

    private SetupConfig BuildSetupConfig(
        SetupStep1ViewModel s1,
        SetupStep2ViewModel s2,
        SetupStep3ViewModel s3)
    {
        var config = new SetupConfig
        {
            IsSetupComplete = true,
            DbProvider = s1.DbProvider,
            SiteName = s2.SiteName,
            SiteTagline = s2.Tagline ?? "",
            SiteBaseUrl = s2.BaseUrl ?? "",
            DefaultLanguage = s2.DefaultLanguage,
            TimeZoneId = s2.TimeZoneId,
            AdminEmail = s3.Email,
            AdminDisplayName = s3.DisplayName,
            AdminPasswordEncrypted = s3.Password,
            AdminSeeded = false,
            SetupVersion = "1.0",
            SetupCompletedAt = DateTime.UtcNow
        };

        switch (s1.DbProvider)
        {
            case "mysql":
                config.DbConnectionString = BuildMySqlConnectionString(s1);
                config.DbPasswordEncrypted = s1.MySqlPassword ?? "";
                break;
            case "mssql":
                config.DbConnectionString = BuildMsSqlConnectionString(s1);
                config.DbPasswordEncrypted = s1.MsSqlPassword ?? "";
                break;
            case "postgresql":
                config.DbConnectionString = BuildPostgreSqlConnectionString(s1);
                config.DbPasswordEncrypted = s1.PgPassword ?? "";
                break;
            case "mongodb":
                config.MongoConnectionString = s1.MongoConnectionString ?? "mongodb://localhost:27017";
                config.MongoDatabase = s1.MongoDatabase ?? "flexcms";
                break;
        }

        return config;
    }
}
