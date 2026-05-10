using FlexCms.Framework.Payments.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Payments;

/// <summary>
/// Façade for the payment subsystem. Reads <see cref="PaymentSettings.ActiveGateway"/>
/// per call and forwards to the matching <see cref="IFcmsPaymentGateway"/>
/// implementation — admins can switch providers without restart.
///
/// <para>
/// Each gateway impl now fetches its own per-gateway credentials internally
/// (see <see cref="Services.IPaymentSettingsService"/>), so the dispatcher only
/// needs the general settings to pick + gate.
/// </para>
///
/// <para>
/// <b>Idempotency.</b> InitiateAsync caches the result by
/// <see cref="PaymentInitiateRequest.IdempotencyKey"/> for 10 minutes. When the
/// same key arrives again — browser retry, double-click, server restart
/// mid-checkout — the cached <see cref="PaymentInitiateResult"/> is returned
/// without re-calling the gateway. This is the primary defense against
/// charging a customer twice for the same order. The BD gateways themselves
/// reject duplicate <see cref="PaymentInitiateRequest.OrderReference"/>
/// within their session window (typically 30 min), giving us a second layer
/// of protection from the gateway side.
/// </para>
/// </summary>
public sealed class DispatchingPaymentGateway
{
    private readonly IPaymentSettingsService _settings;
    private readonly Dictionary<string, IFcmsPaymentGateway> _gateways;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DispatchingPaymentGateway>? _logger;

    /// <summary>How long an InitiateAsync result is cached for replay.</summary>
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromMinutes(10);

    public DispatchingPaymentGateway(
        IPaymentSettingsService settings,
        IEnumerable<IFcmsPaymentGateway> gateways,
        IMemoryCache cache,
        ILogger<DispatchingPaymentGateway>? logger = null)
    {
        _settings = settings;
        _gateways = gateways.ToDictionary(g => g.GatewayId, StringComparer.OrdinalIgnoreCase);
        _cache = cache;
        _logger = logger;
    }

    public async Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest req, CancellationToken ct = default)
    {
        var cfg = await _settings.GetGeneralAsync(ct);
        if (!cfg.Enabled) return PaymentInitiateResult.Fail("Payments not enabled.");
        if (!_gateways.TryGetValue(cfg.ActiveGateway, out var gw))
            return PaymentInitiateResult.Fail($"Unknown gateway '{cfg.ActiveGateway}'.");

        // Resolve the idempotency key. If the caller passed one, honor it
        // verbatim. Otherwise derive one from the order ref + amount + active
        // gateway so a same-order retry within the cache window still hits
        // the cached result without the caller doing anything special.
        var key = string.IsNullOrWhiteSpace(req.IdempotencyKey)
            ? $"{cfg.ActiveGateway}:{req.OrderReference}:{req.Amount}"
            : req.IdempotencyKey;
        var cacheKey = "fcms:payment:initiate:" + key;

        if (_cache.TryGetValue(cacheKey, out PaymentInitiateResult? cached) && cached is not null)
        {
            _logger?.LogInformation(
                "Idempotency hit for payment initiate (key={Key}, order={Order}) — returning cached result without re-calling gateway.",
                key, req.OrderReference);
            return cached;
        }

        var result = await gw.InitiateAsync(req, ct);

        // Only cache successes — a failed call should be retryable so the
        // customer can try again. (And a failed call with the same key
        // shouldn't lock them into the failure for 10 minutes.)
        if (result.Success)
            _cache.Set(cacheKey, result, IdempotencyWindow);

        return result;
    }

    public async Task<PaymentResult> VerifyAsync(string transactionId, CancellationToken ct = default)
    {
        var cfg = await _settings.GetGeneralAsync(ct);
        if (!cfg.Enabled) return PaymentResult.Fail("Payments not enabled.");
        if (!_gateways.TryGetValue(cfg.ActiveGateway, out var gw))
            return PaymentResult.Fail($"Unknown gateway '{cfg.ActiveGateway}'.");
        return await gw.VerifyAsync(transactionId, ct);
    }

    /// <summary>Webhook entry point — caller knows the gateway id from the route, not from settings.</summary>
    public async Task<PaymentResult> HandleWebhookAsync(string gatewayId, IDictionary<string, string> payload, CancellationToken ct = default)
    {
        if (!_gateways.TryGetValue(gatewayId, out var gw))
            return PaymentResult.Fail($"Unknown gateway '{gatewayId}'.");
        return await gw.HandleWebhookAsync(payload, ct);
    }
}
