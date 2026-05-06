using System.Text.Json;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Services;

public class SettingsService : ISettingsService
{
    private readonly IRepository<FcmsSettings> _repo;
    private readonly IFcmsUnitOfWork _uow;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(IRepository<FcmsSettings> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : class, new()
    {
        var row = await _repo.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (row is null || string.IsNullOrEmpty(row.Value))
            return new T();

        return JsonSerializer.Deserialize<T>(row.Value, JsonOpts) ?? new T();
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
    }
}
