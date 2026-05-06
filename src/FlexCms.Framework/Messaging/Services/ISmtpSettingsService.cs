namespace FlexCms.Framework.Messaging.Services;

/// <summary>
/// Thin wrapper around <see cref="FlexCms.Framework.Services.ISettingsService"/>
/// that handles the symmetric encryption of <c>SmtpSettings.PasswordEncrypted</c>
/// via <see cref="Microsoft.AspNetCore.DataProtection.IDataProtector"/>. Callers
/// hand in / receive plaintext; the ciphertext stays inside this service and
/// the persisted DB row.
/// </summary>
public interface ISmtpSettingsService
{
    Task<SmtpSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Returns the SMTP settings with the password decrypted to plaintext (or empty if never set).</summary>
    Task<(SmtpSettings Settings, string Password)> GetWithPasswordAsync(CancellationToken ct = default);

    /// <summary>
    /// Persist <paramref name="settings"/>; if <paramref name="newPassword"/> is
    /// non-null/non-empty, it is encrypted and stored, otherwise the existing
    /// ciphertext is preserved.
    /// </summary>
    Task SaveAsync(SmtpSettings settings, string? newPassword, CancellationToken ct = default);
}
