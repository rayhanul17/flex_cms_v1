using System.Linq.Expressions;
using FlexCms.Framework.Cms;

namespace FlexCms.Framework.Db.MongoDb;

/// <summary>
/// Decorates any <see cref="IRepository{T}"/> with automatic audit logging,
/// mirroring what <see cref="FcmsAuditInterceptor"/> does for EF.
///
/// Write operations (Add, AddRange, Update, UpdateRange, Delete, DeleteRange,
/// SoftDelete, SoftDeleteRange) write one <see cref="FcmsLog"/> row after
/// the underlying repository call succeeds. Read operations are pass-through.
///
/// Audit failure is always non-fatal — logged via <see cref="IFcmsLogService"/>
/// which itself swallows transport errors.
/// </summary>
internal sealed class AuditingRepository<T> : IRepository<T>, IAuditingRepositoryInner
    where T : class, IBaseEntity
{
    private readonly IRepository<T> _inner;
    private readonly IFcmsLogService _audit;

    private static readonly string Prefix = FcmsAuditInterceptor.GetPrefix(typeof(T));
    private static readonly string TypeName = typeof(T).Name;

    public object Inner => _inner;

    public AuditingRepository(IRepository<T> inner, IFcmsLogService audit)
    {
        _inner = inner;
        _audit = audit;
    }

    // ── Write operations with audit ───────────────────────────────────────────

    public async Task AddAsync(T entity, CancellationToken ct = default)
    {
        await _inner.AddAsync(entity, ct);
        await LogAsync($"{Prefix}.Created", entity.Id, entity, FcmsLogSeverity.Info, ct);
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var list = entities.ToList();
        await _inner.AddRangeAsync(list, ct);
        foreach (var e in list)
            await LogAsync($"{Prefix}.Created", e.Id, e, FcmsLogSeverity.Info, ct);
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        await _inner.UpdateAsync(entity, ct);
        var verb = entity is IBaseEntity b && b.Status == EntityStatus.Deleted ? "Deleted" : "Updated";
        await LogAsync($"{Prefix}.{verb}", entity.Id, entity, FcmsLogSeverity.Info, ct);
    }

    public async Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var list = entities.ToList();
        await _inner.UpdateRangeAsync(list, ct);
        foreach (var e in list)
        {
            var verb = e.Status == EntityStatus.Deleted ? "Deleted" : "Updated";
            await LogAsync($"{Prefix}.{verb}", e.Id, e, FcmsLogSeverity.Info, ct);
        }
    }

    public async Task SoftDeleteAsync(T entity, CancellationToken ct = default)
    {
        await _inner.SoftDeleteAsync(entity, ct);
        await LogAsync($"{Prefix}.Deleted", entity.Id, entity, FcmsLogSeverity.Info, ct);
    }

    public async Task SoftDeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var list = entities.ToList();
        await _inner.SoftDeleteRangeAsync(list, ct);
        foreach (var e in list)
            await LogAsync($"{Prefix}.Deleted", e.Id, e, FcmsLogSeverity.Info, ct);
    }

    public async Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        await _inner.DeleteAsync(entity, ct);
        await LogAsync($"{Prefix}.HardDeleted", entity.Id, entity, FcmsLogSeverity.Warning, ct);
    }

    public async Task DeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var list = entities.ToList();
        await _inner.DeleteRangeAsync(list, ct);
        foreach (var e in list)
            await LogAsync($"{Prefix}.HardDeleted", e.Id, e, FcmsLogSeverity.Warning, ct);
    }

    // ── Read operations — pure pass-through ──────────────────────────────────

    public Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _inner.GetByIdAsync(id, ct);

    public Task<List<T>> GetAllAsync(CancellationToken ct = default)
        => _inner.GetAllAsync(ct);

    public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false)
        => _inner.FindAsync(predicate, ct, includeDeleted);

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool includeDeleted = false)
        => _inner.FirstOrDefaultAsync(predicate, ct, includeDeleted);

    public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => _inner.ExistsAsync(predicate, ct);

    public Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => _inner.CountAsync(predicate, ct);

    public Task<PagedResponse<T>> FindPagedAsync(Expression<Func<T, bool>>? predicate, Expression<Func<T, object>> orderBy, int page, int pageSize, bool descending = false, CancellationToken ct = default)
        => _inner.FindPagedAsync(predicate, orderBy, page, pageSize, descending, ct);

    public Task<List<T>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        => _inner.GetByIdsAsync(ids, ct);

    public Task<List<T>> FindAsync(QueryFilter<T> filter, CancellationToken ct = default)
        => _inner.FindAsync(filter, ct);

    public Task<PagedResponse<T>> FindPagedAsync(QueryFilter<T> filter, CancellationToken ct = default)
        => _inner.FindPagedAsync(filter, ct);

    public IQueryable<T> Query()
        => _inner.Query();

    public Task<List<T>> FindByTextAsync(string searchTerm, CancellationToken ct = default)
        => _inner.FindByTextAsync(searchTerm, ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task LogAsync(string action, Guid entityId, object snapshot, FcmsLogSeverity severity, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(action, TypeName, entityId.ToString(), value: snapshot, ct: ct);
        }
        catch { /* audit failure must never surface to caller */ }
    }
}
