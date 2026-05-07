namespace FlexCms.Framework.Cms;

/// <summary>
/// Well-known action strings written to <see cref="FcmsLog.Action"/>.
/// Convention: "{EntityType}.{Verb}" — keeps audit log rows filterable and consistent
/// across every provider (EF + MongoDB).
/// </summary>
public static class FcmsAuditActions
{
    // ── Pages ─────────────────────────────────────────────────────────────────
    public const string PageCreated = "Page.Created";
    public const string PageUpdated = "Page.Updated";
    public const string PageDeleted = "Page.Deleted";       // soft-delete → trash
    public const string PageHardDeleted = "Page.HardDeleted";   // permanent removal
    public const string PageRestored = "Page.Restored";      // restored from trash

    // ── Posts ─────────────────────────────────────────────────────────────────
    public const string PostCreated = "Post.Created";
    public const string PostUpdated = "Post.Updated";
    public const string PostDeleted = "Post.Deleted";
    public const string PostHardDeleted = "Post.HardDeleted";
    public const string PostRestored = "Post.Restored";

    // ── Categories ────────────────────────────────────────────────────────────
    public const string CategoryCreated = "Category.Created";
    public const string CategoryUpdated = "Category.Updated";
    public const string CategoryDeleted = "Category.Deleted";

    // ── Media ─────────────────────────────────────────────────────────────────
    public const string MediaUploaded = "Media.Uploaded";
    public const string MediaDeleted = "Media.Deleted";
    public const string MediaMoved = "Media.Moved";

    // ── Media Folders ─────────────────────────────────────────────────────────
    public const string FolderCreated = "MediaFolder.Created";
    public const string FolderRenamed = "MediaFolder.Renamed";
    public const string FolderDeleted = "MediaFolder.Deleted";

    // ── OTP / 2FA ─────────────────────────────────────────────────────────────
    public const string OtpIssued       = "Otp.Issued";        // code generated + sent successfully
    public const string OtpSendFailed   = "Otp.SendFailed";    // transport error (SMTP/SMS) — code cleared
    public const string OtpVerified     = "Otp.Verified";      // correct code entered
    public const string OtpFailed       = "Otp.Failed";        // wrong/expired/too-many-attempts
    public const string RecoveryCodeUsed = "Otp.RecoveryCodeUsed"; // backup code consumed
}
