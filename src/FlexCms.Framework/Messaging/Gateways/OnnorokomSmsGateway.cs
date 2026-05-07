using System.Net.Http.Json;
using System.Text.Json;
using FlexCms.Framework.Messaging;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Messaging.Gateways;

/// <summary>
/// Onnorokom SMS — JSON POST. Their REST contract returns a JSON body containing
/// a <c>responseCode</c>; <c>"1900"</c> means accepted-for-delivery.
/// Default endpoint: <c>https://api2.onnorokomsms.com/sendsms.asmx/OneToManyWithCounter</c>
/// (the legacy SOAP-style REST gateway most accounts ship with).
/// </summary>
public sealed class OnnorokomSmsGateway : ISmsGateway
{
    public const string DefaultEndpoint = "https://api2.onnorokomsms.com/sendsms.asmx/OneToManyWithCounter";
    public const string SuccessCode = "1900";

    private readonly HttpClient _http;
    private readonly ILogger<OnnorokomSmsGateway> _logger;

    public OnnorokomSmsGateway(HttpClient http, ILogger<OnnorokomSmsGateway> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string GatewayId => SmsGateways.Onnorokom;

    public async Task<SmsSendResult> SendAsync(SmsMessage message, SmsSettings settings, string apiKey, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(settings.EndpointOverride) ? DefaultEndpoint : settings.EndpointOverride;
        var payload = new
        {
            userName = settings.Username,
            userPassword = apiKey,
            mobileNumber = message.To,
            smsText = message.Text,
            // Onnorokom expects "TEXT" or "UNICODE" — Bengali content sent
            // as TEXT renders as ?'s on the handset.
            type = settings.BulkSmsType == BulkSmsType.Unicode ? "UNICODE" : "TEXT",
            maskName = settings.SenderId,
            campaignName = ""
        };

        try
        {
            using var resp = await _http.PostAsJsonAsync(url, payload, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return SmsSendResult.Fail($"HTTP {(int)resp.StatusCode}", raw);

            // Onnorokom wraps payload as {"responseCode":"1900", ...}; substring check is robust to schema drift.
            if (raw.Contains($"\"responseCode\":\"{SuccessCode}\"", StringComparison.Ordinal))
                return SmsSendResult.Ok(raw);

            return SmsSendResult.Fail($"Onnorokom non-success response.", raw);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Onnorokom SMS response parse failed");
            return SmsSendResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Onnorokom SMS send failed to {To}", message.To);
            return SmsSendResult.Fail(ex.Message);
        }
    }
}
