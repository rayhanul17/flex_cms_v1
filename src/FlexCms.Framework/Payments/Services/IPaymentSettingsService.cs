namespace FlexCms.Framework.Payments.Services;

/// <summary>
/// Per-gateway settings access. Each gateway has materially different
/// credential shapes (bKash: app key + secret + user/pass, SSLCommerz:
/// store id + password, Nagad: RSA key pairs) so they live under separate
/// settings keys with separate <c>IDataProtector</c> purposes.
///
/// <para>
/// "Get + Save" returns/accepts the persisted DTO with ciphertext fields
/// intact. "GetWithSecretsAsync" decrypts on the way out — only used by
/// the dispatcher right before calling the upstream gateway. "SaveXxxAsync"
/// follows the established "leave new-secret blank to keep the existing
/// ciphertext" pattern shared with SMTP/SMS settings.
/// </para>
/// </summary>
public interface IPaymentSettingsService
{
    Task<PaymentSettings> GetGeneralAsync(CancellationToken ct = default);
    Task SaveGeneralAsync(PaymentSettings settings, CancellationToken ct = default);

    Task<BkashSettings> GetBkashAsync(CancellationToken ct = default);
    Task<(BkashSettings Settings, string AppSecret, string Password)> GetBkashWithSecretsAsync(CancellationToken ct = default);
    Task SaveBkashAsync(BkashSettings settings, string? newAppSecret, string? newPassword, CancellationToken ct = default);

    Task<SslcommerzSettings> GetSslcommerzAsync(CancellationToken ct = default);
    Task<(SslcommerzSettings Settings, string StorePassword)> GetSslcommerzWithSecretsAsync(CancellationToken ct = default);
    Task SaveSslcommerzAsync(SslcommerzSettings settings, string? newStorePassword, CancellationToken ct = default);

    Task<NagadSettings> GetNagadAsync(CancellationToken ct = default);
    Task<(NagadSettings Settings, string MerchantPrivateKey)> GetNagadWithSecretsAsync(CancellationToken ct = default);
    Task SaveNagadAsync(NagadSettings settings, string? newMerchantPrivateKey, CancellationToken ct = default);
}
