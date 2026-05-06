using System.Net.Http.Json;
using FlexCms.Framework.Messaging;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Messaging.Gateways;

/// <summary>
/// Alpha SMS — JSON POST. Success = HTTP 2xx with response code 200 in body.
/// Default endpoint: <c>https://api.sms.net.bd/sendsms</c>.
/// </summary>
public sealed class AlphaSmsGateway : ISmsGateway
{
    public const string DefaultEndpoint = "https://api.sms.net.bd/sendsms";
    private readonly HttpClient _http;
    private readonly ILogger<AlphaSmsGateway> _logger;

    public AlphaSmsGateway(HttpClient http, ILogger<AlphaSmsGateway> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string GatewayId => SmsGateways.Alpha;

    public async Task<SmsSendResult> SendAsync(SmsMessage message, SmsSettings settings, string apiKey, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(settings.EndpointOverride) ? DefaultEndpoint : settings.EndpointOverride;
        var payload = new
        {
            api_key = apiKey,
            msg = message.Text,
            to = message.To,
            sender_id = string.IsNullOrWhiteSpace(settings.SenderId) ? null : settings.SenderId
        };

        try
        {
            using var resp = await _http.PostAsJsonAsync(url, payload, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return SmsSendResult.Fail($"HTTP {(int)resp.StatusCode}", raw);

            // Alpha returns a JSON envelope with "error":0 / "error":non-zero. Cheap substring check
            // beats a strongly-typed model since the gateway returns mixed shapes on edge cases.
            if (raw.Contains("\"error\":0", StringComparison.Ordinal))
                return SmsSendResult.Ok(raw);

            return SmsSendResult.Fail("Alpha responded with non-zero error code.", raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Alpha SMS send failed to {To}", message.To);
            return SmsSendResult.Fail(ex.Message);
        }
    }
}
