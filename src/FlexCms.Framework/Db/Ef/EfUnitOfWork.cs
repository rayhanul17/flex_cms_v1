using Microsoft.EntityFrameworkCore.Storage;

namespace FlexCms.Framework.Db.Ef;

public class EfUnitOfWork : IFcmsUnitOfWork
{
    private readonly FcmsDbContext _context;
    private IDbContextTransaction? _transaction;
    private readonly Dictionary<Type, object> _repositories = new();

    public EfUnitOfWork(FcmsDbContext context)
    {
        _context = context;
    }

    public IRepository<T> Repository<T>() where T : class, IBaseEntity
    {
        var type = typeof(T);
        if (!_repositories.TryGetValue(type, out var repo))
        {
            // Use Activator to bypass compile-time BaseEfEntity constraint
            var repoType = typeof(EfRepository<>).MakeGenericType(type);
            repo = Activator.CreateInstance(repoType, _context)!;
            _repositories[type] = repo;
        }
        return (IRepository<T>)repo;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
        => _transaction = await _context.Database.BeginTransactionAsync(ct);

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
        await _context.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
