using FlexCms.Framework.Messaging;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Messaging.Gateways;

/// <summary>
/// MRAM SMS — GET-style URL with credentials in query string. Success contract:
/// the response body is purely numeric (the message ID) on success; any
/// non-numeric body is treated as an error message from the gateway.
/// Default endpoint: <c>https://mram.com.bd/api/sendsms.php</c>.
/// </summary>
public sealed class MramSmsGateway : ISmsGateway
{
    public const string DefaultEndpoint = "https://mram.com.bd/api/sendsms.php";
    private readonly HttpClient _http;
    private readonly ILogger<MramSmsGateway> _logger;

    public MramSmsGateway(HttpClient http, ILogger<MramSmsGateway> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string GatewayId => SmsGateways.Mram;

    public async Task<SmsSendResult> SendAsync(SmsMessage message, SmsSettings settings, string apiKey, CancellationToken ct = default)
    {
        var endpoint = string.IsNullOrWhiteSpace(settings.EndpointOverride) ? DefaultEndpoint : settings.EndpointOverride;
        // MRAM takes lowercase "text" / "unicode" on the type query parameter.
        var smsType = settings.BulkSmsType == BulkSmsType.Unicode ? "unicode" : "text";
        var url = $"{endpoint}?api_key={Uri.EscapeDataString(apiKey)}" +
                  $"&type={smsType}&contacts={Uri.EscapeDataString(message.To)}" +
                  $"&senderid={Uri.EscapeDataString(settings.SenderId)}" +
                  $"&msg={Uri.EscapeDataString(message.Text)}";

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            var raw = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            if (!resp.IsSuccessStatusCode)
                return SmsSendResult.Fail($"HTTP {(int)resp.StatusCode}", raw);

            // Numeric response → message ID = success. Anything else is an error string.
            if (long.TryParse(raw, out _))
                return SmsSendResult.Ok(raw);

            return SmsSendResult.Fail($"MRAM error: {raw}", raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MRAM SMS send failed to {To}", message.To);
            return SmsSendResult.Fail(ex.Message);
        }
    }
}
