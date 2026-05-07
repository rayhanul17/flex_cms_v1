namespace FlexCms.Framework.Caching;

/// <summary>
/// Per-key cache primitive with stampede protection. When N concurrent
/// requests miss the cache for the same key, only ONE invokes the factory;
/// the rest wait on a per-key semaphore and read the populated cache after
/// the first finishes.
///
/// <para>
/// Distinct from <see cref="OutputCache.IFcmsOutputCache"/>:
/// </para>
/// <list type="bullet">
///   <item>Output cache is for HTML fragments / rendered responses with tag-based invalidation by editorial action.</item>
///   <item>This cache is for hot DB reads (permissions, menu, redirects, settings) where the cost of a thundering herd on miss is real (1000 concurrent → 1000 DB queries).</item>
/// </list>
///
/// <para>
/// Refactor candidates inside the framework: <c>PermissionService</c>,
/// <c>MenuService</c>, <c>RedirectService</c>, settings reads. Behavior
/// matches NetCoreCMS / standard "lock-once-on-miss" pattern.
/// </para>
/// </summary>
public interface IFcmsCacheService
{
    /// <summary>
    /// Get or atomically populate. The <paramref name="factory"/> runs at
    /// most once per (key, miss); concurrent callers for the same key wait
    /// on a per-key semaphore.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Manual eviction.</summary>
    void Evict(string key);
}
