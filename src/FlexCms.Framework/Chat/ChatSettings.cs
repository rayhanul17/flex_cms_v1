namespace FlexCms.Framework.Chat;

/// <summary>
/// Stored under settings key <c>chat:default</c>.
/// </summary>
public class ChatSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum upload size in MB. Default 5.</summary>
    public int MaxUploadSizeMb { get; set; } = 5;

    /// <summary>
    /// Comma-separated extensions allowed for chat uploads. Whitelist + magic-byte
    /// validation in <c>ChatController.Upload</c> together prevent payload spoofing.
    /// </summary>
    public string AllowedExtensions { get; set; } = ".jpg,.jpeg,.png,.gif,.webp,.pdf,.doc,.docx,.xls,.xlsx,.txt,.zip";
}
