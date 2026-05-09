using FlexCms.Framework.Caching;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace FlexCms.Tests.Integration.Phase6;

/// <summary>
/// Verifies SettingsService caching behaviour: cache hit avoids DB,
/// SaveAsync invalidates the cache so the next Get re-reads from DB.
/// Uses a real EF InMemory DB and a real FcmsGroupCacheService.
/// </summary>
public class SettingsServiceCachingTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly FcmsGroupCacheService _cache;
    private readonly SettingsService _svc;

    public SettingsServiceCachingTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _cache = new FcmsGroupCacheService(new MemoryCache(new MemoryCacheOptions()));
#pragma warning restore CA2000
        _svc = new SettingsService(new EfRepository<FcmsSettings>(_db), new EfUnitOfWork(_db), _cache);
    }

    public void Dispose() => _db.Dispose();

    private sealed class Cfg { public string Name { get; set; } = ""; public int Count { get; set; } }

    // ── Cache warm-up ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_caches_result_after_first_db_hit()
    {
        await _svc.SaveAsync("cfg", new Cfg { Name = "initial", Count = 1 });

        // First call → DB hit, populates cache
        var first = await _svc.GetAsync<Cfg>("cfg");

        // Manually corrupt the DB row to prove second call comes from cache
        var row = await _db.Set<FcmsSettings>().FirstAsync(s => s.Key == "cfg");
        row.Value = """{"Name":"tampered","Count":99}""";
        // Do NOT save through UoW — bypass service layer to leave cache intact
        await _db.SaveChangesAsync();

        var second = await _svc.GetAsync<Cfg>("cfg");

        Assert.Equal("initial", first.Name);
        Assert.Equal("initial", second.Name);  // cache hit — stale DB ignored
    }

    // ── Cache invalidation on save ─────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_invalidates_cache_so_next_get_reads_from_db()
    {
        await _svc.SaveAsync("cfg", new Cfg { Name = "v1" });
        _ = await _svc.GetAsync<Cfg>("cfg");       // prime the cache

        await _svc.SaveAsync("cfg", new Cfg { Name = "v2" });  // must bust cache

        var result = await _svc.GetAsync<Cfg>("cfg");
        Assert.Equal("v2", result.Name);
    }

    [Fact]
    public async Task SaveAsync_only_invalidates_its_own_key()
    {
        await _svc.SaveAsync("cfg-a", new Cfg { Name = "alpha" });
        await _svc.SaveAsync("cfg-b", new Cfg { Name = "beta" });

        // Prime both keys in cache
        _ = await _svc.GetAsync<Cfg>("cfg-a");
        _ = await _svc.GetAsync<Cfg>("cfg-b");

        // Save changes only "cfg-a"
        await _svc.SaveAsync("cfg-a", new Cfg { Name = "alpha-updated" });

        var a = await _svc.GetAsync<Cfg>("cfg-a");
        var b = await _svc.GetAsync<Cfg>("cfg-b");

        Assert.Equal("alpha-updated", a.Name);
        Assert.Equal("beta", b.Name);
    }

    // ── Cache miss after manual group invalidation ─────────────────────────────

    [Fact]
    public async Task InvalidateGroup_forces_db_reload_on_next_get()
    {
        await _svc.SaveAsync("cfg", new Cfg { Name = "cached" });
        _ = await _svc.GetAsync<Cfg>("cfg");      // prime cache

        // Simulate "System → Clear All Cache"
        _cache.InvalidateAll();

        // Manually update the DB row (simulating external change)
        var row = await _db.Set<FcmsSettings>().FirstAsync(s => s.Key == "cfg");
        row.Value = """{"Name":"from-db-after-clear","Count":0}""";
        await _db.SaveChangesAsync();

        var result = await _svc.GetAsync<Cfg>("cfg");
        Assert.Equal("from-db-after-clear", result.Name);
    }

    // ── Default value on missing key ──────────────────────────────────────────

    [Fact]
    public async Task GetAsync_returns_default_and_does_not_crash_when_key_absent()
    {
        var result = await _svc.GetAsync<Cfg>("no-such-key");
        Assert.NotNull(result);
        Assert.Equal("", result.Name);
        Assert.Equal(0, result.Count);
    }

    // ── Multiple saves produce single DB row ──────────────────────────────────

    [Fact]
    public async Task Repeated_saves_upsert_do_not_create_duplicate_rows()
    {
        await _svc.SaveAsync("cfg", new Cfg { Name = "v1" });
        await _svc.SaveAsync("cfg", new Cfg { Name = "v2" });
        await _svc.SaveAsync("cfg", new Cfg { Name = "v3" });

        var count = await _db.Set<FcmsSettings>().CountAsync(s => s.Key == "cfg");
        Assert.Equal(1, count);

        var result = await _svc.GetAsync<Cfg>("cfg");
        Assert.Equal("v3", result.Name);
    }
}
