using FlexCms.Framework.Cms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FlexCms.Framework.Db.MongoDb;

internal interface IMongoSessionAware
{
    void SetSession(IClientSessionHandle? session);
}

public class MongoUnitOfWork : IFcmsUnitOfWork
{
    private readonly IMongoDatabase _database;
    private readonly IMongoClient _client;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<MongoUnitOfWork>? _logger;
    private readonly IFcmsLogService? _audit;
    private IClientSessionHandle? _session;
    /// <summary>
    /// Tracks whether the current Mongo deployment supports transactions.
    /// Single-node Mongo + Atlas serverless reject StartTransaction with
    /// "Transaction numbers are only allowed on a replica set member or
    /// mongos". We try once, latch the result, and skip cleanly thereafter
    /// — services calling BeginTransactionAsync get a warning + the work
    /// proceeds without atomicity instead of crashing the request.
    /// </summary>
    private static bool? _transactionsSupported;
    private readonly Dictionary<Type, object> _repositories = new();

    private readonly IServiceProvider? _sp;

    public MongoUnitOfWork(
        IMongoClient client,
        IMongoDatabase database,
        IHttpContextAccessor? httpContextAccessor = null,
        ILogger<MongoUnitOfWork>? logger = null,
        IFcmsLogService? audit = null)
    {
        _client = client;
        _database = database;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _audit = audit;
    }

    // Overload used by the DI factory — lazy IFcmsLogService lookup avoids the cycle:
    // IFcmsUnitOfWork → IFcmsLogService → IFcmsUnitOfWork (FcmsLogService injects IFcmsUnitOfWork).
    public MongoUnitOfWork(
        IMongoClient client,
        IMongoDatabase database,
        IHttpContextAccessor? httpContextAccessor,
        ILogger<MongoUnitOfWork>? logger,
        IServiceProvider sp)
    {
        _client = client;
        _database = database;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _sp = sp;
    }

    public IRepository<T> Repository<T>() where T : class, IBaseEntity
    {
        var type = typeof(T);
        if (!_repositories.TryGetValue(type, out var repo))
        {
            // Create MongoRepository<T> via reflection — T is constrained to
            // IBaseEntity here but MongoRepository<T> also requires new(), which
            // can't be expressed at this call-site without duplicating constraints.
            var repoType = typeof(MongoRepository<>).MakeGenericType(type);
            var inner = (IRepository<T>)Activator.CreateInstance(repoType, _database, _httpContextAccessor)!;

            // Wrap with auditing decorator unless the entity opts out.
            // Resolve _audit lazily from _sp when constructed via the DI factory
            // (avoids the IFcmsUnitOfWork → IFcmsLogService → IFcmsUnitOfWork cycle).
            var audit = _audit ?? _sp?.GetService<IFcmsLogService>();
            repo = ShouldAudit<T>() && audit is not null
                ? new AuditingRepository<T>(inner, audit)
                : inner;
            _repositories[type] = repo;

            // Wire session into the inner Mongo repo if a transaction is active.
            if (_session is not null && inner is IMongoSessionAware aware)
                aware.SetSession(_session);
        }
        return (IRepository<T>)repo;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        _session = await _client.StartSessionAsync(cancellationToken: ct);

        if (_transactionsSupported == false)
        {
            // Latched off from a previous probe — skip silently. Repos still
            // run in the (non-transactional) session so any reads honor
            // read-concern, but writes commit immediately on the cluster.
            PropagateSession(_session);
            return;
        }

        try
        {
            _session.StartTransaction();
            _transactionsSupported = true;
            PropagateSession(_session);
        }
        catch (NotSupportedException ex)
        {
            // Standalone server — driver throws NotSupportedException at the
            // client layer before reaching the wire. Latch + warn + carry on.
            _transactionsSupported = false;
            _logger?.LogWarning(ex,
                "MongoDB transactions are not supported by this server (standalone instance). " +
                "FlexCMS will continue without atomic write-sets — install a 3-node replica set or use a managed cluster for transactional safety.");
            PropagateSession(_session);
        }
        catch (MongoCommandException ex) when (ex.Code == 20 || ex.CodeName == "IllegalOperation"
                                               || ex.Message.Contains("replica set", StringComparison.OrdinalIgnoreCase)
                                               || ex.Message.Contains("Transaction numbers", StringComparison.OrdinalIgnoreCase))
        {
            // Server-side rejection on standalone: same outcome.
            _transactionsSupported = false;
            _logger?.LogWarning(ex,
                "MongoDB rejected StartTransaction. Continuing without atomic write-sets.");
            PropagateSession(_session);
        }
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_session is null) return;
        // Skip the commit call when transactions aren't supported — the
        // session is still in use for read-concern routing but has no
        // pending transaction to commit.
        if (_transactionsSupported == false) return;
        try { await _session.CommitTransactionAsync(ct); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not in progress", StringComparison.OrdinalIgnoreCase))
        {
            // Race between latch flip + commit attempt — safe to swallow.
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_session is null) return;
        if (_transactionsSupported == false) return;
        try { await _session.AbortTransactionAsync(ct); }
        catch (InvalidOperationException) { /* same safety as CommitAsync */ }
    }

    /// <summary>
    /// MongoDB persists on every repository call — there's no change-tracker
    /// to flush. <c>SaveChangesAsync</c> stays no-op so callers written
    /// against the EF unit-of-work pattern keep compiling, but if you want
    /// atomicity across multiple operations on Mongo you MUST wrap them in
    /// <see cref="BeginTransactionAsync"/> / <see cref="CommitAsync"/>
    /// (and the deployment must be a replica set or mongos cluster).
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

    public ValueTask DisposeAsync()
    {
        _session?.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private static bool ShouldAudit<T>() where T : class, IBaseEntity
    {
        var type = typeof(T);
        if (typeof(IAppendOnlyEntity).IsAssignableFrom(type)) return false;
        if (type.GetCustomAttributes(typeof(FcmsAuditIgnoreEntityAttribute), inherit: true).Length > 0) return false;
        return true;
    }

    private void PropagateSession(IClientSessionHandle? session)
    {
        foreach (var repo in _repositories.Values)
        {
            // Session must reach the inner MongoRepository, not the decorator.
            var inner = repo is IAuditingRepositoryInner inner2 ? inner2.Inner : repo;
            if (inner is IMongoSessionAware aware)
                aware.SetSession(session);
        }
    }
}

// Marker so PropagateSession can unwrap the decorator.
internal interface IAuditingRepositoryInner
{
    object Inner { get; }
}
