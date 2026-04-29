using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace FlexCms.Framework.Db.MongoDb;

public class MongoRepository<T> : IRepository<T> where T : BaseMongoEntity
{
    protected readonly IMongoCollection<T> _collection;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    private static readonly FilterDefinitionBuilder<T> Filter = Builders<T>.Filter;

    public MongoRepository(IMongoDatabase database, IHttpContextAccessor? httpContextAccessor = null)
    {
        var collectionName = typeof(T).Name.ToLowerInvariant() + "s";
        _collection = database.GetCollection<T>(collectionName);
        _httpContextAccessor = httpContextAccessor;
    }

    private Guid? CurrentUserId()
    {
        var claim = _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    // All queries auto-prepend IsDeleted=false (B3 fix)
    private FilterDefinition<T> NotDeleted => Filter.Eq(e => e.IsDeleted, false);

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _collection.FindAsync(
            Filter.And(NotDeleted, Filter.Eq(e => e.Id, id)), cancellationToken: ct);
        return await result.FirstOrDefaultAsync(ct);
    }

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
    {
        var result = await _collection.FindAsync(NotDeleted, cancellationToken: ct);
        return await result.ToListAsync(ct);
    }

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        var result = await _collection.FindAsync(
            Filter.And(NotDeleted, Filter.Where(predicate)), cancellationToken: ct);
        return await result.ToListAsync(ct);
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        var result = await _collection.FindAsync(
            Filter.And(NotDeleted, Filter.Where(predicate)), cancellationToken: ct);
        return await result.FirstOrDefaultAsync(ct);
    }

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        var count = await _collection.CountDocumentsAsync(
            Filter.And(NotDeleted, Filter.Where(predicate)), cancellationToken: ct);
        return count > 0;
    }

    public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var filter = predicate is null ? NotDeleted : Filter.And(NotDeleted, Filter.Where(predicate));
        return await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        var userId = CurrentUserId();
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.CreatedBy ??= userId;
        entity.UpdatedBy = userId;
        await _collection.InsertOneAsync(entity, cancellationToken: ct);
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        var userId = CurrentUserId();
        var list = entities.ToList();
        foreach (var e in list) { e.CreatedAt = now; e.UpdatedAt = now; e.CreatedBy ??= userId; e.UpdatedBy = userId; }
        await _collection.InsertManyAsync(list, cancellationToken: ct);
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = FcmsTime.Now;
        entity.UpdatedBy = CurrentUserId();
        await _collection.ReplaceOneAsync(
            Filter.Eq(e => e.Id, entity.Id), entity, cancellationToken: ct);
    }

    public async Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(Filter.Eq(e => e.Id, entity.Id), ct);
    }

    public async Task SoftDeleteAsync(T entity, CancellationToken ct = default)
    {
        entity.IsDeleted = true;
        entity.UpdatedAt = FcmsTime.Now;
        entity.UpdatedBy = CurrentUserId();
        await _collection.ReplaceOneAsync(
            Filter.Eq(e => e.Id, entity.Id), entity, cancellationToken: ct);
    }

    public async Task<PagedResponse<T>> FindPagedAsync(
        Expression<Func<T, bool>>? predicate,
        Expression<Func<T, object>> orderBy,
        int page,
        int pageSize,
        bool descending = false,
        CancellationToken ct = default)
    {
        var filter = predicate is null
            ? NotDeleted
            : Filter.And(NotDeleted, Filter.Where(predicate));

        var total = (int)await _collection.CountDocumentsAsync(filter, cancellationToken: ct);

        var sortDef = descending
            ? Builders<T>.Sort.Descending(orderBy)
            : Builders<T>.Sort.Ascending(orderBy);

        var items = await _collection
            .Find(filter)
            .Sort(sortDef)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return PagedResponse<T>.Create(items, total, page, pageSize);
    }

    // --- New: Batch fetch ---

    public async Task<List<T>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        var result = await _collection.FindAsync(
            Filter.And(NotDeleted, Filter.In(e => e.Id, idList)), cancellationToken: ct);
        return await result.ToListAsync(ct);
    }

    // --- New: Bulk write ---

    public async Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        var userId = CurrentUserId();
        var models = entities.Select(e =>
        {
            e.UpdatedAt = now;
            e.UpdatedBy = userId;
            return new ReplaceOneModel<T>(Filter.Eq(x => x.Id, e.Id), e);
        }).ToList<WriteModel<T>>();

        if (models.Count > 0)
            await _collection.BulkWriteAsync(models, cancellationToken: ct);
    }

    public async Task SoftDeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        var userId = CurrentUserId();
        var models = entities.Select(e =>
        {
            e.IsDeleted = true;
            e.UpdatedAt = now;
            e.UpdatedBy = userId;
            return new ReplaceOneModel<T>(Filter.Eq(x => x.Id, e.Id), e);
        }).ToList<WriteModel<T>>();

        if (models.Count > 0)
            await _collection.BulkWriteAsync(models, cancellationToken: ct);
    }

    // --- New: QueryFilter overloads ---

    public async Task<List<T>> FindAsync(QueryFilter<T> filter, CancellationToken ct = default)
    {
        var combined = NotDeleted;
        foreach (var cond in filter.Conditions)
            combined = Filter.And(combined, Filter.Where(cond));

        var findOptions = new FindOptions<T>();

        if (filter.OrderByExpr != null)
            findOptions.Sort = filter.IsDescending
                ? Builders<T>.Sort.Descending(filter.OrderByExpr)
                : Builders<T>.Sort.Ascending(filter.OrderByExpr);

        if (filter.IsPaged)
        {
            findOptions.Skip = (filter.PageNumber!.Value - 1) * filter.PageSize!.Value;
            findOptions.Limit = filter.PageSize.Value;
        }

        var cursor = await _collection.FindAsync(combined, findOptions, ct);
        return await cursor.ToListAsync(ct);
    }

    public async Task<PagedResponse<T>> FindPagedAsync(QueryFilter<T> filter, CancellationToken ct = default)
    {
        var combined = NotDeleted;
        foreach (var cond in filter.Conditions)
            combined = Filter.And(combined, Filter.Where(cond));

        var total = (int)await _collection.CountDocumentsAsync(combined, cancellationToken: ct);

        var findOptions = new FindOptions<T>();

        if (filter.OrderByExpr != null)
            findOptions.Sort = filter.IsDescending
                ? Builders<T>.Sort.Descending(filter.OrderByExpr)
                : Builders<T>.Sort.Ascending(filter.OrderByExpr);
        else
            findOptions.Sort = Builders<T>.Sort.Descending(e => e.CreatedAt);

        var page = filter.PageNumber ?? 1;
        var pageSize = filter.PageSize ?? total;

        findOptions.Skip = (page - 1) * pageSize;
        findOptions.Limit = pageSize > 0 ? pageSize : total;

        var cursor = await _collection.FindAsync(combined, findOptions, ct);
        var items = await cursor.ToListAsync(ct);

        return PagedResponse<T>.Create(items, total, page, pageSize > 0 ? pageSize : total);
    }

    public async Task<List<T>> FindByTextAsync(string searchTerm, CancellationToken ct = default)
    {
        var regex = new BsonRegularExpression(Regex.Escape(searchTerm), "i");

        var stringProps = typeof(T).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p =>
            {
                var attr = p.GetCustomAttribute<BsonElementAttribute>();
                var name = p.Name;
                return attr?.ElementName ?? (name.Length == 1
                    ? char.ToLower(name[0]).ToString()
                    : char.ToLower(name[0]) + name[1..]);
            });

        var orFilters = stringProps
            .Select(field => Builders<T>.Filter.Regex(field, regex))
            .ToList();

        if (!orFilters.Any()) return new List<T>();

        var textFilter = Filter.And(NotDeleted, Filter.Or(orFilters));
        var result = await _collection.FindAsync(textFilter, cancellationToken: ct);
        return await result.ToListAsync(ct);
    }
}
