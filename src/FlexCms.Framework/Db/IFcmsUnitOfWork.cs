namespace FlexCms.Framework.Db;

public interface IFcmsUnitOfWork : IAsyncDisposable
{
    IRepository<T> Repository<T>() where T : class, IBaseEntity;
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
