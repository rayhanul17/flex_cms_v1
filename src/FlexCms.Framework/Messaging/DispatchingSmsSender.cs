using FlexCms.Framework.Messaging.Services;

namespace FlexCms.Framework.Messaging;

/// <summary>
/// Default <see cref="IFcmsSmsSender"/> registered in DI. Looks up the
/// configured gateway from <see cref="SmsSettings.Gateway"/> on every send so
/// admins can switch providers without restarting the app.
/// </summary>
public sealed class DispatchingSmsSender : IFcmsSmsSender
{
    private readonly ISmsSettingsService _settings;
    private readonly Dictionary<string, ISmsGateway> _gateways;

    public DispatchingSmsSender(ISmsSettingsService settings, IEnumerable<ISmsGateway> gateways)
    {
        _settings = settings;
        _gateways = gateways.ToDictionary(g => g.GatewayId, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        if (message is null) return SmsSendResult.Fail("Message was null.");
        if (string.IsNullOrWhiteSpace(message.To)) return SmsSendResult.Fail("Recipient required.");

        var (cfg, apiKey) = await _settings.GetWithKeyAsync(ct);
        if (!cfg.Enabled) return SmsSendResult.Fail("SMS not enabled.");
        if (string.IsNullOrWhiteSpace(apiKey)) return SmsSendResult.Fail("SMS API key not configured.");

        var gw = (cfg.Gateway ?? "").ToLowerInvariant();
        if (!_gateways.TryGetValue(gw, out var gateway))
            return SmsSendResult.Fail($"Unknown SMS gateway '{cfg.Gateway}'.");

        return await gateway.SendAsync(message, cfg, apiKey, ct);
    }
}
