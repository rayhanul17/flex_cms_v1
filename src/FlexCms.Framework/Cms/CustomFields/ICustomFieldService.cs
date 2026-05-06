using System.Globalization;
using System.Text.Json;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Cms.CustomFields;

public interface ICustomFieldService
{
    Task<T?> GetAsync<T>(string entityType, Guid entityId, string key, CancellationToken ct = default);
    Task SetAsync<T>(string entityType, Guid entityId, string key, T? value, CancellationToken ct = default);
    Task<List<FcmsContentMeta>> GetAllForAsync(string entityType, Guid entityId, CancellationToken ct = default);
    Task RemoveAsync(string entityType, Guid entityId, string key, CancellationToken ct = default);
}

public sealed class CustomFieldService : ICustomFieldService
{
    private readonly IRepository<FcmsContentMeta> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public CustomFieldService(IRepository<FcmsContentMeta> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<T?> GetAsync<T>(string entityType, Guid entityId, string key, CancellationToken ct = default)
    {
        var row = await _repo.FirstOrDefaultAsync(m => m.EntityType == entityType && m.EntityId == entityId && m.Key == key, ct);
        if (row is null) return default;
        return Deserialize<T>(row.ValueType, row.Value);
    }

    public async Task SetAsync<T>(string entityType, Guid entityId, string key, T? value, CancellationToken ct = default)
    {
        var existing = await _repo.FirstOrDefaultAsync(m => m.EntityType == entityType && m.EntityId == entityId && m.Key == key, ct);
        var (typeTag, str) = Serialize(value);

        if (existing is null)
        {
            await _repo.AddAsync(new FcmsContentMeta
            {
                EntityType = entityType ?? "",
                EntityId = entityId,
                Key = key ?? "",
                ValueType = typeTag,
                Value = str
            }, ct);
        }
        else
        {
            existing.ValueType = typeTag;
            existing.Value = str;
            await _repo.UpdateAsync(existing, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    public Task<List<FcmsContentMeta>> GetAllForAsync(string entityType, Guid entityId, CancellationToken ct = default)
        => _repo.FindAsync(m => m.EntityType == entityType && m.EntityId == entityId, ct);

    public async Task RemoveAsync(string entityType, Guid entityId, string key, CancellationToken ct = default)
    {
        var row = await _repo.FirstOrDefaultAsync(m => m.EntityType == entityType && m.EntityId == entityId && m.Key == key, ct);
        if (row is null) return;
        await _repo.DeleteAsync(row, ct);   // hard delete — meta has no soft-delete value
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Serializer mirrors the deserializer: keep the type tag in sync if you add a new branch.</summary>
    private static (string TypeTag, string Value) Serialize<T>(T? value)
    {
        if (value is null) return ("string", "");
        return value switch
        {
            string s => ("string", s),
            int i => ("int", i.ToString(CultureInfo.InvariantCulture)),
            long l => ("int", l.ToString(CultureInfo.InvariantCulture)),
            decimal d => ("decimal", d.ToString(CultureInfo.InvariantCulture)),
            double d => ("decimal", d.ToString(CultureInfo.InvariantCulture)),
            bool b => ("bool", b ? "true" : "false"),
            DateTime dt => ("datetime", dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            _ => ("json", JsonSerializer.Serialize(value))
        };
    }

    private static T? Deserialize<T>(string typeTag, string value)
    {
        if (string.IsNullOrEmpty(value)) return default;
        try
        {
            object? boxed = (typeTag, typeof(T)) switch
            {
                ("string", _) when typeof(T) == typeof(string) => value,
                ("int", _) when typeof(T) == typeof(int) || typeof(T) == typeof(int?) => int.Parse(value, CultureInfo.InvariantCulture),
                ("int", _) when typeof(T) == typeof(long) || typeof(T) == typeof(long?) => long.Parse(value, CultureInfo.InvariantCulture),
                ("decimal", _) when typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?) => decimal.Parse(value, CultureInfo.InvariantCulture),
                ("decimal", _) when typeof(T) == typeof(double) || typeof(T) == typeof(double?) => double.Parse(value, CultureInfo.InvariantCulture),
                ("bool", _) when typeof(T) == typeof(bool) || typeof(T) == typeof(bool?) => bool.Parse(value),
                ("datetime", _) when typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTime?) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ("json", _) => JsonSerializer.Deserialize<T>(value),
                _ => null
            };
            return boxed is null ? default : (T)boxed;
        }
        catch
        {
            return default;
        }
    }
}
