namespace FlexCms.Framework.Messaging;

/// <summary>
/// Stored under settings key <c>sms:default</c>. <see cref="ApiKeyEncrypted"/>
/// is symmetric-encrypted via <see cref="Microsoft.AspNetCore.DataProtection.IDataProtector"/>
/// (see <see cref="Services.SmsSettingsService"/>) so the gateway secret never
/// lives at rest in plaintext.
/// </summary>
public class SmsSettings
{
    public bool Enabled { get; set; }

    /// <summary>One of the constants in <see cref="SmsGateways"/>.</summary>
    public string Gateway { get; set; } = SmsGateways.Alpha;

    public string SenderId { get; set; } = "";

    /// <summary>Ciphertext (DataProtection-encoded). Read/write plaintext via <see cref="Services.ISmsSettingsService"/>.</summary>
    public string ApiKeyEncrypted { get; set; } = "";

    /// <summary>Optional username/email field — Alpha + Onnorokom only.</summary>
    public string Username { get; set; } = "";

    /// <summary>Override gateway endpoint for testing or self-hosted relays. Empty = use built-in default.</summary>
    public string EndpointOverride { get; set; } = "";

    /// <summary>
    /// Encoding hint sent to the upstream gateway. BD gateways bill
    /// Unicode/Bengali messages at a higher rate per segment, and most
    /// require an explicit type flag — picking the wrong one causes the
    /// SMS to render as <c>?</c>'s on the handset. Default <see cref="Text"/>
    /// keeps English-only deployments cheap; switch to <see cref="Unicode"/>
    /// for Bengali / mixed-script content.
    /// </summary>
    public BulkSmsType BulkSmsType { get; set; } = BulkSmsType.Text;
}

/// <summary>
/// Per-bulk-job message encoding. The string values match the literals
/// the BD gateways accept on the <c>type</c> form field.
/// </summary>
public enum BulkSmsType
{
    Text = 0,
    Unicode = 1
}

public static class SmsGateways
{
    public const string Alpha = "alpha";
    public const string Mram = "mram";
    public const string Onnorokom = "onnorokom";

    public static IReadOnlyList<string> All { get; } = [Alpha, Mram, Onnorokom];
}
