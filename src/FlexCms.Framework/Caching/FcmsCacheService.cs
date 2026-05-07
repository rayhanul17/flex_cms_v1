using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace FlexCms.Framework.Caching;

/// <summary>
/// Default impl: <see cref="IMemoryCache"/> for storage + a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> of per-key
/// <see cref="SemaphoreSlim"/>s for stampede protection.
///
/// <para>
/// On miss: take the per-key semaphore. Re-check the cache (some other
/// caller may have populated it while we were waiting). If still missing,
/// invoke the factory + populate the cache. Release the semaphore in a
/// finally block so a throwing factory doesn't leave the lock permanently
/// held — the next caller retries the factory rather than getting stuck.
/// </para>
///
/// <para>
/// Per-key SemaphoreSlim instances are kept indefinitely (one allocation
/// per cache key over the lifetime of the process). Acceptable for the
/// FlexCms keyspace size; for huge keyspaces use a sliding cleanup.
/// </para>
/// </summary>
public sealed class FcmsCacheService : IFcmsCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public FcmsCacheService(IMemoryCache cache) => _cache = cache;

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out T? hit) && hit is not null) return hit;

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Double-check: another waiter may have populated while we slept.
            if (_cache.TryGetValue(key, out hit) && hit is not null) return hit;

            var value = await factory(ct);
            _cache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
            return value;
        }
        finally
        {
            // Always release — a throwing factory must NOT leave the lock
            // held, otherwise every subsequent miss for this key would
            // hang on the semaphore.
            gate.Release();
        }
    }

    public void Evict(string key) => _cache.Remove(key);
}
