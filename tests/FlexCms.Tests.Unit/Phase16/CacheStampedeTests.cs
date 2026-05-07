using FlexCms.Framework.Caching;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace FlexCms.Tests.Unit.Phase16;

public class CacheStampedeTests
{
#pragma warning disable CA2000
    private static FcmsCacheService Create() => new(new MemoryCache(new MemoryCacheOptions()));
#pragma warning restore CA2000

    [Fact]
    public async Task Concurrent_misses_for_same_key_invoke_factory_once()
    {
        var cache = Create();
        var calls = 0;

        async Task<int> Factory(CancellationToken ct)
        {
            // Hold the factory long enough that all 50 callers pile up on
            // the per-key semaphore — this is the stampede condition.
            Interlocked.Increment(ref calls);
            await Task.Delay(50, ct);
            return 42;
        }

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => cache.GetOrCreateAsync("hot-key", Factory, TimeSpan.FromMinutes(5)))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(42, r));
        Assert.Equal(1, calls);   // factory ran once across 50 concurrent callers
    }

    [Fact]
    public async Task Different_keys_do_not_serialize_each_other()
    {
        var cache = Create();
        var inFlight = 0;
        var maxObserved = 0;

        async Task<int> Slow(int v, CancellationToken ct)
        {
            var current = Interlocked.Increment(ref inFlight);
            // Track the high-water mark — if per-key isolation works, two
            // different keys should run concurrently → maxObserved >= 2.
            int observed;
            do { observed = maxObserved; } while (Interlocked.CompareExchange(ref maxObserved, Math.Max(observed, current), observed) != observed);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref inFlight);
            return v;
        }

        var t1 = cache.GetOrCreateAsync("k1", ct => Slow(1, ct), TimeSpan.FromMinutes(5));
        var t2 = cache.GetOrCreateAsync("k2", ct => Slow(2, ct), TimeSpan.FromMinutes(5));
        await Task.WhenAll(t1, t2);

        Assert.True(maxObserved >= 2, $"Expected concurrent execution but only saw {maxObserved} in flight at peak.");
    }

    [Fact]
    public async Task Throwing_factory_releases_lock_so_next_call_retries()
    {
        var cache = Create();
        var calls = 0;
        async Task<int> Failing(CancellationToken ct)
        {
            await Task.Yield();
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("transient");
        }

        // First call throws.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync("k", Failing, TimeSpan.FromMinutes(5)));

        // Second call MUST be able to retry — if the lock had stayed held
        // this would hang forever (test would time out).
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync("k", Failing, TimeSpan.FromMinutes(5)));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Evict_drops_the_entry()
    {
        var cache = Create();
        var calls = 0;
        await cache.GetOrCreateAsync("k", _ => { Interlocked.Increment(ref calls); return Task.FromResult(1); }, TimeSpan.FromMinutes(5));
        cache.Evict("k");
        await cache.GetOrCreateAsync("k", _ => { Interlocked.Increment(ref calls); return Task.FromResult(1); }, TimeSpan.FromMinutes(5));
        Assert.Equal(2, calls);
    }
}
