using FlexCms.Framework.Caching;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FlexCms.Tests.Integration.Phase5;

public class SettingsServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly SettingsService _svc;

    public SettingsServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new SettingsService(new EfRepository<FlexCms.Framework.Db.FcmsSettings>(_db), new EfUnitOfWork(_db),
            new FcmsGroupCacheService(new MemoryCache(new MemoryCacheOptions())));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    private sealed class TestSettings
    {
        public string SiteName { get; set; } = "";
        public int PostsPerPage { get; set; } = 10;
        public bool EnableSearch { get; set; }
    }

    [Fact]
    public async Task GetAsync_returns_default_when_key_missing()
    {
        var result = await _svc.GetAsync<TestSettings>("site");
        Assert.NotNull(result);
        Assert.Equal("", result.SiteName);
        Assert.Equal(10, result.PostsPerPage);
    }

    [Fact]
    public async Task SaveAsync_then_GetAsync_roundtrips_values()
    {
        var settings = new TestSettings { SiteName = "My CMS", PostsPerPage = 20, EnableSearch = true };
        await _svc.SaveAsync("site", settings);

        var loaded = await _svc.GetAsync<TestSettings>("site");

        Assert.Equal("My CMS", loaded.SiteName);
        Assert.Equal(20, loaded.PostsPerPage);
        Assert.True(loaded.EnableSearch);
    }

    [Fact]
    public async Task SaveAsync_updates_existing_key()
    {
        await _svc.SaveAsync("site", new TestSettings { SiteName = "Old" });
        await _svc.SaveAsync("site", new TestSettings { SiteName = "New" });

        var loaded = await _svc.GetAsync<TestSettings>("site");
        Assert.Equal("New", loaded.SiteName);

        // Only one row in DB
        Assert.Equal(1, await _db.Set<FlexCms.Framework.Db.FcmsSettings>().CountAsync(s => s.Key == "site"));
    }

    [Fact]
    public async Task SaveAsync_different_keys_stored_independently()
    {
        await _svc.SaveAsync("site", new TestSettings { SiteName = "CMS" });
        await _svc.SaveAsync("other", new TestSettings { SiteName = "Other" });

        var site = await _svc.GetAsync<TestSettings>("site");
        var other = await _svc.GetAsync<TestSettings>("other");

        Assert.Equal("CMS", site.SiteName);
        Assert.Equal("Other", other.SiteName);
    }
}
