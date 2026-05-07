using FlexCms.Framework.Payments.Services;

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
        var cfg = await _settings.GetGeneralAsync(ct);
        if (!cfg.Enabled) return PaymentInitiateResult.Fail("Payments not enabled.");
        if (!_gateways.TryGetValue(cfg.ActiveGateway, out var gw))
            return PaymentInitiateResult.Fail($"Unknown gateway '{cfg.ActiveGateway}'.");
        return await gw.InitiateAsync(req, ct);
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
