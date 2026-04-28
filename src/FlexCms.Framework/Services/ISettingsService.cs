namespace FlexCms.Framework.Services;

public interface ISettingsService
{
    Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : class, new();
    Task SaveAsync<T>(string key, T value, CancellationToken ct = default);
}
