using FlexCms.Framework.Caching;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Integration.Phase17;

/// <summary>
/// Integration tests for FcmsAuditInterceptor on a real SQLite DB.
/// EF InMemory does not support interceptors — SQLite in-memory does.
///
/// All components share a single SqliteConnection so schema + data are
/// visible across DbContext instances.
/// </summary>
public sealed class FcmsAuditInterceptorIntegrationTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly FcmsDbContext _db;
    private readonly SettingsService _settings;
    private readonly FcmsAuditInterceptor _interceptor;

    public FcmsAuditInterceptorIntegrationTests()
    {
        // Shared in-memory SQLite connection keeps schema alive across all
        // DbContext instances that share it.
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();

        var userCtx = Substitute.For<IFcmsContextService>();
        userCtx.UserId.Returns((Guid?)Guid.NewGuid());
        userCtx.Username.Returns("testuser");
        userCtx.IpAddress.Returns("127.0.0.1");
        userCtx.Browser.Returns("Chrome");
        userCtx.Os.Returns("Windows");

        // Settings service uses its own DbContext on the same connection.
        // Because they share _conn, all writes are immediately visible to _db.
        var baseOpts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseSqlite(_conn)
            .Options;

#pragma warning disable CA2000
        var settingsDb   = new FcmsDbContext(baseOpts);
        var settingsRepo = new EfRepository<FcmsSettings>(settingsDb);
        var settingsUow  = new EfUnitOfWork(settingsDb);
#pragma warning restore CA2000
        _settings = new SettingsService(settingsRepo, settingsUow,
            new FcmsGroupCacheService(new MemoryCache(new MemoryCacheOptions())));

        _interceptor = new FcmsAuditInterceptor(
            userCtx, _settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FcmsAuditInterceptor>.Instance);

        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(_interceptor)
            .Options;
        _db = new FcmsDbContext(opts);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    // ── Core behaviour ────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_entity_produces_Created_log_row()
    {
        var page = new FcmsPage { Title = "Hello", Slug = "hello" };
        _db.Set<FcmsPage>().Add(page);
        await _db.SaveChangesAsync();

        var logs = await _db.Set<FcmsLog>().ToListAsync();
        Assert.Single(logs);
        Assert.Equal("Page.Created", logs[0].Action);
        Assert.Equal(nameof(FcmsPage), logs[0].EntityType);
        Assert.Equal(page.Id.ToString(), logs[0].EntityId);
    }

    [Fact]
    public async Task Update_entity_produces_Updated_log_row()
    {
        var page = new FcmsPage { Title = "Before", Slug = "before" };
        _db.Set<FcmsPage>().Add(page);
        await _db.SaveChangesAsync();

        page.Title = "After";
        _db.Set<FcmsPage>().Update(page);
        await _db.SaveChangesAsync();

        var logs = await _db.Set<FcmsLog>().OrderBy(l => l.CreatedAt).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Equal("Page.Created", logs[0].Action);
        Assert.Equal("Page.Updated", logs[1].Action);
    }

    [Fact]
    public async Task SoftDelete_entity_produces_Deleted_log_row()
    {
        var page = new FcmsPage { Title = "ToDelete", Slug = "to-delete" };
        _db.Set<FcmsPage>().Add(page);
        await _db.SaveChangesAsync();

        page.Status = EntityStatus.Deleted;
        _db.Set<FcmsPage>().Update(page);
        await _db.SaveChangesAsync();

        var logs = await _db.Set<FcmsLog>()
            .Where(l => l.Action == "Page.Deleted")
            .ToListAsync();
        Assert.Single(logs);
    }

    [Fact]
    public async Task HardDelete_entity_produces_HardDeleted_Warning_log_row()
    {
        var page = new FcmsPage { Title = "Hard", Slug = "hard" };
        _db.Set<FcmsPage>().Add(page);
        await _db.SaveChangesAsync();

        _db.Set<FcmsPage>().Remove(page);
        await _db.SaveChangesAsync();

        var logs = await _db.Set<FcmsLog>()
            .Where(l => l.Action == "Page.HardDeleted")
            .ToListAsync();
        Assert.Single(logs);
        Assert.Equal(FcmsLogSeverity.Warning, logs[0].Severity);
    }

    [Fact]
    public async Task Log_value_contains_full_entity_snapshot()
    {
        var page = new FcmsPage { Title = "Snapshot Test", Slug = "snap" };
        _db.Set<FcmsPage>().Add(page);
        await _db.SaveChangesAsync();

        var log = await _db.Set<FcmsLog>().FirstAsync();
        Assert.NotNull(log.Value);
        Assert.Contains("Snapshot Test", log.Value);
        Assert.Contains("snap", log.Value);
    }

    [Fact]
    public async Task FcmsLog_itself_is_not_auto_logged_no_infinite_loop()
    {
        var page = new FcmsPage { Title = "Once", Slug = "once" };
        _db.Set<FcmsPage>().Add(page);
        await _db.SaveChangesAsync();

        // Should be exactly 1 log row — not 2 (which would mean the FcmsLog
        // insert was itself logged, triggering another FcmsLog, etc.)
        var count = await _db.Set<FcmsLog>().CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Multiple_entities_in_one_save_produce_one_log_row_each()
    {
        _db.Set<FcmsPage>().Add(new FcmsPage { Title = "A", Slug = "a" });
        _db.Set<FcmsPage>().Add(new FcmsPage { Title = "B", Slug = "b" });
        _db.Set<FcmsPage>().Add(new FcmsPage { Title = "C", Slug = "c" });
        await _db.SaveChangesAsync();

        var count = await _db.Set<FcmsLog>().CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Audit_disabled_via_settings_produces_no_log_rows()
    {
        // Write the disabled flag directly through the intercepted DbContext
        // so both the settings write and the subsequent entity write use the
        // same EF identity cache — no stale snapshot across two DbContexts.
        var settingsRepo = new EfRepository<FcmsSettings>(_db);
        var settingsUow  = new EfUnitOfWork(_db);
        var localSettings = new SettingsService(settingsRepo, settingsUow,
            new FcmsGroupCacheService(new MemoryCache(new MemoryCacheOptions())));
        await localSettings.SaveAsync(AuditLogSettings.Key, new AuditEnabledDto { Enabled = false });

        // Clear tracker so the setting row doesn't count as a "pending" entity
        // for the interceptor on the next SaveChanges.
        _db.ChangeTracker.Clear();

        _db.Set<FcmsPage>().Add(new FcmsPage { Title = "Silent", Slug = "silent" });
        await _db.SaveChangesAsync();

        var count = await _db.Set<FcmsLog>().CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Log_row_captures_user_context()
    {
        var page = new FcmsPage { Title = "User", Slug = "user" };
        _db.Set<FcmsPage>().Add(page);
        await _db.SaveChangesAsync();

        var log = await _db.Set<FcmsLog>().FirstAsync();
        Assert.Equal("testuser", log.UserName);
        Assert.Equal("127.0.0.1", log.UserIp);
    }

    private sealed class AuditEnabledDto { public bool Enabled { get; set; } = true; }
}
