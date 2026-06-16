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

    //
    // Centralises the soft-delete + inactive + include logic so every read
    // method below behaves identically. Defaults:
    //   includeDeleted = false  → Status != Deleted (soft-delete filter applied)
    //   includeInactive = true  → Active + InActive surfaced; flip to false to
    //                             restrict to Status == Active (public-site
    //                             queries, "is this usable" checks).

    private IQueryable<T> BuildQuery(
        bool includeDeleted,
        bool includeInactive,
        Expression<Func<T, object>>[]? includes)
    {
        IQueryable<T> q = _set;

        if (includeDeleted || IsAppendOnly)
        {
            // Bypass the global soft-delete query filter EF applies on save.
            q = q.IgnoreQueryFilters();
        }
        else
        {
            // Belt-and-braces: the global filter already excludes Deleted, but
            // an explicit Where keeps behaviour correct on contexts that don't
            // have the global filter installed (raw DbContext from a module).
            q = q.Where(e => e.Status != EntityStatus.Deleted);
        }

        // Only meaningful for non-append-only entities.
        if (!IsAppendOnly && !includeInactive)
            q = q.Where(e => e.Status == EntityStatus.Active);

        if (includes is { Length: > 0 })
            foreach (var inc in includes)
                q = q.Include(inc);

        return q;
    }


    public async Task<T?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes)
        => await BuildQuery(includeDeleted, includeInactive, includes)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes)
        => await BuildQuery(includeDeleted, includeInactive, includes)
            .FirstOrDefaultAsync(predicate, ct);


    public async Task<List<T>> GetAllAsync(
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes)
        => await BuildQuery(includeDeleted, includeInactive, includes).ToListAsync(ct);

    public async Task<List<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes)
        => await BuildQuery(includeDeleted, includeInactive, includes)
            .Where(predicate).ToListAsync(ct);

    public async Task<List<T>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes)
    {
        var idList = ids.ToList();
        return await BuildQuery(includeDeleted, includeInactive, includes)
            .Where(e => idList.Contains(e.Id)).ToListAsync(ct);
    }


    public async Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true)
        => await BuildQuery(includeDeleted, includeInactive, null).AnyAsync(predicate, ct);

    public async Task<long> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true)
    {
        var q = BuildQuery(includeDeleted, includeInactive, null);
        return predicate is null
            ? await q.LongCountAsync(ct)
            : await q.LongCountAsync(predicate, ct);
    }


    public async Task<PagedResponse<T>> FindPagedAsync(
        Expression<Func<T, bool>>? predicate,
        Expression<Func<T, object>> orderBy,
        int page,
        int pageSize,
        bool descending = false,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes)
    {
        var query = BuildQuery(includeDeleted, includeInactive, includes);
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


    public async Task<List<T>> FindAsync(QueryFilter<T> filter, CancellationToken ct = default)
    {
        IQueryable<T> query = BuildQuery(includeDeleted: false, includeInactive: true, includes: null);

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
        IQueryable<T> query = BuildQuery(includeDeleted: false, includeInactive: true, includes: null);

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


    public IQueryable<T> Query() => BuildQuery(includeDeleted: false, includeInactive: true, includes: null);

    public IQueryable<T> Query(
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes)
        => BuildQuery(includeDeleted, includeInactive, includes);
}
