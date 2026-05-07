namespace FlexCms.Framework.Db;

/// <summary>
/// Persistence façade — abstracts the EF / MongoDB difference for code
/// that doesn't care which backend is in play. <b>Behavior differs by
/// backend in subtle ways</b> — read these notes before writing services
/// that need to be portable.
///
/// <list type="table">
///   <item>
///     <term><see cref="SaveChangesAsync"/></term>
///     <description>EF: flushes the change tracker (insert/update/delete).
///     Mongo: <b>NO-OP</b> — every <c>IRepository&lt;T&gt;</c> call already
///     hit the wire. Calling it on Mongo is harmless but doesn't provide
///     atomicity; use <see cref="BeginTransactionAsync"/> for that.</description>
///   </item>
///   <item>
///     <term><see cref="BeginTransactionAsync"/> / <see cref="CommitAsync"/> / <see cref="RollbackAsync"/></term>
///     <description>EF: standard DB transaction, works on every supported
///     RDBMS. Mongo: requires a replica set or mongos cluster. On a
///     standalone Mongo instance, BeginTransactionAsync swallows the
///     "not supported" exception with a one-time warning and continues
///     non-transactionally — Commit/Rollback then become no-ops. Caller
///     code stays the same; only the safety guarantee changes.</description>
///   </item>
///   <item>
///     <term>Audit field defaults</term>
///     <description>Mongo's repository sets CreatedAt / UpdatedAt /
///     CreatedBy / UpdatedBy on every Add/Update. EF relies on
///     <c>FcmsDbContext.SaveChangesAsync</c>'s interceptor to do the same.
///     Net effect identical, but if you bypass SaveChangesAsync (e.g.
///     <c>ExecuteUpdateAsync</c>) the EF path skips them — Mongo would
///     have already written them.</description>
///   </item>
/// </list>
/// </summary>
public interface IFcmsUnitOfWork : IAsyncDisposable
{
    IRepository<T> Repository<T>() where T : class, IBaseEntity;

    /// <summary>
    /// Begin an atomic transaction. <b>Mongo:</b> requires a replica set —
    /// on a standalone server this no-ops with a warning log so the rest
    /// of the operation proceeds without atomicity rather than crashing.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);

    /// <summary>
    /// EF: flushes the change tracker. <b>Mongo: no-op</b> — every
    /// repository call already persisted; the method exists only so
    /// shared code can compile against the same contract.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
