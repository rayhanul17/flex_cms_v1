namespace FlexCms.Framework.Cms;

/// <summary>
/// Well-known action strings written to <see cref="FcmsLog.Action"/>.
/// Convention: "{EntityType}.{Verb}" — keeps audit log rows filterable and consistent.
/// </summary>
public static class FcmsAuditActions
{
    public const string PageCreated = "Page.Created";
    public const string PageUpdated = "Page.Updated";
    public const string PageDeleted = "Page.Deleted";       // soft-delete → trash
    public const string PageHardDeleted = "Page.HardDeleted";   // permanent removal
    public const string PageRestored = "Page.Restored";      // restored from trash

    public const string PostCreated = "Post.Created";
    public const string PostUpdated = "Post.Updated";
    public const string PostDeleted = "Post.Deleted";
    public const string PostHardDeleted = "Post.HardDeleted";
    public const string PostRestored = "Post.Restored";

    public const string CategoryCreated = "Category.Created";
    public const string CategoryUpdated = "Category.Updated";
    public const string CategoryDeleted = "Category.Deleted";

    public const string MediaUploaded = "Media.Uploaded";
    public const string MediaDeleted = "Media.Deleted";
    public const string MediaMoved = "Media.Moved";

    public const string FolderCreated = "MediaFolder.Created";
    public const string FolderRenamed = "MediaFolder.Renamed";
    public const string FolderDeleted = "MediaFolder.Deleted";

    public const string OtpIssued = "Otp.Issued";        // code generated + sent successfully
    public const string OtpSendFailed = "Otp.SendFailed";    // transport error (SMTP/SMS) — code cleared
    public const string OtpVerified = "Otp.Verified";      // correct code entered
    public const string OtpFailed = "Otp.Failed";        // wrong/expired/too-many-attempts
    public const string RecoveryCodeUsed = "Otp.RecoveryCodeUsed"; // backup code consumed

    public const string ModuleUploaded = "Module.Uploaded";       // ZIP uploaded into modules/
    public const string ModuleActivated = "Module.Activated";      // marker cleared; pending restart
    public const string ModuleDeactivated = "Module.Deactivated";    // marker added; pending restart
    public const string ModuleUninstalled = "Module.Uninstalled";    // marked for filesystem removal
    public const string ModuleUpdated = "Module.Updated";        // new binaries deployed
}
