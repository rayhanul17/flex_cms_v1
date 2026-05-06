using FlexCms.Framework.Services;
using Microsoft.AspNetCore.DataProtection;

namespace FlexCms.Framework.Payments.Services;

public sealed class PaymentSettingsService : IPaymentSettingsService
{
    public const string SettingsKey = "payments:default";
    private const string Purpose = "FlexCms.Payments.Secret";

    private readonly ISettingsService _settings;
    private readonly IDataProtector _protector;

    public PaymentSettingsService(ISettingsService settings, IDataProtectionProvider dp)
    {
        _settings = settings;
        _protector = dp.CreateProtector(Purpose);
    }

    public Task<PaymentSettings> GetAsync(CancellationToken ct = default)
        => _settings.GetAsync<PaymentSettings>(SettingsKey, ct);

    public async Task<(PaymentSettings Settings, string ApiKey, string MerchantPassword)> GetWithSecretsAsync(CancellationToken ct = default)
    {
        var s = await _settings.GetAsync<PaymentSettings>(SettingsKey, ct);
        return (s, Decrypt(s.ApiKeyEncrypted), Decrypt(s.MerchantPasswordEncrypted));
    }

    public async Task SaveAsync(PaymentSettings settings, string? newApiKey, string? newMerchantPassword, CancellationToken ct = default)
    {
        if (settings is null) return;

        // Same "leave blank to keep" pattern used by SMTP / SMS settings services
        // so the admin UI can show a placeholder without exposing ciphertext.
        var existing = await _settings.GetAsync<PaymentSettings>(SettingsKey, ct);
        settings.ApiKeyEncrypted = string.IsNullOrEmpty(newApiKey)
            ? existing.ApiKeyEncrypted
            : _protector.Protect(newApiKey);
        settings.MerchantPasswordEncrypted = string.IsNullOrEmpty(newMerchantPassword)
            ? existing.MerchantPasswordEncrypted
            : _protector.Protect(newMerchantPassword);

        await _settings.SaveAsync(SettingsKey, settings, ct);
    }

    private string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        try { return _protector.Unprotect(ciphertext); }
        catch { return ""; }
    }
}
