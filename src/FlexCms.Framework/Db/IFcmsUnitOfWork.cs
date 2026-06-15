namespace FlexCms.Framework.Db;

/// <summary>
/// Persistence façade — exposes per-entity repositories and an explicit
/// transaction scope (Begin / Commit / Rollback) on top of a single shared
/// <c>FcmsDbContext</c>. <see cref="SaveChangesAsync"/> flushes the EF
/// change tracker (insert / update / delete). Callers that need atomicity
/// across multiple <see cref="SaveChangesAsync"/> calls wrap them with
/// <see cref="BeginTransactionAsync"/> + <see cref="CommitAsync"/>.
/// </summary>
public interface IFcmsUnitOfWork : IAsyncDisposable
{
    IRepository<T> Repository<T>() where T : class, IBaseEntity;

    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
