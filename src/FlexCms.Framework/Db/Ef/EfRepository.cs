using System.Linq.Expressions;
using FlexCms.Framework.Clock;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Db.Ef;

public class EfRepository<T> : IRepository<T> where T : BaseEfEntity
{
    protected readonly DbContext _context;
    protected readonly DbSet<T> _set;

    /// <summary>
    /// Append-only entities (audit logs) opt out of soft-delete filtering —
    /// FcmsDbContext strips the Status column entirely for them, so any
    /// LINQ predicate referencing Status would fail to translate.
    /// </summary>
    private static readonly bool IsAppendOnly =
        typeof(IAppendOnlyEntity).IsAssignableFrom(typeof(T));

    public EfRepository(DbContext context)
    {
        _context = context;
        _set = context.Set<T>();
    }

    /// <summary>
    /// Base query honoring soft-delete semantics for normal entities OR
    /// returning the unfiltered set for append-only entities.
    /// </summary>
    private IQueryable<T> NotDeleted =>
        IsAppendOnly ? _set : _set.Where(e => e.Status != EntityStatus.Deleted);

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => IsAppendOnly
            ? await _set.FirstOrDefaultAsync(e => e.Id == id, ct)
            : await _set.FirstOrDefaultAsync(e => e.Id == id && e.Status != EntityStatus.Deleted, ct);

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
        => await NotDeleted.ToListAsync(ct);

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false)
    {
        var query = (includeDeleted || IsAppendOnly)
            ? _set.IgnoreQueryFilters()
            : _set.Where(e => e.Status != EntityStatus.Deleted);
        return await query.Where(predicate).ToListAsync(ct);
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false)
    {
        var query = (includeDeleted || IsAppendOnly)
            ? _set.IgnoreQueryFilters()
            : _set.Where(e => e.Status != EntityStatus.Deleted);
        return await query.FirstOrDefaultAsync(predicate, ct);
    }

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await NotDeleted.AnyAsync(predicate, ct);

    public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = NotDeleted;
        return predicate is null
            ? await query.LongCountAsync(ct)
            : await query.LongCountAsync(predicate, ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await _set.AddAsync(entity, ct);

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
        => await _set.AddRangeAsync(entities, ct);

    public Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        _set.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        _set.Remove(entity);
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(T entity, CancellationToken ct = default)
    {
        entity.Status = EntityStatus.Deleted;
        entity.DeletedAt ??= FcmsTime.Now;
        entity.UpdatedAt = FcmsTime.Now;
        _set.Update(entity);
        return Task.CompletedTask;
    }

    public async Task<PagedResponse<T>> FindPagedAsync(
        Expression<Func<T, bool>>? predicate,
        Expression<Func<T, object>> orderBy,
        int page,
        int pageSize,
        bool descending = false,
        CancellationToken ct = default)
    {
        var query = NotDeleted;
        if (predicate is not null) query = query.Where(predicate);

        var total = await query.CountAsync(ct);

        query = descending
            ? query.OrderByDescending(orderBy)
            : query.OrderBy(orderBy);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResponse<T>.Create(items, total, page, pageSize);
    }

    // --- New: Batch fetch ---

    public async Task<List<T>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        return IsAppendOnly
            ? await _set.Where(e => idList.Contains(e.Id)).ToListAsync(ct)
            : await _set.Where(e => idList.Contains(e.Id) && e.Status != EntityStatus.Deleted).ToListAsync(ct);
    }

    // --- New: Bulk write ---

    public Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        _set.UpdateRange(entities);
        return Task.CompletedTask;
    }

    public Task SoftDeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        foreach (var e in entities) { e.Status = EntityStatus.Deleted; e.DeletedAt ??= now; e.UpdatedAt = now; }
        _set.UpdateRange(entities);
        return Task.CompletedTask;
    }

    public Task DeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        _set.RemoveRange(entities);
        return Task.CompletedTask;
    }

    // --- New: QueryFilter overloads ---

    public async Task<List<T>> FindAsync(QueryFilter<T> filter, CancellationToken ct = default)
    {
        IQueryable<T> query = NotDeleted;

        foreach (var cond in filter.Conditions)
            query = query.Where(cond);

        if (filter.OrderByExpr != null)
            query = filter.IsDescending
                ? query.OrderByDescending(filter.OrderByExpr)
                : query.OrderBy(filter.OrderByExpr);

        if (filter.IsPaged)
            query = query
                .Skip((filter.PageNumber!.Value - 1) * filter.PageSize!.Value)
                .Take(filter.PageSize.Value);

        return await query.ToListAsync(ct);
    }

    public async Task<PagedResponse<T>> FindPagedAsync(QueryFilter<T> filter, CancellationToken ct = default)
    {
        IQueryable<T> query = NotDeleted;

        foreach (var cond in filter.Conditions)
            query = query.Where(cond);

        var total = await query.CountAsync(ct);

        if (filter.OrderByExpr != null)
            query = filter.IsDescending
                ? query.OrderByDescending(filter.OrderByExpr)
                : query.OrderBy(filter.OrderByExpr);
        else
            query = query.OrderByDescending(e => e.CreatedAt);

        var page = filter.PageNumber ?? 1;
        var pageSize = filter.PageSize ?? total;

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResponse<T>.Create(items, total, page, pageSize > 0 ? pageSize : total);
    }

    public Task<List<T>> FindByTextAsync(string searchTerm, CancellationToken ct = default)
        => throw new NotSupportedException("Text search is only supported with MongoDB.");

    public IQueryable<T> Query() => NotDeleted;
}
