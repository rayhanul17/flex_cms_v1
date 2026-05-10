namespace FlexCms.Framework.Payments;

/// <summary>
/// One implementation per BD payment gateway (bKash / SSLCommerz / Nagad).
/// Concrete impls live in <see cref="Gateways"/> and are registered as
/// <see cref="IFcmsPaymentGateway"/>; the dispatcher
/// (<see cref="DispatchingPaymentGateway"/>) picks one per call based on
/// <see cref="PaymentSettings.ActiveGateway"/>.
///
/// <para>
/// Each impl owns its own credential DTO (<see cref="BkashSettings"/>,
/// <see cref="SslcommerzSettings"/>, <see cref="NagadSettings"/>) — fetched
/// internally via <see cref="Services.IPaymentSettingsService"/>. The interface
/// stays generic so module-shipped gateways can plug in with their own
/// credential shape.
/// </para>
///
/// <para>
/// All methods return <see cref="PaymentResult"/> and never throw for
/// upstream-API errors — failure mode mirrors
/// <see cref="Messaging.IFcmsEmailService"/> / <see cref="Messaging.IFcmsSmsSender"/>.
/// </para>
/// </summary>
public interface IFcmsPaymentGateway
{
    /// <summary>Identifier — must match a value from <see cref="PaymentGateways"/>.</summary>
    string GatewayId { get; }

    /// <summary>Step 1: ask the gateway to start a transaction. The amount on the request is the ORDER amount; the impl is responsible for applying any Forward charge before sending.</summary>
    Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest request, CancellationToken ct = default);

    /// <summary>Step 2 (after callback): verify a transaction id with the gateway.</summary>
    Task<PaymentResult> VerifyAsync(string transactionId, CancellationToken ct = default);

    /// <summary>Webhook: validate the gateway's signed payload + return the parsed result.</summary>
    Task<PaymentResult> HandleWebhookAsync(IDictionary<string, string> payload, CancellationToken ct = default);
}

public record PaymentInitiateRequest(
    decimal Amount,
    string Currency,
    string OrderReference,
    string CallbackUrl,
    string? CustomerPhone = null,
    string? CustomerEmail = null,
    /// <summary>
    /// Optional idempotency key. When supplied, the dispatcher caches the
    /// <see cref="PaymentInitiateResult"/> for this key (default 10 minutes)
    /// and returns it verbatim on any subsequent call with the same key —
    /// preventing the customer being charged twice from a browser retry,
    /// double-click, network blip, or server restart mid-checkout.
    ///
    /// <para>If null, the dispatcher auto-generates one based on
    /// <see cref="OrderReference"/> + amount, so callers that haven't
    /// adopted the pattern yet still get duplicate-protection within an
    /// order. Pass an explicit value (e.g. a stored per-checkout-attempt
    /// GUID) when you want the protection to span retries that happen on
    /// different requests.</para>
    /// </summary>
    string? IdempotencyKey = null);

public record PaymentInitiateResult(
    bool Success,
    string? RedirectUrl = null,
    string? TransactionId = null,
    string? Error = null,
    PaymentCharge? Charge = null)
{
    public static PaymentInitiateResult Ok(string redirectUrl, string transactionId, PaymentCharge? charge = null)
        => new(true, redirectUrl, transactionId, null, charge);
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
