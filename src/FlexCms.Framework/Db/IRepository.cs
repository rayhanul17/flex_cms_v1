using System.Linq.Expressions;

namespace FlexCms.Framework.Db;

public interface IRepository<T> where T : class, IBaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<T>> GetAllAsync(CancellationToken ct = default);
    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(T entity, CancellationToken ct = default);
    Task SoftDeleteAsync(T entity, CancellationToken ct = default);

    /// <summary>
    /// Returns a page of results matching <paramref name="predicate"/> (pass <c>null</c> for all),
    /// ordered by <paramref name="orderBy"/> (ascending by default).
    /// </summary>
    Task<PagedResponse<T>> FindPagedAsync(
        Expression<Func<T, bool>>? predicate,
        Expression<Func<T, object>> orderBy,
        int page,
        int pageSize,
        bool descending = false,
        CancellationToken ct = default);

    // --- Batch fetch ---
    Task<List<T>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    // --- Bulk write ---
    Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    Task SoftDeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes a batch of entities (rows physically removed). Use for
    /// append-only entities like audit logs. For domain entities prefer
    /// <see cref="SoftDeleteRangeAsync"/>.
    /// </summary>
    Task DeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>
    /// Returns a non-deleted <see cref="IQueryable{T}"/> for advanced LINQ
    /// composition (joins, projections, server-side DataTables). The query
    /// already excludes <see cref="EntityStatus.Deleted"/> rows.
    ///
    /// EF returns <c>IQueryable&lt;T&gt;</c>; Mongo returns
    /// <c>MongoDB.Driver.Linq.IMongoQueryable&lt;T&gt;</c> (which derives
    /// from <c>IQueryable&lt;T&gt;</c>) — callers using
    /// <c>BaseAdminController.DataTableResult</c> get correct behavior either way.
    /// </summary>
    IQueryable<T> Query();

    // --- QueryFilter overloads ---

    /// <summary>
    /// Returns all matching entities. If <paramref name="filter"/> has pagination set, applies skip/take.
    /// </summary>
    Task<List<T>> FindAsync(QueryFilter<T> filter, CancellationToken ct = default);

    /// <summary>
    /// Returns a <see cref="PagedResponse{T}"/> using the pagination settings in <paramref name="filter"/>.
    /// </summary>
    Task<PagedResponse<T>> FindPagedAsync(QueryFilter<T> filter, CancellationToken ct = default);

    /// <summary>
    /// Full-text regex search across all string properties of <typeparamref name="T"/>.
    /// Only supported by <c>MongoRepository</c>; EF implementation throws <see cref="NotSupportedException"/>.
    /// </summary>
    Task<List<T>> FindByTextAsync(string searchTerm, CancellationToken ct = default);
}
