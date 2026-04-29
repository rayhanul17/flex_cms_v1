using System.Linq.Expressions;
using FlexCms.Framework.Clock;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Db.Ef;

public class EfRepository<T> : IRepository<T> where T : BaseEfEntity
{
    protected readonly DbContext _context;
    protected readonly DbSet<T> _set;

    public EfRepository(DbContext context)
    {
        _context = context;
        _set = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _set.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct);

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
        => await _set.Where(e => !e.IsDeleted).ToListAsync(ct);

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false)
    {
        var query = includeDeleted ? _set.IgnoreQueryFilters() : _set.Where(e => !e.IsDeleted);
        return await query.Where(predicate).ToListAsync(ct);
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false)
    {
        var query = includeDeleted ? _set.IgnoreQueryFilters() : _set.Where(e => !e.IsDeleted);
        return await query.FirstOrDefaultAsync(predicate, ct);
    }

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await _set.Where(e => !e.IsDeleted).AnyAsync(predicate, ct);

    public async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = _set.Where(e => !e.IsDeleted);
        return predicate is null
            ? await query.LongCountAsync(ct)
            : await query.LongCountAsync(predicate, ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default)
    {
        entity.CreatedAt = FcmsTime.Now;
        entity.UpdatedAt = FcmsTime.Now;
        await _set.AddAsync(entity, ct);
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        foreach (var e in entities) { e.CreatedAt = now; e.UpdatedAt = now; }
        await _set.AddRangeAsync(entities, ct);
    }

    public Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = FcmsTime.Now;
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
        entity.IsDeleted = true;
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
        var query = _set.Where(e => !e.IsDeleted);
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
        return await _set.Where(e => idList.Contains(e.Id) && !e.IsDeleted).ToListAsync(ct);
    }

    // --- New: Bulk write ---

    public Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        foreach (var e in entities) e.UpdatedAt = now;
        _set.UpdateRange(entities);
        return Task.CompletedTask;
    }

    public Task SoftDeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var now = FcmsTime.Now;
        foreach (var e in entities) { e.IsDeleted = true; e.UpdatedAt = now; }
        _set.UpdateRange(entities);
        return Task.CompletedTask;
    }

    // --- New: QueryFilter overloads ---

    public async Task<List<T>> FindAsync(QueryFilter<T> filter, CancellationToken ct = default)
    {
        IQueryable<T> query = _set.Where(e => !e.IsDeleted);

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
        IQueryable<T> query = _set.Where(e => !e.IsDeleted);

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
}
