using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Chat;

public enum ChatSenderRole
{
    User = 0,
    Admin = 1,
    System = 2
}

public enum ChatAttachmentKind
{
    None = 0,
    Image = 1,
    File = 2
}

/// <summary>
/// Single chat message. Inline text + optional uploaded attachment (image or
/// arbitrary file). Soft-delete still applies via the inherited
/// <see cref="Db.Ef.BaseEfEntity.Status"/> for moderation but isn't surfaced
/// in the chat UI.
/// </summary>
public class FcmsChatMessage : BaseEfEntity
{
    public Guid ThreadId { get; set; }
    public FcmsChatThread? Thread { get; set; }

    /// <summary>The actual sender identity (user or admin id). Null only for <see cref="ChatSenderRole.System"/>.</summary>
    public Guid? SenderUserId { get; set; }

    public ChatSenderRole SenderRole { get; set; } = ChatSenderRole.User;

    /// <summary>Cached display name so admin list rendering doesn't have to JOIN against users.</summary>
    public string SenderDisplayName { get; set; } = "";

    public string Body { get; set; } = "";

    public ChatAttachmentKind AttachmentKind { get; set; } = ChatAttachmentKind.None;

    /// <summary>Public URL relative to the host (e.g. <c>/uploads/chat/abc.jpg</c>). Empty if no attachment.</summary>
    public string AttachmentUrl { get; set; } = "";

    /// <summary>Original filename (display only — not used for storage path).</summary>
    public string AttachmentName { get; set; } = "";

    public long AttachmentSizeBytes { get; set; }

    /// <summary>True once the recipient (the OTHER side of the thread) has loaded this message.</summary>
    public bool IsRead { get; set; }
}
