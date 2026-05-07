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
using MongoDB.Driver.Linq;

namespace FlexCms.Framework.Db.MongoDb;

public class MongoRepository<T> : IRepository<T>, IMongoSessionAware where T : class, IBaseEntity, new()
{
    protected readonly IMongoCollection<T> _collection;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private IClientSessionHandle? _session;

    private static readonly FilterDefinitionBuilder<T> Filter = Builders<T>.Filter;

    public MongoRepository(IMongoDatabase database, IHttpContextAccessor? httpContextAccessor = null)
    {
        var collectionName = Helpers.FcmsHelper.GetTableName<T>("fcms");
        _collection = database.GetCollection<T>(collectionName);
        _httpContextAccessor = httpContextAccessor;
    }

    void IMongoSessionAware.SetSession(IClientSessionHandle? session) => _session = session;

    /// <summary>
    /// True if <typeparamref name="T"/> declares a <c>RowVersion</c> property
    /// (byte[]). Mirrors EF's <c>IsRowVersion()</c> opt-in: presence of the
    /// property turns on optimistic concurrency on Mongo too. Cached via
    /// static reflection so we don't hit Type.GetProperty per UpdateAsync.
    /// </summary>
    private static readonly PropertyInfo? RowVersionProp =
        typeof(T).GetProperty("RowVersion", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// Audit-log entities (FcmsLog, FcmsLogArchive) opt out of soft-delete
    /// filtering — EF strips the inherited Status column entirely; Mongo
    /// matches by short-circuiting to "match all" so the two stay symmetric.
    /// </summary>
    private static readonly bool IsAppendOnly =
        typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(T));

    private Guid? CurrentUserId()
    {
        var claim = _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    // All queries auto-exclude soft-deleted entities (Status != Deleted),
    // EXCEPT append-only entities (audit logs) — see IAppendOnlyEntity comment.
    private FilterDefinition<T> NotDeleted =>
        IsAppendOnly ? Filter.Empty : Filter.Ne(e => e.Status, EntityStatus.Deleted);

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var f = Filter.And(NotDeleted, Filter.Eq(e => e.Id, id));
        var result = _session is not null
            ? await _collection.FindAsync(_session, f, cancellationToken: ct)
            : await _collection.FindAsync(f, cancellationToken: ct);
        return await result.FirstOrDefaultAsync(ct);
    }

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
    {
        var result = _session is not null
            ? await _collection.FindAsync(_session, NotDeleted, cancellationToken: ct)
            : await _collection.FindAsync(NotDeleted, cancellationToken: ct);
        return await result.ToListAsync(ct);
    }

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false)
    {
        var f = includeDeleted ? Filter.Where(predicate) : Filter.And(NotDeleted, Filter.Where(predicate));
        var result = _session is not null
            ? await _collection.FindAsync(_session, f, cancellationToken: ct)
            : await _collection.FindAsync(f, cancellationToken: ct);
        return await result.ToListAsync(ct);
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false)
    {
        var f = includeDeleted ? Filter.Where(predicate) : Filter.And(NotDeleted, Filter.Where(predicate));
        var result = _session is not null
            ? await _collection.FindAsync(_session, f, cancellationToken: ct)
            : await _collection.FindAsync(f, cancellationToken: ct);
        return await result.FirstOrDefaultAsync(ct);
    }

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        var f = Filter.And(NotDeleted, Filter.Where(predicate));
        var count = _session is not null
            ? await _collection.CountDocumentsAsync(_session, f, cancellationToken: ct)
            : await _collection.CountDocumentsAsync(f, cancellationToken: ct);
        return count > 0;
    }

    public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var f = predicate is null ? NotDeleted : Filter.And(NotDeleted, Filter.Where(predicate));
        return _session is not null
            ? await _collection.CountDocumentsAsync(_session, f, cancellationToken: ct)
            : await _collection.CountDocumentsAsync(f, cancellationToken: ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        var userId = CurrentUserId();
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.CreatedBy ??= userId;
        entity.UpdatedBy = userId;
        if (_session is not null)
            await _collection.InsertOneAsync(_session, entity, cancellationToken: ct);
        else
            await _collection.InsertOneAsync(entity, cancellationToken: ct);
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        var userId = CurrentUserId();
        var list = entities.ToList();
        foreach (var e in list) { e.CreatedAt = now; e.UpdatedAt = now; e.CreatedBy ??= userId; e.UpdatedBy = userId; }
        if (_session is not null)
            await _collection.InsertManyAsync(_session, list, cancellationToken: ct);
        else
            await _collection.InsertManyAsync(list, cancellationToken: ct);
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = FcmsTime.Now;
        entity.UpdatedBy = CurrentUserId();

        // Optimistic concurrency: if T has a RowVersion byte[] (EF Phase 15
        // Issue 96 — FcmsPage / FcmsPost), require it match the stored value
        // OR be null/empty (first save). Then bump it to a fresh value so the
        // NEXT writer of the same row will see a mismatch.
        var f = Filter.Eq(e => e.Id, entity.Id);
        if (RowVersionProp is not null)
        {
            var current = (byte[]?)RowVersionProp.GetValue(entity);
            // Mongo's strict "field equals X bytes" filter — compares element-wise.
            f = current is null || current.Length == 0
                ? Filter.And(f, Filter.Or(
                    Filter.Exists("RowVersion", false),
                    Filter.Eq<byte[]?>("RowVersion", null),
                    Filter.Size("RowVersion", 0)))
                : Filter.And(f, Filter.Eq<byte[]?>("RowVersion", current));
            RowVersionProp.SetValue(entity, NewRowVersion());
        }

        var result = _session is not null
            ? await _collection.ReplaceOneAsync(_session, f, entity, cancellationToken: ct)
            : await _collection.ReplaceOneAsync(f, entity, cancellationToken: ct);

        if (RowVersionProp is not null && result.MatchedCount == 0)
        {
            // Mirrors EF's DbUpdateConcurrencyException — caller is expected
            // to surface "Another editor saved first; refresh" UX. Custom
            // exception type keeps the EF/Mongo signal symmetric without
            // pulling EF Core into the Mongo path.
            throw new FcmsConcurrencyException(
                $"Concurrency conflict: {typeof(T).Name} {entity.Id} was modified by another writer (RowVersion mismatch).");
        }
    }

    private static byte[] NewRowVersion()
    {
        // 8 bytes is what SQL Server's ROWVERSION uses; mirror it for
        // consistency between backends.
        var b = new byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(b);
        return b;
    }

    public async Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        var f = Filter.Eq(e => e.Id, entity.Id);
        if (_session is not null)
            await _collection.DeleteOneAsync(_session, f, cancellationToken: ct);
        else
            await _collection.DeleteOneAsync(f, cancellationToken: ct);
    }

    public async Task SoftDeleteAsync(T entity, CancellationToken ct = default)
    {
        entity.Status = EntityStatus.Deleted;
        entity.DeletedAt ??= FcmsTime.Now;
        entity.UpdatedAt = FcmsTime.Now;
        entity.UpdatedBy = CurrentUserId();
        var f = Filter.Eq(e => e.Id, entity.Id);
        if (_session is not null)
            await _collection.ReplaceOneAsync(_session, f, entity, cancellationToken: ct);
        else
            await _collection.ReplaceOneAsync(f, entity, cancellationToken: ct);
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

        var total = _session is not null
            ? (int)await _collection.CountDocumentsAsync(_session, filter, cancellationToken: ct)
            : (int)await _collection.CountDocumentsAsync(filter, cancellationToken: ct);

        var sortDef = descending
            ? Builders<T>.Sort.Descending(orderBy)
            : Builders<T>.Sort.Ascending(orderBy);

        var findFluent = _session is not null
            ? _collection.Find(_session, filter)
            : _collection.Find(filter);

        var items = await findFluent
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
        var f = Filter.And(NotDeleted, Filter.In(e => e.Id, idList));
        var result = _session is not null
            ? await _collection.FindAsync(_session, f, cancellationToken: ct)
            : await _collection.FindAsync(f, cancellationToken: ct);
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
        {
            if (_session is not null)
                await _collection.BulkWriteAsync(_session, models, cancellationToken: ct);
            else
                await _collection.BulkWriteAsync(models, cancellationToken: ct);
        }
    }

    public async Task DeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var ids = entities.Select(e => e.Id).ToList();
        if (ids.Count == 0) return;
        var f = Filter.In(e => e.Id, ids);
        if (_session is not null)
            await _collection.DeleteManyAsync(_session, f, cancellationToken: ct);
        else
            await _collection.DeleteManyAsync(f, cancellationToken: ct);
    }

    public async Task SoftDeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        var userId = CurrentUserId();
        var models = entities.Select(e =>
        {
            e.Status = EntityStatus.Deleted;
            e.DeletedAt ??= now;
            e.UpdatedAt = now;
            e.UpdatedBy = userId;
            return new ReplaceOneModel<T>(Filter.Eq(x => x.Id, e.Id), e);
        }).ToList<WriteModel<T>>();

        if (models.Count > 0)
        {
            if (_session is not null)
                await _collection.BulkWriteAsync(_session, models, cancellationToken: ct);
            else
                await _collection.BulkWriteAsync(models, cancellationToken: ct);
        }
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

        var cursor = _session is not null
            ? await _collection.FindAsync(_session, combined, findOptions, ct)
            : await _collection.FindAsync(combined, findOptions, ct);
        return await cursor.ToListAsync(ct);
    }

    public async Task<PagedResponse<T>> FindPagedAsync(QueryFilter<T> filter, CancellationToken ct = default)
    {
        var combined = NotDeleted;
        foreach (var cond in filter.Conditions)
            combined = Filter.And(combined, Filter.Where(cond));

        var total = _session is not null
            ? (int)await _collection.CountDocumentsAsync(_session, combined, cancellationToken: ct)
            : (int)await _collection.CountDocumentsAsync(combined, cancellationToken: ct);

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

        var cursor = _session is not null
            ? await _collection.FindAsync(_session, combined, findOptions, ct)
            : await _collection.FindAsync(combined, findOptions, ct);
        var items = await cursor.ToListAsync(ct);

        return PagedResponse<T>.Create(items, total, page, pageSize > 0 ? pageSize : total);
    }

    public IQueryable<T> Query()
        => _collection.AsQueryable().Where(e => e.Status != EntityStatus.Deleted);

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
        var result = _session is not null
            ? await _collection.FindAsync(_session, textFilter, cancellationToken: ct)
            : await _collection.FindAsync(textFilter, cancellationToken: ct);
        return await result.ToListAsync(ct);
    }
}
