namespace FlexCms.Framework.Payments;

/// <summary>
/// Stored under settings key <c>payments:default</c>. The secret credentials
/// (<see cref="ApiKeyEncrypted"/>, <see cref="MerchantPasswordEncrypted"/>)
/// are encrypted via <see cref="Microsoft.AspNetCore.DataProtection.IDataProtector"/>;
/// callers go through <see cref="Services.IPaymentSettingsService"/> for plaintext
/// reads and writes.
/// </summary>
public class PaymentSettings
{
    public bool Enabled { get; set; }

    /// <summary>One of the constants in <see cref="PaymentGateways"/>.</summary>
    public string ActiveGateway { get; set; } = PaymentGateways.Bkash;

    /// <summary>If true, the gateway uses its sandbox endpoint instead of production.</summary>
    public bool TestMode { get; set; } = true;

    /// <summary>Merchant id (display + signing). Plaintext — not a secret.</summary>
    public string MerchantId { get; set; } = "";

    /// <summary>Public username / store id. Plaintext.</summary>
    public string Username { get; set; } = "";

    /// <summary>Encrypted API key / app key. Use <see cref="Services.IPaymentSettingsService"/> for plaintext access.</summary>
    public string ApiKeyEncrypted { get; set; } = "";

    /// <summary>Encrypted second-secret (bKash app secret, SSLCommerz store password, etc.).</summary>
    public string MerchantPasswordEncrypted { get; set; } = "";

    /// <summary>Override gateway endpoint for self-hosted relays / lab tests. Empty = built-in default.</summary>
    public string EndpointOverride { get; set; } = "";

    /// <summary>Currency — ISO 4217 (BDT default for the BD gateways).</summary>
    public string Currency { get; set; } = "BDT";
}
