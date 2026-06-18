using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlexCms.Host.Hosting;

/// <summary>
/// Sample <see cref="BackgroundService"/> registered by the host itself.
/// Logs a heartbeat every five minutes — proves the host's hosted-service
/// runner is alive and that the loop pattern (PeriodicTimer + cancellation
/// token cooperation) is correct.
///
/// <para>
/// Paired with InvestPro's <c>InvestProDailySummaryService</c> to confirm
/// modules can register <c>IHostedService</c>s the same way: see
/// <c>modules/FlexCms.InvestPro/Services/InvestProDailySummaryService.cs</c>.
/// </para>
/// </summary>
public sealed class HostHeartbeatService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private readonly ILogger<HostHeartbeatService> _logger;

    public HostHeartbeatService(ILogger<HostHeartbeatService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First tick fires immediately so an operator sees the service
        // started without waiting 5 min.
        _logger.LogInformation("HostHeartbeat: started.");

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _logger.LogInformation("HostHeartbeat: tick at {Now:o}.", DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — host stopping. Don't propagate.
        }
        finally
        {
            _logger.LogInformation("HostHeartbeat: stopped.");
        }
    }
}
