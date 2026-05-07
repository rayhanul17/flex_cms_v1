using System.Net.Http.Json;
using System.Text.Json;
using FlexCms.Framework.Payments.Services;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Payments.Gateways;

/// <summary>
/// Nagad merchant gateway. The full handshake is: <c>checkout/initialize</c>
/// → <c>checkout/complete</c> with RSA-encrypted sensitive fields.
///
/// <para>
/// Reads <see cref="NagadSettings"/> on each call. Forward charges are added
/// to the amount sent to Nagad; Backward charges come out of the merchant's
/// settlement.
/// </para>
///
/// <para>
/// <b>Stub note:</b> the structural shape of the request/response is wired,
/// but the RSA encryption step (using <c>MerchantPrivateKey</c> + Nagad's
/// public key) and SHA-256 signature build are deferred to the live
/// integration pass — they require a real merchant keypair.
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
    private readonly IPaymentSettingsService _settings;
    private readonly IPaymentChargeCalculator _charges;
    private readonly ILogger<NagadPaymentGateway> _logger;

    public NagadPaymentGateway(
        HttpClient http,
        IPaymentSettingsService settings,
        IPaymentChargeCalculator charges,
        ILogger<NagadPaymentGateway> logger)
    {
        _http = http;
        _settings = settings;
        _charges = charges;
        _logger = logger;
    }

    public string GatewayId => PaymentGateways.Nagad;

    public async Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest request, CancellationToken ct = default)
    {
        var (cfg, _privateKey) = await _settings.GetNagadWithSecretsAsync(ct);
        if (!cfg.Enabled) return PaymentInitiateResult.Fail("Nagad gateway not enabled.");

        var charge = _charges.Calculate(request.Amount, cfg.Charge);
        var amountToCharge = charge.CustomerPays;

        var baseUrl = ResolveBaseUrl(cfg);
        var url = $"{baseUrl}/check-out/initialize/{Uri.EscapeDataString(cfg.MerchantId)}/{Uri.EscapeDataString(request.OrderReference)}";
        var payload = new
        {
            accountNumber = cfg.MerchantNumber,
            dateTime = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            amount = amountToCharge.ToString("0.00"),
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
            return PaymentInitiateResult.Ok(redirect, paymentRef ?? request.OrderReference, charge);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nagad Initiate failed for {Order}", request.OrderReference);
            return PaymentInitiateResult.Fail(ex.Message);
        }
    }

    public async Task<PaymentResult> VerifyAsync(string transactionId, CancellationToken ct = default)
    {
        var (cfg, _privateKey) = await _settings.GetNagadWithSecretsAsync(ct);
        if (!cfg.Enabled) return PaymentResult.Fail("Nagad gateway not enabled.");

        var baseUrl = ResolveBaseUrl(cfg);
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

    public Task<PaymentResult> HandleWebhookAsync(IDictionary<string, string> payload, CancellationToken ct = default)
    {
        // Nagad IPN signature scheme requires the merchant private key —
        // implement during live integration. Reject for now rather than
        // accepting unsigned webhooks.
        if (!payload.TryGetValue("payment_ref_id", out var pid) || string.IsNullOrEmpty(pid))
            return Task.FromResult(PaymentResult.Fail("Missing payment_ref_id."));
        return Task.FromResult(PaymentResult.Fail("Webhook verification not yet implemented for Nagad."));
    }

    private static string ResolveBaseUrl(NagadSettings cfg)
        => !string.IsNullOrWhiteSpace(cfg.EndpointOverride) ? cfg.EndpointOverride
           : cfg.TestMode ? SandboxBase : ProductionBase;
}
