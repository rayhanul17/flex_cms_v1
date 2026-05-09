using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace FlexCms.Framework.Caching;

public sealed class FcmsGroupCacheService : IFcmsGroupCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _groups = new();

    public FcmsGroupCacheService(IMemoryCache cache) => _cache = cache;

    public T? Get<T>(string group, string key)
        => _cache.TryGetValue(CacheKey(group, key), out T? val) ? val : default;

    public void Set<T>(string group, string key, T value, TimeSpan ttl)
    {
        var ck = CacheKey(group, key);
        _cache.Set(ck, value, ttl);
        _groups.GetOrAdd(group, _ => new ConcurrentBag<string>()).Add(ck);
    }

    public void Invalidate(string group, string key) => _cache.Remove(CacheKey(group, key));

    public void InvalidateGroup(string group)
    {
        if (!_groups.TryRemove(group, out var keys)) return;
        foreach (var k in keys) _cache.Remove(k);
    }

    public void InvalidateAll()
    {
        foreach (var group in _groups.Keys.ToList()) InvalidateGroup(group);
    }

    private static string CacheKey(string group, string key) => $"fcms:{group}:{key}";
}
