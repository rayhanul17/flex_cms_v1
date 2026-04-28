using System.Text.Json;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Services;

public class SettingsService : ISettingsService
{
    private readonly FcmsDbContext _db;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(FcmsDbContext db) => _db = db;

    public async Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : class, new()
    {
        var row = await _db.Set<FcmsSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);

        if (row is null || string.IsNullOrEmpty(row.Value))
            return new T();

        return JsonSerializer.Deserialize<T>(row.Value, JsonOpts) ?? new T();
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);

        var row = await _db.Set<FcmsSettings>()
            .FirstOrDefaultAsync(s => s.Key == key, ct);

        if (row is null)
        {
            _db.Set<FcmsSettings>().Add(new FcmsSettings { Key = key, Value = json });
        }
        else
        {
            row.Value = json;
            _db.Set<FcmsSettings>().Update(row);
        }

        await _db.SaveChangesAsync(ct);
    }
}
