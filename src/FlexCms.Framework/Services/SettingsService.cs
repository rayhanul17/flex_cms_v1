using System.Text.Json;
using FlexCms.Framework.Caching;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Services;

public class SettingsService : ISettingsService
{
    private readonly IRepository<FcmsSettings> _repo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IFcmsGroupCacheService _cache;

    private const string Group = "settings";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(IRepository<FcmsSettings> repo, IFcmsUnitOfWork uow, IFcmsGroupCacheService cache)
    {
        _repo = repo;
        _uow = uow;
        _cache = cache;
    }

    public async Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : class, new()
    {
        var cached = _cache.Get<T>(Group, key);
        if (cached is not null) return cached;

        var row = await _repo.FirstOrDefaultAsync(s => s.Key == key, ct);
        var result = (row is null || string.IsNullOrEmpty(row.Value))
            ? new T()
            : JsonSerializer.Deserialize<T>(row.Value, JsonOpts) ?? new T();

        _cache.Set(Group, key, result, Ttl);
        return result;
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);

        var row = await _repo.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (row is null)
            await _repo.AddAsync(new FcmsSettings { Key = key, Value = json }, ct);
        else
        {
            row.Value = json;
            await _repo.UpdateAsync(row, ct);
        }

        await _uow.SaveChangesAsync(ct);
        _cache.Invalidate(Group, key);
    }
}
