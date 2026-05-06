namespace FlexCms.Framework.Payments;

/// <summary>
/// One implementation per BD payment gateway (bKash / SSLCommerz / Nagad).
/// Concrete impls live in <see cref="Gateways"/> and are registered as
/// <see cref="IFcmsPaymentGateway"/>; the dispatcher
/// (<see cref="DispatchingPaymentGateway"/>) picks one per call based on
/// <see cref="PaymentSettings.ActiveGateway"/>.
///
/// <para>
/// All methods return <see cref="PaymentResult"/> and never throw for
/// upstream-API errors — failure mode is the same as in
/// <see cref="Messaging.IFcmsEmailService"/> / <see cref="Messaging.IFcmsSmsSender"/>.
/// </para>
/// </summary>
public interface IFcmsPaymentGateway
{
    /// <summary>Identifier — must match a value from <see cref="PaymentGateways"/>.</summary>
    string GatewayId { get; }

    /// <summary>Step 1: ask the gateway to start a transaction. Returns the redirect URL the user should be sent to.</summary>
    Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest request, PaymentSettings settings, string apiKey, CancellationToken ct = default);

    /// <summary>Step 2 (after callback): verify a transaction id with the gateway.</summary>
    Task<PaymentResult> VerifyAsync(string transactionId, PaymentSettings settings, string apiKey, CancellationToken ct = default);

    /// <summary>Webhook: validate the gateway's signed payload + return the parsed result.</summary>
    Task<PaymentResult> HandleWebhookAsync(IDictionary<string, string> payload, PaymentSettings settings, string apiKey, CancellationToken ct = default);
}

public record PaymentInitiateRequest(
    decimal Amount,
    string Currency,
    string OrderReference,
    string CallbackUrl,
    string? CustomerPhone = null,
    string? CustomerEmail = null);

public record PaymentInitiateResult(bool Success, string? RedirectUrl = null, string? TransactionId = null, string? Error = null)
{
    public static PaymentInitiateResult Ok(string redirectUrl, string transactionId) => new(true, redirectUrl, transactionId);
    public static PaymentInitiateResult Fail(string error) => new(false, Error: error);
}

public record PaymentResult(
    bool Success,
    string? TransactionId = null,
    decimal? Amount = null,
    string? Status = null,
    string? Error = null,
    string? RawResponse = null)
{
    public static PaymentResult Ok(string transactionId, decimal amount, string status, string? raw = null)
        => new(true, transactionId, amount, status, null, raw);
    public static PaymentResult Fail(string error, string? raw = null)
        => new(false, Error: error, RawResponse: raw);
}

public static class PaymentGateways
{
    public const string Bkash = "bkash";
    public const string Sslcommerz = "sslcommerz";
    public const string Nagad = "nagad";

    public static IReadOnlyList<string> All { get; } = [Bkash, Sslcommerz, Nagad];
}
