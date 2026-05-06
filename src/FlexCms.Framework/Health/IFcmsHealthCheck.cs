namespace FlexCms.Framework.Health;

public enum HealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2
}

public record HealthCheckResult(HealthStatus Status, string? Description = null, IReadOnlyDictionary<string, object>? Data = null)
{
    public static HealthCheckResult Healthy(string? description = null, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Healthy, description, data);
    public static HealthCheckResult Degraded(string description, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Degraded, description, data);
    public static HealthCheckResult Unhealthy(string description, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Unhealthy, description, data);
}

/// <summary>
/// Pluggable health-check probe. The framework ships built-in checks for the
/// DB, the audit log, the background queue, and disk space; modules can
/// register their own — they're auto-discovered via DI.
///
/// <para>
/// <b>Live vs ready</b>: probes used for the <c>/health/live</c> endpoint
/// MUST be cheap and never depend on external systems (otherwise a flaky DB
/// looks like a dead app to Kubernetes/Docker and triggers restarts).
/// External-dependency probes belong in <c>/health/ready</c>.
/// </para>
/// </summary>
public interface IFcmsHealthCheck
{
    /// <summary>Display name shown in the JSON payload + admin dashboard.</summary>
    string Name { get; }

    /// <summary>True if this probe should be included in the <c>/health/ready</c> roll-up.</summary>
    bool IncludeInReadiness { get; }

    Task<HealthCheckResult> CheckAsync(CancellationToken ct = default);
}
