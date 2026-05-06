using FlexCms.Framework.Db;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Runs every hour and moves operation logs older than 24 hours to the archive table.
/// </summary>
public class LogArchiveService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LogArchiveService> _logger;

    public LogArchiveService(IServiceScopeFactory scopes, ILogger<LogArchiveService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await ArchiveAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LogArchiveService: archive pass failed.");
            }
        }
    }

    private async Task ArchiveAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IFcmsLogService>();
        var uow = scope.ServiceProvider.GetRequiredService<IFcmsUnitOfWork>();

        await svc.ArchiveOlderThanAsync(TimeSpan.FromHours(24), ct);
        await uow.SaveChangesAsync(ct);
    }
}
