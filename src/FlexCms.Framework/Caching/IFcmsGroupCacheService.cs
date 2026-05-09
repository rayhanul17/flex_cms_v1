namespace FlexCms.Framework.Caching;

public interface IFcmsGroupCacheService
{
    T? Get<T>(string group, string key);
    void Set<T>(string group, string key, T value, TimeSpan ttl);
    void Invalidate(string group, string key);
    void InvalidateGroup(string group);
    void InvalidateAll();
}
