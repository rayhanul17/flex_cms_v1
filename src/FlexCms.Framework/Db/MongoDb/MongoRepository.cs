using System.Linq.Expressions;
using MongoDB.Driver;

namespace FlexCms.Framework.Db.MongoDb;

public class MongoRepository<T> : IRepository<T> where T : BaseMongoEntity
{
    protected readonly IMongoCollection<T> _collection;

    private static readonly FilterDefinitionBuilder<T> Filter = Builders<T>.Filter;

    public MongoRepository(IMongoDatabase database)
    {
        var collectionName = typeof(T).Name.ToLowerInvariant() + "s";
        _collection = database.GetCollection<T>(collectionName);
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
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _collection.InsertOneAsync(entity, cancellationToken: ct);
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var list = entities.ToList();
        foreach (var e in list) { e.CreatedAt = now; e.UpdatedAt = now; }
        await _collection.InsertManyAsync(list, cancellationToken: ct);
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;
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
        entity.UpdatedAt = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(
            Filter.Eq(e => e.Id, entity.Id), entity, cancellationToken: ct);
    }
}
