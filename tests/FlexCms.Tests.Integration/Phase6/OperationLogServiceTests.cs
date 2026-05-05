using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase6;

/// <summary>
/// Integration tests for OperationLogService using EF InMemory + real SettingsService.
/// EfRepository follows UoW pattern (no auto-save), so tests call _db.SaveChangesAsync()
/// after each service write to mirror what the controller/UoW would do.
/// </summary>
public class OperationLogServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly SettingsService _settings;
    private readonly OperationLogService _svc;

    public OperationLogServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
        _settings = new SettingsService(new EfRepository<FlexCms.Framework.Db.FcmsSettings>(_db), new EfUnitOfWork(_db));

        var context = Substitute.For<IFcmsContextService>();
        context.UserId.Returns((Guid?)Guid.NewGuid());
        context.Username.Returns("testuser");
        context.IpAddress.Returns("127.0.0.1");
        context.Browser.Returns("Chrome");
        context.Os.Returns("Windows");

        var logRepo = new EfRepository<FcmsLog>(_db);
        var archiveRepo = new EfRepository<FcmsLogArchive>(_db);
        _svc = new OperationLogService(logRepo, archiveRepo, context, _settings, new EfUnitOfWork(_db));
    }

    public void Dispose() => _db.Dispose();

    // ── Toggle ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LogAsync_creates_entry_when_enabled()
    {
        await SetAuditEnabledAsync(true);

        await _svc.LogAsync("Post.Created", "FcmsPost", Guid.NewGuid().ToString());
        await Save();

        Assert.Equal(1, await _db.Set<FcmsLog>().CountAsync(l => !l.IsDeleted));
    }

    [Fact]
    public async Task LogAsync_does_nothing_when_disabled()
    {
        await SetAuditEnabledAsync(false);

        await _svc.LogAsync("X.Action", "Entity", "id");
        await Save();

        Assert.Equal(0, await _db.Set<FcmsLog>().CountAsync());
    }

    [Fact]
    public async Task LogAsync_enabled_by_default_when_no_setting_stored()
    {
        // No setting saved — should default to enabled (new T() = Enabled=true)
        await _svc.LogAsync("Default.Check", "Entity", "1");
        await Save();

        Assert.Equal(1, await _db.Set<FcmsLog>().CountAsync(l => !l.IsDeleted));
    }

    // ── LogAsync field correctness ────────────────────────────────────────────

    [Fact]
    public async Task LogAsync_stores_correct_fields()
    {
        await SetAuditEnabledAsync(true);
        var entityId = Guid.NewGuid().ToString();

        await _svc.LogAsync("Post.Created", "FcmsPost", entityId, new { Title = "Test" }, "blog", FcmsLogSeverity.Warning);
        await Save();

        var log = await _db.Set<FcmsLog>().FirstAsync(l => !l.IsDeleted);
        Assert.Equal("Post.Created", log.Action);
        Assert.Equal("FcmsPost", log.EntityType);
        Assert.Equal(entityId, log.EntityId);
        Assert.Equal("blog", log.Module);
        Assert.Equal(FcmsLogSeverity.Warning, log.Severity);
        Assert.NotNull(log.NewValue);
        Assert.Contains("Test", log.NewValue);
    }

    // ── ArchiveOlderThan ──────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveOlderThan_moves_old_logs_to_archive_in_bulk()
    {
        await InsertLogAsync(daysAgo: 2);
        await InsertLogAsync(daysAgo: 3);
        await InsertLogAsync(daysAgo: 0); // recent — should stay

        await _svc.ArchiveOlderThanAsync(TimeSpan.FromHours(24));
        await Save();

        var remaining = await _db.Set<FcmsLog>().CountAsync(l => !l.IsDeleted);
        var archived = await _db.Set<FcmsLogArchive>().CountAsync(a => !a.IsDeleted);
        Assert.Equal(1, remaining);
        Assert.Equal(2, archived);
    }

    [Fact]
    public async Task ArchiveOlderThan_no_old_logs_is_noop()
    {
        await InsertLogAsync(daysAgo: 0);

        await _svc.ArchiveOlderThanAsync(TimeSpan.FromHours(24));
        await Save();

        Assert.Equal(0, await _db.Set<FcmsLogArchive>().CountAsync());
    }

    [Fact]
    public async Task ArchiveOlderThan_copies_all_fields_to_archive()
    {
        var entityId = Guid.NewGuid().ToString();
        var log = await InsertLogAsync(daysAgo: 2, action: "User.Deleted", entityId: entityId);

        await _svc.ArchiveOlderThanAsync(TimeSpan.FromHours(1));
        await Save();

        var archived = await _db.Set<FcmsLogArchive>().FirstAsync(a => !a.IsDeleted);
        Assert.Equal("User.Deleted", archived.Action);
        Assert.Equal(entityId, archived.EntityId);
        Assert.Equal(log.UserName, archived.UserName);
        Assert.Equal(log.Severity, archived.Severity);
        Assert.Equal(log.Module, archived.Module);
    }

    [Fact]
    public async Task ArchiveOlderThan_archived_logs_are_soft_deleted_from_main()
    {
        await InsertLogAsync(daysAgo: 2);

        await _svc.ArchiveOlderThanAsync(TimeSpan.FromHours(1));
        await Save();

        var visibleLogs = await _db.Set<FcmsLog>().CountAsync(l => !l.IsDeleted);
        var rawLogs = await _db.Set<FcmsLog>().IgnoreQueryFilters().CountAsync();
        Assert.Equal(0, visibleLogs);
        Assert.Equal(1, rawLogs); // soft-deleted, physically still there
    }

    // ── GetRecent ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRecentAsync_respects_count_limit()
    {
        for (int i = 0; i < 5; i++)
            await InsertLogAsync(daysAgo: i);

        var recent = await _svc.GetRecentAsync(3);

        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public async Task GetRecentAsync_returns_most_recent_first()
    {
        await InsertLogAsync(daysAgo: 2);
        await InsertLogAsync(daysAgo: 1);
        await InsertLogAsync(daysAgo: 0);

        var recent = await _svc.GetRecentAsync(3);

        Assert.True(recent[0].CreatedAt >= recent[1].CreatedAt);
        Assert.True(recent[1].CreatedAt >= recent[2].CreatedAt);
    }

    // ── GetArchive ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetArchiveAsync_returns_archived_entries()
    {
        await InsertLogAsync(daysAgo: 2);
        await InsertLogAsync(daysAgo: 3);
        await _svc.ArchiveOlderThanAsync(TimeSpan.FromHours(24));
        await Save();

        var archive = await _svc.GetArchiveAsync(100);

        Assert.Equal(2, archive.Count);
    }

    // ── ClearArchive ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearArchiveAsync_soft_deletes_all_archive_entries()
    {
        await InsertLogAsync(daysAgo: 2);
        await InsertLogAsync(daysAgo: 3);
        await _svc.ArchiveOlderThanAsync(TimeSpan.FromHours(24));
        await Save();

        await _svc.ClearArchiveAsync();
        await Save();

        Assert.Equal(0, await _db.Set<FcmsLogArchive>().CountAsync(a => !a.IsDeleted));
        Assert.Equal(2, await _db.Set<FcmsLogArchive>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ClearArchiveAsync_empty_archive_does_not_throw()
    {
        await _svc.ClearArchiveAsync();
        await Save();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task Save() => _db.SaveChangesAsync();

    private async Task SetAuditEnabledAsync(bool enabled)
        => await _settings.SaveAsync(AuditLogSettings.Key, new AuditEnabledDto { Enabled = enabled });

    private async Task<FcmsLog> InsertLogAsync(
        int daysAgo = 0,
        string action = "Test.Action",
        string entityId = "")
    {
        var log = new FcmsLog
        {
            Action = action,
            EntityType = "TestEntity",
            EntityId = string.IsNullOrEmpty(entityId) ? Guid.NewGuid().ToString() : entityId,
            UserName = "testuser",
            UserIp = "127.0.0.1",
            UserAgent = "TestAgent",
            Module = "core",
            Severity = FcmsLogSeverity.Info,
        };
        _db.Set<FcmsLog>().Add(log);
        await _db.SaveChangesAsync();

        // Patch CreatedAt/UpdatedAt after save because SaveChangesAsync overrides them with FcmsTime.Now
        if (daysAgo > 0)
        {
            var past = DateTime.UtcNow.AddDays(-daysAgo);
            log.CreatedAt = past;
            log.UpdatedAt = past;
            _db.Set<FcmsLog>().Update(log);
            await _db.SaveChangesAsync();
        }

        return log;
    }

    private sealed class AuditEnabledDto
    {
        public bool Enabled { get; set; } = true;
    }
}
