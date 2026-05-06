using FlexCms.Framework.Services;
using Microsoft.AspNetCore.DataProtection;

namespace FlexCms.Framework.Messaging.Services;

public class SmtpSettingsService : ISmtpSettingsService
{
    public const string SettingsKey = "smtp:default";
    private const string ProtectorPurpose = "FlexCms.Smtp.Password";

    private readonly ISettingsService _settings;
    private readonly IDataProtector _protector;

    public SmtpSettingsService(ISettingsService settings, IDataProtectionProvider protectionProvider)
    {
        _settings = settings;
        _protector = protectionProvider.CreateProtector(ProtectorPurpose);
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
        catch { return ""; }   // key-rotation or corruption — caller treats as "not configured"
    }
}
