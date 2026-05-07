using FlexCms.Framework.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Payments.Services;

public sealed class PaymentSettingsService : IPaymentSettingsService
{
    public const string GeneralKey = "payments:general";
    public const string BkashKey = "payments:bkash";
    public const string SslcommerzKey = "payments:sslcommerz";
    public const string NagadKey = "payments:nagad";

    // Distinct DataProtection purposes per gateway — a leak of one gateway's
    // ciphertext can't be replayed against another. Same isolation pattern
    // SMTP / SMS settings use.
    private const string BkashPurpose = "FlexCms.Payments.Bkash";
    private const string SslcommerzPurpose = "FlexCms.Payments.Sslcommerz";
    private const string NagadPurpose = "FlexCms.Payments.Nagad";

    private readonly ISettingsService _settings;
    private readonly IDataProtector _bkashProtector;
    private readonly IDataProtector _sslcommerzProtector;
    private readonly IDataProtector _nagadProtector;
    private readonly ILogger<PaymentSettingsService> _logger;

    public PaymentSettingsService(ISettingsService settings, IDataProtectionProvider dp, ILogger<PaymentSettingsService> logger)
    {
        _settings = settings;
        _bkashProtector = dp.CreateProtector(BkashPurpose);
        _sslcommerzProtector = dp.CreateProtector(SslcommerzPurpose);
        _nagadProtector = dp.CreateProtector(NagadPurpose);
        _logger = logger;
    }

    // ---------- General ----------

    public Task<PaymentSettings> GetGeneralAsync(CancellationToken ct = default)
        => _settings.GetAsync<PaymentSettings>(GeneralKey, ct);

    public Task SaveGeneralAsync(PaymentSettings settings, CancellationToken ct = default)
    {
        if (settings is null) return Task.CompletedTask;
        return _settings.SaveAsync(GeneralKey, settings, ct);
    }

    // ---------- bKash ----------

    public Task<BkashSettings> GetBkashAsync(CancellationToken ct = default)
        => _settings.GetAsync<BkashSettings>(BkashKey, ct);

    public async Task<(BkashSettings Settings, string AppSecret, string Password)> GetBkashWithSecretsAsync(CancellationToken ct = default)
    {
        var s = await _settings.GetAsync<BkashSettings>(BkashKey, ct);
        return (s, Decrypt(_bkashProtector, s.AppSecretEncrypted), Decrypt(_bkashProtector, s.PasswordEncrypted));
    }

    public async Task SaveBkashAsync(BkashSettings settings, string? newAppSecret, string? newPassword, CancellationToken ct = default)
    {
        if (settings is null) return;
        var existing = await _settings.GetAsync<BkashSettings>(BkashKey, ct);
        settings.AppSecretEncrypted = string.IsNullOrEmpty(newAppSecret)
            ? existing.AppSecretEncrypted
            : _bkashProtector.Protect(newAppSecret);
        settings.PasswordEncrypted = string.IsNullOrEmpty(newPassword)
            ? existing.PasswordEncrypted
            : _bkashProtector.Protect(newPassword);
        await _settings.SaveAsync(BkashKey, settings, ct);
    }

    // ---------- SSLCommerz ----------

    public Task<SslcommerzSettings> GetSslcommerzAsync(CancellationToken ct = default)
        => _settings.GetAsync<SslcommerzSettings>(SslcommerzKey, ct);

    public async Task<(SslcommerzSettings Settings, string StorePassword)> GetSslcommerzWithSecretsAsync(CancellationToken ct = default)
    {
        var s = await _settings.GetAsync<SslcommerzSettings>(SslcommerzKey, ct);
        return (s, Decrypt(_sslcommerzProtector, s.StorePasswordEncrypted));
    }

    public async Task SaveSslcommerzAsync(SslcommerzSettings settings, string? newStorePassword, CancellationToken ct = default)
    {
        if (settings is null) return;
        var existing = await _settings.GetAsync<SslcommerzSettings>(SslcommerzKey, ct);
        settings.StorePasswordEncrypted = string.IsNullOrEmpty(newStorePassword)
            ? existing.StorePasswordEncrypted
            : _sslcommerzProtector.Protect(newStorePassword);
        await _settings.SaveAsync(SslcommerzKey, settings, ct);
    }

    // ---------- Nagad ----------

    public Task<NagadSettings> GetNagadAsync(CancellationToken ct = default)
        => _settings.GetAsync<NagadSettings>(NagadKey, ct);

    public async Task<(NagadSettings Settings, string MerchantPrivateKey)> GetNagadWithSecretsAsync(CancellationToken ct = default)
    {
        var s = await _settings.GetAsync<NagadSettings>(NagadKey, ct);
        return (s, Decrypt(_nagadProtector, s.MerchantPrivateKeyEncrypted));
    }

    public async Task SaveNagadAsync(NagadSettings settings, string? newMerchantPrivateKey, CancellationToken ct = default)
    {
        if (settings is null) return;
        var existing = await _settings.GetAsync<NagadSettings>(NagadKey, ct);
        settings.MerchantPrivateKeyEncrypted = string.IsNullOrEmpty(newMerchantPrivateKey)
            ? existing.MerchantPrivateKeyEncrypted
            : _nagadProtector.Protect(newMerchantPrivateKey);
        await _settings.SaveAsync(NagadKey, settings, ct);
    }

    // ---------- helpers ----------

    private string Decrypt(IDataProtector protector, string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        try { return protector.Unprotect(ciphertext); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Payment gateway secret ciphertext present but DataProtection could not decrypt it. " +
                "Re-save the gateway settings to refresh the ciphertext.");
            return "";
        }
    }
}
