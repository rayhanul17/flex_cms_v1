namespace FlexCms.Framework.Messaging;

/// <summary>
/// Stored under settings key <c>smtp:default</c>. Persisted via
/// <see cref="Services.ISettingsService"/>; <see cref="PasswordEncrypted"/> is
/// re-encrypted by <see cref="Services.SmtpSettingsService"/> with
/// <see cref="Microsoft.AspNetCore.DataProtection.IDataProtector"/> before write
/// and decrypted on read so the plaintext password never lives at rest.
/// </summary>
public class SmtpSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";

    /// <summary>Ciphertext (DataProtection-encoded). Use <see cref="Services.ISmtpSettingsService"/> to read/write the plaintext.</summary>
    public string PasswordEncrypted { get; set; } = "";

    public bool UseSsl { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "";

    /// <summary>Connection timeout for the underlying MailKit client (seconds). Default 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
