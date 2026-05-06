using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Messaging;

/// <summary>
/// Drains <see cref="IFcmsBackgroundQueue"/> serially. Each work item runs
/// inside a fresh scope so it can resolve scoped services (DbContext, repos)
/// without leaking state between items. Failures are caught and logged — a
/// throwing item must never poison the queue.
/// </summary>
public sealed class FcmsQueueProcessor : BackgroundService
{
    private readonly IFcmsBackgroundQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<FcmsQueueProcessor> _logger;

    public FcmsQueueProcessor(
        IFcmsBackgroundQueue queue,
        IServiceScopeFactory scopes,
        ILogger<FcmsQueueProcessor> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await work(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FcmsQueueProcessor: queued work item threw");
            }
        }
    }
}
