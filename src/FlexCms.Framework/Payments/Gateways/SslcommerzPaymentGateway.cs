using System.Text.Json;
using FlexCms.Framework.Payments.Services;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Payments.Gateways;

/// <summary>
/// SSLCommerz: form-encoded create-session POST returns a JSON envelope
/// containing <c>GatewayPageURL</c>; verification reads <c>val_id</c> from
/// the IPN/return-url and POSTs to <c>/validator/api/validationserverAPI.php</c>.
///
/// <para>
/// Reads <see cref="SslcommerzSettings"/> on each call. Forward charges are
/// added to <c>total_amount</c> before send; Backward charges are absorbed by
/// the merchant payout.
/// </para>
///
/// Default endpoints:
/// <list type="bullet">
///   <item>Sandbox: <c>https://sandbox.sslcommerz.com</c></item>
///   <item>Production: <c>https://securepay.sslcommerz.com</c></item>
/// </list>
/// </summary>
public sealed class SslcommerzPaymentGateway : IFcmsPaymentGateway
{
    public const string SandboxBase = "https://sandbox.sslcommerz.com";
    public const string ProductionBase = "https://securepay.sslcommerz.com";

    private readonly HttpClient _http;
    private readonly IPaymentSettingsService _settings;
    private readonly IPaymentChargeCalculator _charges;
    private readonly ILogger<SslcommerzPaymentGateway> _logger;

    public SslcommerzPaymentGateway(
        HttpClient http,
        IPaymentSettingsService settings,
        IPaymentChargeCalculator charges,
        ILogger<SslcommerzPaymentGateway> logger)
    {
        _http = http;
        _settings = settings;
        _charges = charges;
        _logger = logger;
    }

    public string GatewayId => PaymentGateways.Sslcommerz;

    public async Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest request, CancellationToken ct = default)
    {
        var (cfg, storePassword) = await _settings.GetSslcommerzWithSecretsAsync(ct);
        if (!cfg.Enabled) return PaymentInitiateResult.Fail("SSLCommerz gateway not enabled.");

        var charge = _charges.Calculate(request.Amount, cfg.Charge);
        var amountToCharge = charge.CustomerPays;

        var baseUrl = ResolveBaseUrl(cfg);
        var url = $"{baseUrl}/gwprocess/v4/api.php";

        var form = new Dictionary<string, string>
        {
            ["store_id"] = cfg.StoreId,
            ["store_passwd"] = storePassword,
            ["total_amount"] = amountToCharge.ToString("0.00"),
            ["currency"] = request.Currency ?? "BDT",
            ["tran_id"] = request.OrderReference,
            ["success_url"] = request.CallbackUrl,
            ["fail_url"] = request.CallbackUrl,
            ["cancel_url"] = request.CallbackUrl,
            ["cus_email"] = request.CustomerEmail ?? "noreply@example.com",
            ["cus_phone"] = request.CustomerPhone ?? "01700000000",
            ["product_name"] = request.OrderReference,
            ["product_category"] = "general",
            ["product_profile"] = "general"
        };

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var resp = await _http.PostAsync(url, content, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return PaymentInitiateResult.Fail($"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(raw);
            var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : null;
            if (!string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                return PaymentInitiateResult.Fail($"SSLCommerz status={status}");

            var redirect = doc.RootElement.TryGetProperty("GatewayPageURL", out var u) ? u.GetString() : null;
            var sessionkey = doc.RootElement.TryGetProperty("sessionkey", out var k) ? k.GetString() : null;
            if (string.IsNullOrEmpty(redirect)) return PaymentInitiateResult.Fail("Missing GatewayPageURL.");
            return PaymentInitiateResult.Ok(redirect, sessionkey ?? request.OrderReference, charge);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSLCommerz Initiate failed for {Order}", request.OrderReference);
            return PaymentInitiateResult.Fail(ex.Message);
        }
    }

    public async Task<PaymentResult> VerifyAsync(string transactionId, CancellationToken ct = default)
    {
        var (cfg, storePassword) = await _settings.GetSslcommerzWithSecretsAsync(ct);
        if (!cfg.Enabled) return PaymentResult.Fail("SSLCommerz gateway not enabled.");

        var baseUrl = ResolveBaseUrl(cfg);
        var url = $"{baseUrl}/validator/api/validationserverAPI.php?val_id={Uri.EscapeDataString(transactionId)}&store_id={Uri.EscapeDataString(cfg.StoreId)}&store_passwd={Uri.EscapeDataString(storePassword)}&v=1&format=json";

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return PaymentResult.Fail($"HTTP {(int)resp.StatusCode}", raw);

            using var doc = JsonDocument.Parse(raw);
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            var amt = doc.RootElement.TryGetProperty("amount", out var a) && decimal.TryParse(a.GetString(), out var d) ? d : 0m;

            return string.Equals(status, "VALID", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "VALIDATED", StringComparison.OrdinalIgnoreCase)
                ? PaymentResult.Ok(transactionId, amt, status!, raw)
                : PaymentResult.Fail($"Status={status}", raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSLCommerz Verify failed for {Tx}", transactionId);
            return PaymentResult.Fail(ex.Message);
        }
    }

    public async Task<PaymentResult> HandleWebhookAsync(IDictionary<string, string> payload, CancellationToken ct = default)
    {
        // SSLCommerz IPN: payload contains tran_id + val_id; we round-trip val_id
        // through VerifyAsync to confirm authenticity (the docs explicitly recommend
        // server-to-server validation rather than trusting the IPN alone).
        if (!payload.TryGetValue("val_id", out var valId) || string.IsNullOrEmpty(valId))
            return PaymentResult.Fail("Missing val_id.");
        return await VerifyAsync(valId, ct);
    }

    private static string ResolveBaseUrl(SslcommerzSettings cfg)
        => !string.IsNullOrWhiteSpace(cfg.EndpointOverride) ? cfg.EndpointOverride
           : cfg.TestMode ? SandboxBase : ProductionBase;
}
