using FlexCms.Framework.Payments.Services;

namespace FlexCms.Framework.Payments;

/// <summary>
/// Façade for the payment subsystem. Reads <see cref="PaymentSettings.ActiveGateway"/>
/// per call and forwards to the matching <see cref="IFcmsPaymentGateway"/>
/// implementation — admins can switch providers without restart.
/// </summary>
public sealed class DispatchingPaymentGateway
{
    private readonly IPaymentSettingsService _settings;
    private readonly Dictionary<string, IFcmsPaymentGateway> _gateways;

    public DispatchingPaymentGateway(IPaymentSettingsService settings, IEnumerable<IFcmsPaymentGateway> gateways)
    {
        _settings = settings;
        _gateways = gateways.ToDictionary(g => g.GatewayId, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest req, CancellationToken ct = default)
    {
        var (cfg, apiKey, _) = await _settings.GetWithSecretsAsync(ct);
        if (!cfg.Enabled) return PaymentInitiateResult.Fail("Payments not enabled.");
        if (!_gateways.TryGetValue(cfg.ActiveGateway, out var gw))
            return PaymentInitiateResult.Fail($"Unknown gateway '{cfg.ActiveGateway}'.");
        return await gw.InitiateAsync(req, cfg, apiKey, ct);
    }

    public async Task<PaymentResult> VerifyAsync(string transactionId, CancellationToken ct = default)
    {
        var (cfg, apiKey, _) = await _settings.GetWithSecretsAsync(ct);
        if (!cfg.Enabled) return PaymentResult.Fail("Payments not enabled.");
        if (!_gateways.TryGetValue(cfg.ActiveGateway, out var gw))
            return PaymentResult.Fail($"Unknown gateway '{cfg.ActiveGateway}'.");
        return await gw.VerifyAsync(transactionId, cfg, apiKey, ct);
    }

    /// <summary>Webhook entry point — caller knows the gateway id from the route, not from settings.</summary>
    public async Task<PaymentResult> HandleWebhookAsync(string gatewayId, IDictionary<string, string> payload, CancellationToken ct = default)
    {
        var (cfg, apiKey, _) = await _settings.GetWithSecretsAsync(ct);
        if (!_gateways.TryGetValue(gatewayId, out var gw))
            return PaymentResult.Fail($"Unknown gateway '{gatewayId}'.");
        return await gw.HandleWebhookAsync(payload, cfg, apiKey, ct);
    }
}
