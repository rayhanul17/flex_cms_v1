namespace FlexCms.Framework.OutputCache;

/// <summary>
/// Lightweight tag-based output-cache facade — the kind of cache used by
/// public CMS pages (anonymous, slow-changing). Wraps ASP.NET Core
/// <c>OutputCache</c> if available; otherwise falls back to <c>IMemoryCache</c>.
///
/// <para>
/// "Tag-based" = the caller writes with one or more semantic tags
/// (e.g. <c>"public-page"</c>, <c>"post:{id}"</c>) and an admin who saves
/// a post calls <see cref="EvictByTagAsync"/> to invalidate every cached
/// entry that mentions the tag — without having to know the cache keys.
/// </para>
/// </summary>
public interface IFcmsOutputCache
{
    /// <summary>Get-or-set with a TTL + tag list. The factory runs only on miss.</summary>
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, IEnumerable<string>? tags = null, CancellationToken ct = default);

    /// <summary>Manual eviction by exact key.</summary>
    Task EvictAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Evict every entry tagged with <paramref name="tag"/>. Use after a
    /// content edit to refresh the public site without restart.
    /// </summary>
    Task EvictByTagAsync(string tag, CancellationToken ct = default);
}
