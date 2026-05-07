using System.Net.Http.Json;
using System.Text.Json;
using FlexCms.Framework.Payments.Services;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Payments.Gateways;

/// <summary>
/// bKash Tokenized Checkout (PGW). Two-phase: <c>create</c> returns a redirect
/// url + paymentID; client completes the flow on bKash's hosted page; we
/// later verify via <c>execute</c> / <c>payment/status</c>.
///
/// <para>
/// Reads <see cref="BkashSettings"/> on each call so credential changes apply
/// without restart. Forward charges are added to the order amount before it's
/// sent to bKash; Backward charges are deducted from the merchant payout (no
/// adjustment to the customer's payable amount).
/// </para>
///
/// <para>
/// <b>Stub note:</b> the production endpoints + token-grant flow require live
/// merchant credentials. This implementation maps the contract shape and
/// returns the gateway's response verbatim, so once real credentials are
/// configured the wiring is in place.
/// </para>
/// </summary>
public sealed class BkashPaymentGateway : IFcmsPaymentGateway
{
    public const string SandboxBase = "https://tokenized.sandbox.bka.sh/v1.2.0-beta";
    public const string ProductionBase = "https://tokenized.pay.bka.sh/v1.2.0-beta";

    private readonly HttpClient _http;
    private readonly IPaymentSettingsService _settings;
    private readonly IPaymentChargeCalculator _charges;
    private readonly ILogger<BkashPaymentGateway> _logger;

    public BkashPaymentGateway(
        HttpClient http,
        IPaymentSettingsService settings,
        IPaymentChargeCalculator charges,
        ILogger<BkashPaymentGateway> logger)
    {
        _http = http;
        _settings = settings;
        _charges = charges;
        _logger = logger;
    }

    public string GatewayId => PaymentGateways.Bkash;

    public async Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest request, CancellationToken ct = default)
    {
        var (cfg, appSecret, _password) = await _settings.GetBkashWithSecretsAsync(ct);
        if (!cfg.Enabled) return PaymentInitiateResult.Fail("bKash gateway not enabled.");

        var charge = _charges.Calculate(request.Amount, cfg.Charge);
        var amountToCharge = charge.CustomerPays;

        var baseUrl = ResolveBaseUrl(cfg);
        var url = $"{baseUrl}/checkout/create";
        var payload = new
        {
            mode = "0011",
            payerReference = request.OrderReference,
            callbackURL = request.CallbackUrl,
            amount = amountToCharge.ToString("0.00"),
            currency = request.Currency ?? "BDT",
            intent = "sale",
            merchantInvoiceNumber = request.OrderReference
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            req.Headers.Add("X-APP-Key", cfg.AppKey);
            req.Headers.Add("X-APP-Secret", appSecret);
            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return PaymentInitiateResult.Fail($"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(raw);
            var redirect = doc.RootElement.TryGetProperty("bkashURL", out var u) ? u.GetString() : null;
            var pid = doc.RootElement.TryGetProperty("paymentID", out var p) ? p.GetString() : null;
            if (string.IsNullOrEmpty(redirect) || string.IsNullOrEmpty(pid))
                return PaymentInitiateResult.Fail("bKash response missing fields.");

            return PaymentInitiateResult.Ok(redirect, pid, charge);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "bKash Initiate failed for {Order}", request.OrderReference);
            return PaymentInitiateResult.Fail(ex.Message);
        }
    }

    public async Task<PaymentResult> VerifyAsync(string transactionId, CancellationToken ct = default)
    {
        var (cfg, appSecret, _password) = await _settings.GetBkashWithSecretsAsync(ct);
        if (!cfg.Enabled) return PaymentResult.Fail("bKash gateway not enabled.");

        var baseUrl = ResolveBaseUrl(cfg);
        var url = $"{baseUrl}/checkout/payment/status";
        var payload = new { paymentID = transactionId };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            req.Headers.Add("X-APP-Key", cfg.AppKey);
            req.Headers.Add("X-APP-Secret", appSecret);
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

    public Task<PaymentResult> HandleWebhookAsync(IDictionary<string, string> payload, CancellationToken ct = default)
    {
        // bKash's IPN signature scheme is account-tier dependent — implement
        // when real merchant docs are available. For now: reject unsigned
        // webhooks rather than blindly trusting them.
        if (!payload.TryGetValue("paymentID", out var pid) || string.IsNullOrEmpty(pid))
            return Task.FromResult(PaymentResult.Fail("Missing paymentID."));
        if (!payload.TryGetValue("signature", out var sig) || string.IsNullOrEmpty(sig))
            return Task.FromResult(PaymentResult.Fail("Missing signature."));
        return Task.FromResult(PaymentResult.Fail("Webhook verification not yet implemented for bKash."));
    }

    private static string ResolveBaseUrl(BkashSettings cfg)
        => !string.IsNullOrWhiteSpace(cfg.EndpointOverride) ? cfg.EndpointOverride
           : cfg.TestMode ? SandboxBase : ProductionBase;
}
