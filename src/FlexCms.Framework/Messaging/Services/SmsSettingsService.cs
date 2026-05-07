using FlexCms.Framework.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Messaging.Services;

public class SmsSettingsService : ISmsSettingsService
{
    public const string SettingsKey = "sms:default";
    private const string ProtectorPurpose = "FlexCms.Sms.ApiKey";

    private readonly ISettingsService _settings;
    private readonly IDataProtector _protector;
    private readonly ILogger<SmsSettingsService> _logger;

    public SmsSettingsService(ISettingsService settings, IDataProtectionProvider protectionProvider, ILogger<SmsSettingsService> logger)
    {
        _settings = settings;
        _protector = protectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public Task<SmsSettings> GetAsync(CancellationToken ct = default)
        => _settings.GetAsync<SmsSettings>(SettingsKey, ct);

    public async Task<(SmsSettings Settings, string ApiKey)> GetWithKeyAsync(CancellationToken ct = default)
    {
        var s = await _settings.GetAsync<SmsSettings>(SettingsKey, ct);
        return (s, Decrypt(s.ApiKeyEncrypted));
    }

    public async Task SaveAsync(SmsSettings settings, string? newApiKey, CancellationToken ct = default)
    {
        if (settings is null) return;

        if (string.IsNullOrEmpty(newApiKey))
        {
            var existing = await _settings.GetAsync<SmsSettings>(SettingsKey, ct);
            settings.ApiKeyEncrypted = existing.ApiKeyEncrypted;
        }
        else
        {
            settings.ApiKeyEncrypted = _protector.Protect(newApiKey);
        }

        await _settings.SaveAsync(SettingsKey, settings, ct);
    }

    private string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        try { return _protector.Unprotect(ciphertext); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SMS API key ciphertext present but DataProtection could not decrypt it. " +
                "Re-save the SMS settings with the API key to refresh the ciphertext.");
            return "";
        }
    }
}
