using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Payments.Gateways;

/// <summary>
/// Nagad merchant gateway. The full handshake is: <c>checkout/initialize</c>
/// → <c>checkout/complete</c> with RSA-encrypted sensitive fields. Production
/// integration requires merchant-specific keys.
///
/// <para>
/// <b>Stub note:</b> the structural shape of the request/response is wired,
/// but the RSA encryption step and SHA-256 signature build are deferred to
/// the live integration pass — they require a real merchant keypair.
/// </para>
///
/// Default endpoints:
/// <list type="bullet">
///   <item>Sandbox: <c>http://sandbox.mynagad.com:10080/api/dfs</c></item>
///   <item>Production: <c>https://api.mynagad.com/api/dfs</c></item>
/// </list>
/// </summary>
public sealed class NagadPaymentGateway : IFcmsPaymentGateway
{
    public const string SandboxBase = "http://sandbox.mynagad.com:10080/api/dfs";
    public const string ProductionBase = "https://api.mynagad.com/api/dfs";

    private readonly HttpClient _http;
    private readonly ILogger<NagadPaymentGateway> _logger;

    public NagadPaymentGateway(HttpClient http, ILogger<NagadPaymentGateway> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string GatewayId => PaymentGateways.Nagad;

    public async Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest request, PaymentSettings settings, string apiKey, CancellationToken ct = default)
    {
        var baseUrl = ResolveBaseUrl(settings);
        var url = $"{baseUrl}/check-out/initialize/{Uri.EscapeDataString(settings.MerchantId)}/{Uri.EscapeDataString(request.OrderReference)}";
        var payload = new
        {
            accountNumber = settings.MerchantId,
            dateTime = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            sensitiveData = "deferred-rsa-encryption",   // placeholder — see class doc
            signature = "deferred-sha256-signature"
        };

        try
        {
            using var resp = await _http.PostAsJsonAsync(url, payload, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return PaymentInitiateResult.Fail($"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(raw);
            var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : null;
            if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
                return PaymentInitiateResult.Fail($"Nagad status={status}");

            var redirect = doc.RootElement.TryGetProperty("callBackUrl", out var u) ? u.GetString() : null;
            var paymentRef = doc.RootElement.TryGetProperty("paymentReferenceId", out var p) ? p.GetString() : null;
            if (string.IsNullOrEmpty(redirect)) return PaymentInitiateResult.Fail("Missing callBackUrl.");
            return PaymentInitiateResult.Ok(redirect, paymentRef ?? request.OrderReference);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nagad Initiate failed for {Order}", request.OrderReference);
            return PaymentInitiateResult.Fail(ex.Message);
        }
    }

    public async Task<PaymentResult> VerifyAsync(string transactionId, PaymentSettings settings, string apiKey, CancellationToken ct = default)
    {
        var baseUrl = ResolveBaseUrl(settings);
        var url = $"{baseUrl}/verify/payment/{Uri.EscapeDataString(transactionId)}";

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return PaymentResult.Fail($"HTTP {(int)resp.StatusCode}", raw);

            using var doc = JsonDocument.Parse(raw);
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            var amt = doc.RootElement.TryGetProperty("amount", out var a) && decimal.TryParse(a.GetString(), out var d) ? d : 0m;

            return string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
                ? PaymentResult.Ok(transactionId, amt, status!, raw)
                : PaymentResult.Fail($"Status={status}", raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nagad Verify failed for {Tx}", transactionId);
            return PaymentResult.Fail(ex.Message);
        }
    }

    public Task<PaymentResult> HandleWebhookAsync(IDictionary<string, string> payload, PaymentSettings settings, string apiKey, CancellationToken ct = default)
    {
        // Nagad IPN signature scheme requires the merchant private key —
        // implement during live integration. Reject for now rather than
        // accepting unsigned webhooks.
        if (!payload.TryGetValue("payment_ref_id", out var pid) || string.IsNullOrEmpty(pid))
            return Task.FromResult(PaymentResult.Fail("Missing payment_ref_id."));
        return Task.FromResult(PaymentResult.Fail("Webhook verification not yet implemented for Nagad."));
    }

    private static string ResolveBaseUrl(PaymentSettings settings)
        => !string.IsNullOrWhiteSpace(settings.EndpointOverride) ? settings.EndpointOverride
           : settings.TestMode ? SandboxBase : ProductionBase;
}
