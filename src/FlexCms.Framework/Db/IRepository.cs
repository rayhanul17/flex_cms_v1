using System.Linq.Expressions;

namespace FlexCms.Framework.Db;

/// <summary>
/// Generic repository for <see cref="IBaseEntity"/> types.
///
/// <para><b>Soft-delete + inactive defaults</b> — read methods exclude rows
/// with <c>Status == Deleted</c> by default and INCLUDE inactive rows by
/// default. Pass <c>includeDeleted: true</c> to also surface deleted rows
/// (trash views, hard-delete tools), or <c>includeInactive: false</c> to
/// restrict to <c>Status == Active</c> only (public site queries, "is this
/// usable" checks).</para>
///
/// <para><b>Includes</b> — pass <c>includes: u =&gt; u.Roles, u =&gt; u.Profile</c>
/// to eager-load navigation properties; the repository turns each expression
/// into an EF <c>Include</c> call so callers don't depend on EF Core directly.</para>
/// </summary>
public interface IRepository<T> where T : class, IBaseEntity
{
    // ── Single-row reads ──────────────────────────────────────────────────────

    Task<T?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes);

    Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes);

    // ── Multi-row reads ───────────────────────────────────────────────────────

    Task<List<T>> GetAllAsync(
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes);

    Task<List<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes);

    Task<List<T>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes);

    // ── Aggregates ────────────────────────────────────────────────────────────

    Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true);

    Task<long> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true);

    // ── Paging ────────────────────────────────────────────────────────────────

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
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes);

    Task<List<T>> FindAsync(QueryFilter<T> filter, CancellationToken ct = default);

    Task<PagedResponse<T>> FindPagedAsync(QueryFilter<T> filter, CancellationToken ct = default);

    // ── Writes ────────────────────────────────────────────────────────────────

    Task AddAsync(T entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(T entity, CancellationToken ct = default);
    Task SoftDeleteAsync(T entity, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    Task SoftDeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes a batch of entities (rows physically removed). Use for
    /// append-only entities like audit logs. For domain entities prefer
    /// <see cref="SoftDeleteRangeAsync"/>.
    /// </summary>
    Task DeleteRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    // ── Raw query ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a non-deleted <see cref="IQueryable{T}"/> for advanced LINQ
    /// composition (joins, projections, server-side DataTables). The query
    /// already excludes <see cref="EntityStatus.Deleted"/> rows.
    /// </summary>
    IQueryable<T> Query();

    /// <summary>
    /// Same as <see cref="Query()"/> but with explicit control over which
    /// lifecycle states are included and which navigation properties are
    /// eager-loaded. Use this when building custom queries that need trash
    /// rows, an Active-only feed, or a pre-included shape (e.g. posts with
    /// their category).
    /// </summary>
    IQueryable<T> Query(
        bool includeDeleted = false,
        bool includeInactive = true,
        params Expression<Func<T, object>>[] includes);
}
