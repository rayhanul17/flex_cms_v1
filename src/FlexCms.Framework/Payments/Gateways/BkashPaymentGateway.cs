using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Payments.Gateways;

/// <summary>
/// bKash Tokenized Checkout (PGW). Two-phase: <c>create</c> returns a redirect
/// url + paymentID; client completes the flow on bKash's hosted page; we
/// later verify via <c>execute</c>.
///
/// <para>
/// <b>Stub note:</b> the production endpoints + token-grant flow require live
/// merchant credentials and a sandbox account. This implementation maps the
/// shape of the contract and returns the gateway's response verbatim, so once
/// real credentials are configured the wiring is in place.
/// </para>
///
/// Default endpoints:
/// <list type="bullet">
///   <item>Sandbox: <c>https://tokenized.sandbox.bka.sh/v1.2.0-beta</c></item>
///   <item>Production: <c>https://tokenized.pay.bka.sh/v1.2.0-beta</c></item>
/// </list>
/// </summary>
public sealed class BkashPaymentGateway : IFcmsPaymentGateway
{
    public const string SandboxBase = "https://tokenized.sandbox.bka.sh/v1.2.0-beta";
    public const string ProductionBase = "https://tokenized.pay.bka.sh/v1.2.0-beta";

    private readonly HttpClient _http;
    private readonly ILogger<BkashPaymentGateway> _logger;

    public BkashPaymentGateway(HttpClient http, ILogger<BkashPaymentGateway> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string GatewayId => PaymentGateways.Bkash;

    public async Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest request, PaymentSettings settings, string apiKey, CancellationToken ct = default)
    {
        var baseUrl = ResolveBaseUrl(settings);
        var url = $"{baseUrl}/checkout/create";
        var payload = new
        {
            mode = "0011",
            payerReference = request.OrderReference,
            callbackURL = request.CallbackUrl,
            amount = request.Amount.ToString("0.00"),
            currency = request.Currency ?? "BDT",
            intent = "sale",
            merchantInvoiceNumber = request.OrderReference
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            req.Headers.Add("X-APP-Key", apiKey);
            req.Headers.Add("Authorization", "Bearer " + settings.MerchantId);
            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return PaymentInitiateResult.Fail($"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(raw);
            var redirect = doc.RootElement.TryGetProperty("bkashURL", out var u) ? u.GetString() : null;
            var pid = doc.RootElement.TryGetProperty("paymentID", out var p) ? p.GetString() : null;
            if (string.IsNullOrEmpty(redirect) || string.IsNullOrEmpty(pid))
                return PaymentInitiateResult.Fail("bKash response missing fields.");

            return PaymentInitiateResult.Ok(redirect, pid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "bKash Initiate failed for {Order}", request.OrderReference);
            return PaymentInitiateResult.Fail(ex.Message);
        }
    }

    public async Task<PaymentResult> VerifyAsync(string transactionId, PaymentSettings settings, string apiKey, CancellationToken ct = default)
    {
        var baseUrl = ResolveBaseUrl(settings);
        var url = $"{baseUrl}/checkout/payment/status";
        var payload = new { paymentID = transactionId };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            req.Headers.Add("X-APP-Key", apiKey);
            req.Headers.Add("Authorization", "Bearer " + settings.MerchantId);
            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return PaymentResult.Fail($"HTTP {(int)resp.StatusCode}", raw);

            using var doc = JsonDocument.Parse(raw);
            var status = doc.RootElement.TryGetProperty("transactionStatus", out var s) ? s.GetString() : null;
            var amt = doc.RootElement.TryGetProperty("amount", out var a) && decimal.TryParse(a.GetString(), out var d) ? d : 0m;

            return string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
                ? PaymentResult.Ok(transactionId, amt, status!, raw)
                : PaymentResult.Fail($"Status={status}", raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "bKash Verify failed for {Tx}", transactionId);
            return PaymentResult.Fail(ex.Message);
        }
    }

    public Task<PaymentResult> HandleWebhookAsync(IDictionary<string, string> payload, PaymentSettings settings, string apiKey, CancellationToken ct = default)
    {
        // bKash's IPN signature scheme is account-tier dependent — implement
        // when real merchant docs are available. For now: reject unsigned
        // webhooks rather than blindly trusting them.
        if (!payload.TryGetValue("paymentID", out var pid) || string.IsNullOrEmpty(pid))
            return Task.FromResult(PaymentResult.Fail("Missing paymentID."));
        if (!payload.TryGetValue("signature", out var sig) || string.IsNullOrEmpty(sig))
            return Task.FromResult(PaymentResult.Fail("Missing signature."));
        // Verification deferred to live integration phase.
        return Task.FromResult(PaymentResult.Fail("Webhook verification not yet implemented for bKash."));
    }

    private static string ResolveBaseUrl(PaymentSettings settings)
        => !string.IsNullOrWhiteSpace(settings.EndpointOverride) ? settings.EndpointOverride
           : settings.TestMode ? SandboxBase : ProductionBase;
}
