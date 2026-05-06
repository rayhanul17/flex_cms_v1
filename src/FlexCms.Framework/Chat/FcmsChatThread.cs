using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Chat;

public enum ChatThreadStatus
{
    Open = 0,
    Resolved = 1,
    Closed = 2
}

/// <summary>
/// One conversation between a single user and admins. A user may have many
/// threads over time but at most one in <see cref="ChatThreadStatus.Open"/>
/// state — "Start new" closes the current open thread before creating
/// another. Admin replies all flow into whichever thread the user owns.
/// </summary>
public class FcmsChatThread : BaseEfEntity
{
    public Guid UserId { get; set; }

    /// <summary>Cached display name + avatar slot for the admin list (denormalized for cheap rendering).</summary>
    public string UserDisplayName { get; set; } = "";

    public ChatThreadStatus ThreadStatus { get; set; } = ChatThreadStatus.Open;

    /// <summary>Last message timestamp — drives admin list ordering and the "unread since" calculation.</summary>
    public DateTime? LastMessageAt { get; set; }

    /// <summary>Cached snippet of the last message for the admin list.</summary>
    public string? LastMessagePreview { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    public ICollection<FcmsChatMessage> Messages { get; set; } = [];
}
