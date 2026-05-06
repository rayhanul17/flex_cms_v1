using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Messaging;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Health;

/// <summary>Pings the relational DB by issuing a no-op <c>SELECT 1</c> equivalent through EF.</summary>
public sealed class EfDatabaseHealthCheck : IFcmsHealthCheck
{
    private readonly FcmsDbContext _db;

    public EfDatabaseHealthCheck(FcmsDbContext db) => _db = db;

    public string Name => "database";
    public bool IncludeInReadiness => true;

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect
                ? HealthCheckResult.Healthy("EF DbContext can connect.")
                : HealthCheckResult.Unhealthy("EF DbContext.CanConnectAsync returned false.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}

/// <summary>
/// Reports the live capacity / depth of the in-memory background queue. Never
/// fails — queue at 100% is degraded but not unhealthy (the request handlers
/// still work; broadcasts just back up).
/// </summary>
public sealed class BackgroundQueueHealthCheck : IFcmsHealthCheck
{
    private readonly IFcmsBackgroundQueue _queue;
    private readonly FcmsBackgroundQueueOptions _options;

    public BackgroundQueueHealthCheck(IFcmsBackgroundQueue queue, FcmsBackgroundQueueOptions options)
    {
        _queue = queue;
        _options = options;
    }

    public string Name => "background-queue";
    public bool IncludeInReadiness => false;   // local memory; never blocks readiness

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var depth = _queue.ApproximateCount;
        var capacity = _options.Capacity;
        var data = new Dictionary<string, object> { ["depth"] = depth, ["capacity"] = capacity };

        if (depth >= capacity)
            return Task.FromResult(HealthCheckResult.Degraded("Queue at capacity — new enqueues will be rejected.", data));
        if (depth >= capacity * 0.8)
            return Task.FromResult(HealthCheckResult.Degraded("Queue >80% full.", data));
        return Task.FromResult(HealthCheckResult.Healthy("OK", data));
    }
}

/// <summary>
/// Reports free disk space on the App_Data drive. Threshold-based:
/// &lt;100MB free is unhealthy, &lt;500MB is degraded.
/// </summary>
public sealed class DiskSpaceHealthCheck : IFcmsHealthCheck
{
    private readonly string _path;

    public DiskSpaceHealthCheck(string path) => _path = path;

    public string Name => "disk";
    public bool IncludeInReadiness => true;

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var driveLetter = Path.GetPathRoot(Path.GetFullPath(_path));
            if (string.IsNullOrEmpty(driveLetter))
                return Task.FromResult(HealthCheckResult.Healthy("path has no root letter — skipping check"));

            var drive = new DriveInfo(driveLetter);
            var freeMb = drive.AvailableFreeSpace / (1024 * 1024);
            var data = new Dictionary<string, object> { ["free_mb"] = freeMb, ["drive"] = driveLetter };

            if (freeMb < 100) return Task.FromResult(HealthCheckResult.Unhealthy($"Only {freeMb} MB free on {driveLetter}.", data));
            if (freeMb < 500) return Task.FromResult(HealthCheckResult.Degraded($"{freeMb} MB free on {driveLetter}.", data));
            return Task.FromResult(HealthCheckResult.Healthy($"{freeMb} MB free", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Degraded(ex.Message));
        }
    }
}
