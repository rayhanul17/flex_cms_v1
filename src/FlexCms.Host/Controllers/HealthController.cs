using FlexCms.Framework.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

/// <summary>
/// Three-tier health endpoints suitable for K8s/Docker liveness +
/// readiness probes:
/// <list type="bullet">
///   <item><c>GET /health</c> — full roll-up of every registered check + per-check detail.</item>
///   <item><c>GET /health/ready</c> — 200 only if every check with <c>IncludeInReadiness=true</c> is Healthy. 503 otherwise.</item>
///   <item><c>GET /health/live</c> — process liveness; always 200 unless the app is hard-broken (no checks invoked).</item>
/// </list>
/// All three are anonymous so probes don't need creds.
/// </summary>
[Route("health")]
[AllowAnonymous]
public class HealthController : Controller
{
    private readonly IEnumerable<IFcmsHealthCheck> _checks;

    public HealthController(IEnumerable<IFcmsHealthCheck> checks) => _checks = checks;

    [HttpGet("")]
    public async Task<IActionResult> Full(CancellationToken ct)
    {
        var results = new List<object>();
        var worst = HealthStatus.Healthy;

        foreach (var check in _checks)
        {
            var r = await SafeAsync(check, ct);
            results.Add(new { name = check.Name, status = r.Status.ToString().ToLowerInvariant(), description = r.Description, data = r.Data });
            if (r.Status > worst) worst = r.Status;
        }

        var status = worst switch
        {
            HealthStatus.Unhealthy => 503,
            HealthStatus.Degraded => 200,   // soft-fail: still 200 so load balancers don't yank us
            _ => 200
        };
        Response.StatusCode = status;
        return Json(new { status = worst.ToString().ToLowerInvariant(), checks = results });
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var results = new List<object>();
        var worst = HealthStatus.Healthy;

        foreach (var check in _checks.Where(c => c.IncludeInReadiness))
        {
            var r = await SafeAsync(check, ct);
            results.Add(new { name = check.Name, status = r.Status.ToString().ToLowerInvariant(), description = r.Description });
            if (r.Status > worst) worst = r.Status;
        }

        Response.StatusCode = worst == HealthStatus.Unhealthy ? 503 : 200;
        return Json(new { status = worst.ToString().ToLowerInvariant(), checks = results });
    }

    [HttpGet("live")]
    public IActionResult Live() => Json(new { status = "alive" });

    private static async Task<HealthCheckResult> SafeAsync(IFcmsHealthCheck check, CancellationToken ct)
    {
        try { return await check.CheckAsync(ct); }
        catch (Exception ex) { return HealthCheckResult.Unhealthy(ex.Message); }
    }
}
