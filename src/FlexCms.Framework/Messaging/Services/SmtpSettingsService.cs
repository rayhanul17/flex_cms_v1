using FlexCms.Framework.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Messaging.Services;

public class SmtpSettingsService : ISmtpSettingsService
{
    public const string SettingsKey = "smtp:default";
    private const string ProtectorPurpose = "FlexCms.Smtp.Password";

    private readonly ISettingsService _settings;
    private readonly IDataProtector _protector;
    private readonly ILogger<SmtpSettingsService> _logger;

    public SmtpSettingsService(ISettingsService settings, IDataProtectionProvider protectionProvider, ILogger<SmtpSettingsService> logger)
    {
        _settings = settings;
        _protector = protectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public Task<SmtpSettings> GetAsync(CancellationToken ct = default)
        => _settings.GetAsync<SmtpSettings>(SettingsKey, ct);

    public async Task<(SmtpSettings Settings, string Password)> GetWithPasswordAsync(CancellationToken ct = default)
    {
        var s = await _settings.GetAsync<SmtpSettings>(SettingsKey, ct);
        return (s, Decrypt(s.PasswordEncrypted));
    }

    public async Task SaveAsync(SmtpSettings settings, string? newPassword, CancellationToken ct = default)
    {
        if (settings is null) return;

        // Preserve existing ciphertext when caller doesn't rotate the password.
        if (string.IsNullOrEmpty(newPassword))
        {
            var existing = await _settings.GetAsync<SmtpSettings>(SettingsKey, ct);
            settings.PasswordEncrypted = existing.PasswordEncrypted;
        }
        else
        {
            settings.PasswordEncrypted = _protector.Protect(newPassword);
        }

        await _settings.SaveAsync(SettingsKey, settings, ct);
    }

    private string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        try { return _protector.Unprotect(ciphertext); }
        catch (Exception ex)
        {
            // Most likely cause: key-ring rotated or App_Data\keys deleted
            // since the password was saved. Operator needs to re-enter the
            // SMTP password before mail will work — log so they catch it
            // at startup, not when the next "test email" silently fails.
            _logger.LogWarning(ex,
                "SMTP password ciphertext present but DataProtection could not decrypt it. " +
                "Re-save the SMTP settings with the password to refresh the ciphertext.");
            return "";
        }
    }
}
