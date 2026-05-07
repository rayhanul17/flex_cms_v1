namespace FlexCms.Framework.Payments;

/// <summary>
/// Top-level payment settings stored under key <c>payments:general</c>. The
/// per-gateway credential blobs live under their own keys
/// (<c>payments:bkash</c>, <c>payments:sslcommerz</c>, <c>payments:nagad</c>)
/// — each gateway has materially different credential + RSA-key shapes that
/// don't fit cleanly under a single shared model.
///
/// <para>
/// Module-shipped gateways follow the same pattern: store under a unique
/// settings key and ship their own settings DTO.
/// </para>
/// </summary>
public class PaymentSettings
{
    public bool Enabled { get; set; }

    /// <summary>One of the constants in <see cref="PaymentGateways"/>.</summary>
    public string ActiveGateway { get; set; } = PaymentGateways.Bkash;

    /// <summary>Currency — ISO 4217 (BDT default for the BD gateways).</summary>
    public string Currency { get; set; } = "BDT";
}

/// <summary>
/// Determines who eats the gateway's transaction fee.
/// <list type="bullet">
///   <item><see cref="Forward"/> — Customer pays the order amount PLUS the gateway charge + VAT + extra. Merchant receives the original order amount in full.</item>
///   <item><see cref="Backward"/> — Customer pays only the order amount. The gateway deducts its fee + VAT + extra from that, so the merchant receives less than the order total.</item>
/// </list>
/// </summary>
public enum ChargeBearer
{
    Forward = 0,
    Backward = 1
}

/// <summary>
/// Per-gateway charge configuration shared between bKash / SSLCommerz / Nagad.
/// Embedded into each per-gateway settings class so admins can set different
/// rates per provider (typically 1.85% bKash, 2.5% SSLCommerz, 1.5% Nagad).
/// </summary>
public class PaymentChargeConfig
{
    public ChargeBearer ChargeBearer { get; set; } = ChargeBearer.Forward;

    /// <summary>Gateway commission as a percentage of the order amount. e.g. <c>1.85</c> for 1.85%.</summary>
    public decimal ChargePercent { get; set; }

    /// <summary>Flat fee added on every transaction, in <see cref="PaymentSettings.Currency"/>. e.g. <c>2.00</c>.</summary>
    public decimal FixedCharge { get; set; }

    /// <summary>VAT applied on top of the gateway charge (NOT on the order amount). e.g. <c>15</c> = 15% of charge.</summary>
    public decimal VatPercent { get; set; }

    /// <summary>Optional service surcharge merchant adds (e.g. processing fee). Forward: added to customer total. Backward: deducted from merchant payout.</summary>
    public decimal ExtraCharge { get; set; }
}

/// <summary>
/// bKash Tokenized Checkout credentials. Stored under <c>payments:bkash</c>.
/// AppKey + AppSecret are the X-APP-Key / X-APP-Secret headers; Username +
/// Password are used in the grant-token call.
/// </summary>
public class BkashSettings
{
    public bool Enabled { get; set; }
    public bool TestMode { get; set; } = true;

    /// <summary>X-APP-Key header value. Plaintext (not a secret on its own — needs AppSecret to abuse).</summary>
    public string AppKey { get; set; } = "";

    /// <summary>X-APP-Secret header value (encrypted via DataProtection — read plaintext only through the settings service).</summary>
    public string AppSecretEncrypted { get; set; } = "";

    public string Username { get; set; } = "";

    public string PasswordEncrypted { get; set; } = "";

    /// <summary>The merchant's bKash personal-retail-account number — display only.</summary>
    public string MerchantNumber { get; set; } = "";

    public string EndpointOverride { get; set; } = "";

    public PaymentChargeConfig Charge { get; set; } = new()
    {
        ChargeBearer = ChargeBearer.Forward,
        ChargePercent = 1.85m,   // bKash standard merchant rate
        FixedCharge = 0m,
        VatPercent = 15m,
        ExtraCharge = 0m
    };
}

/// <summary>
/// SSLCommerz credentials. Stored under <c>payments:sslcommerz</c>.
/// </summary>
public class SslcommerzSettings
{
    public bool Enabled { get; set; }
    public bool TestMode { get; set; } = true;

    public string StoreId { get; set; } = "";
    public string StorePasswordEncrypted { get; set; } = "";

    public string EndpointOverride { get; set; } = "";

    public PaymentChargeConfig Charge { get; set; } = new()
    {
        ChargeBearer = ChargeBearer.Forward,
        ChargePercent = 2.5m,   // SSLCommerz typical rate (varies by card brand)
        FixedCharge = 0m,
        VatPercent = 15m,
        ExtraCharge = 0m
    };
}

/// <summary>
/// Nagad credentials. Stored under <c>payments:nagad</c>. Nagad uses RSA
/// asymmetric crypto — both key pairs are needed: the merchant's key pair
/// for signing requests, plus Nagad's public key for verifying responses.
/// </summary>
public class NagadSettings
{
    public bool Enabled { get; set; }
    public bool TestMode { get; set; } = true;

    public string MerchantId { get; set; } = "";
    public string MerchantNumber { get; set; } = "";

    /// <summary>Merchant's RSA public key in PEM format. Plaintext — sent to Nagad for verification.</summary>
    public string MerchantPublicKey { get; set; } = "";

    /// <summary>Merchant's RSA private key (PEM). Encrypted at rest.</summary>
    public string MerchantPrivateKeyEncrypted { get; set; } = "";

    /// <summary>Nagad's RSA public key (PEM) — used to verify Nagad's signed responses. Provided by Nagad onboarding; not secret.</summary>
    public string NagadPublicKey { get; set; } = "";

    public string EndpointOverride { get; set; } = "";

    public PaymentChargeConfig Charge { get; set; } = new()
    {
        ChargeBearer = ChargeBearer.Forward,
        ChargePercent = 1.5m,   // Nagad typical rate
        FixedCharge = 0m,
        VatPercent = 15m,
        ExtraCharge = 0m
    };
}
