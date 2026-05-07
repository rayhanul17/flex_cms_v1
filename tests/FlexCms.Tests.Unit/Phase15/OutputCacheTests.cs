using FlexCms.Framework.OutputCache;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace FlexCms.Tests.Unit.Phase15;

public class OutputCacheTests
{
    // Test-scope MemoryCache — disposal not material in a unit test process.
#pragma warning disable CA2000
    private static FcmsMemoryOutputCache Create() =>
        new(new MemoryCache(new MemoryCacheOptions()));
#pragma warning restore CA2000

    [Fact]
    public async Task GetOrSet_runs_factory_only_on_miss()
    {
        var cache = Create();
        var calls = 0;
        for (var i = 0; i < 5; i++)
        {
            var v = await cache.GetOrSetAsync("k1", _ =>
            {
                calls++;
                return Task.FromResult("hello");
            }, TimeSpan.FromMinutes(1));
            Assert.Equal("hello", v);
        }
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EvictAsync_drops_the_entry()
    {
        var cache = Create();
        var calls = 0;
        await cache.GetOrSetAsync("k1", _ => { calls++; return Task.FromResult(1); }, TimeSpan.FromMinutes(1));
        await cache.EvictAsync("k1");
        await cache.GetOrSetAsync("k1", _ => { calls++; return Task.FromResult(1); }, TimeSpan.FromMinutes(1));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task EvictByTagAsync_drops_every_entry_carrying_that_tag()
    {
        var cache = Create();
        var hits = 0;
        await cache.GetOrSetAsync("post:1", _ => { hits++; return Task.FromResult("a"); }, TimeSpan.FromMinutes(1), tags: ["public-page", "post"]);
        await cache.GetOrSetAsync("post:2", _ => { hits++; return Task.FromResult("b"); }, TimeSpan.FromMinutes(1), tags: ["public-page", "post"]);
        await cache.GetOrSetAsync("admin:1", _ => { hits++; return Task.FromResult("c"); }, TimeSpan.FromMinutes(1), tags: ["admin-page"]);

        // Evicting "public-page" should clear post:1 + post:2 but leave admin:1.
        await cache.EvictByTagAsync("public-page");

        await cache.GetOrSetAsync("post:1", _ => { hits++; return Task.FromResult("a"); }, TimeSpan.FromMinutes(1));
        await cache.GetOrSetAsync("post:2", _ => { hits++; return Task.FromResult("b"); }, TimeSpan.FromMinutes(1));
        await cache.GetOrSetAsync("admin:1", _ => { hits++; return Task.FromResult("c"); }, TimeSpan.FromMinutes(1));

        // hits = 3 (initial 3) + 2 (post:1 + post:2 re-rendered after evict) = 5.
        // admin:1 second call is a hit, no factory invocation.
        Assert.Equal(5, hits);
    }

    [Fact]
    public async Task EvictByTagAsync_unknown_tag_is_a_noop()
    {
        var cache = Create();
        await cache.GetOrSetAsync("k", _ => Task.FromResult(1), TimeSpan.FromMinutes(1));
        await cache.EvictByTagAsync("does-not-exist"); // shouldn't throw
        // Entry still cached.
        var calls = 0;
        await cache.GetOrSetAsync("k", _ => { calls++; return Task.FromResult(1); }, TimeSpan.FromMinutes(1));
        Assert.Equal(0, calls);
    }
}
