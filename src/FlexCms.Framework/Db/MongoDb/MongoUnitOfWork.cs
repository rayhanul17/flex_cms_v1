using MongoDB.Driver;

namespace FlexCms.Framework.Db.MongoDb;

public class MongoUnitOfWork : IFcmsUnitOfWork
{
    private readonly IMongoDatabase _database;
    private readonly IMongoClient _client;
    private IClientSessionHandle? _session;
    private readonly Dictionary<Type, object> _repositories = new();

    public MongoUnitOfWork(IMongoClient client, IMongoDatabase database)
    {
        _client = client;
        _database = database;
    }

    public IRepository<T> Repository<T>() where T : class, IBaseEntity
    {
        var type = typeof(T);
        if (!_repositories.TryGetValue(type, out var repo))
        {
            // Use Activator to bypass compile-time BaseMongoEntity constraint
            var repoType = typeof(MongoRepository<>).MakeGenericType(type);
            repo = Activator.CreateInstance(repoType, _database)!;
            _repositories[type] = repo;
        }
        return (IRepository<T>)repo;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        _session = await _client.StartSessionAsync(cancellationToken: ct);
        _session.StartTransaction();
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_session is not null)
            await _session.CommitTransactionAsync(ct);
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_session is not null)
            await _session.AbortTransactionAsync(ct);
    }

    // MongoDB auto-saves on operation — SaveChanges is a no-op
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

    public ValueTask DisposeAsync()
    {
        _session?.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
