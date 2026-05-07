using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace FlexCms.Framework.OutputCache;

/// <summary>
/// Default <see cref="IFcmsOutputCache"/> impl backed by <see cref="IMemoryCache"/>.
/// Suits single-instance deployments (the FlexCMS deployment model). Multi-
/// node deployments should swap in a Redis-backed impl with shared eviction.
///
/// <para>
/// Tag → keys mapping is held in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// alongside the cache entries. When an entry expires naturally the tag map
/// holds a stale key — that's fine because <see cref="EvictAsync"/> on a
/// missing key is a no-op; we trim opportunistically on the next eviction.
/// </para>
/// </summary>
public sealed class FcmsMemoryOutputCache : IFcmsOutputCache
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, HashSet<string>> _tagIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _tagLock = new();

    public FcmsMemoryOutputCache(IMemoryCache cache) => _cache = cache;

    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, IEnumerable<string>? tags = null, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out T? hit) && hit is not null) return hit;

        var value = await factory(ct);
        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };

        // When the entry expires (naturally OR via eviction), drop the key
        // from each tag's set so subsequent EvictByTag scans stay tight.
        if (tags is not null)
        {
            var tagList = tags.ToArray();
            options.RegisterPostEvictionCallback((k, _, _, _) =>
            {
                lock (_tagLock)
                {
                    foreach (var t in tagList)
                        if (_tagIndex.TryGetValue(t, out var set))
                            set.Remove(k.ToString()!);
                }
            });
            lock (_tagLock)
            {
                foreach (var t in tagList)
                {
                    var set = _tagIndex.GetOrAdd(t, _ => new HashSet<string>(StringComparer.Ordinal));
                    set.Add(key);
                }
            }
        }

        _cache.Set(key, value, options);
        return value;
    }

    public Task EvictAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task EvictByTagAsync(string tag, CancellationToken ct = default)
    {
        string[] keys;
        lock (_tagLock)
        {
            if (!_tagIndex.TryGetValue(tag, out var set)) return Task.CompletedTask;
            keys = set.ToArray();
            // Clear the tag's set right away so concurrent reads don't re-evict.
            set.Clear();
        }
        foreach (var k in keys) _cache.Remove(k);
        return Task.CompletedTask;
    }
}
