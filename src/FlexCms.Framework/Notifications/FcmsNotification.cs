using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Notifications;

public enum NotificationLevel
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Danger = 3
}

/// <summary>
/// Per-user persistent notification visible in the admin bell-icon dropdown.
/// Soft-delete still applies via the inherited <see cref="Db.Ef.BaseEfEntity.Status"/>;
/// <see cref="IsRead"/> is the unread/read toggle for the bell badge count.
///
/// <para>
/// Notifications targeting "everyone" use <see cref="UserId"/> = <c>null</c>;
/// the service expands them by joining against the user list at insert time
/// (one row per user) so per-user read state stays simple.
/// </para>
/// </summary>
public class FcmsNotification : BaseEfEntity
{
    /// <summary>Recipient user id. <c>null</c> only inside the service before broadcast expansion.</summary>
    public Guid? UserId { get; set; }

    public NotificationLevel Level { get; set; } = NotificationLevel.Info;

    public string Title { get; set; } = "";
    public string? Body { get; set; }

    /// <summary>Optional click-target — admin UI navigates here when notification is clicked.</summary>
    public string? Url { get; set; }

    /// <summary>Optional Bootstrap-Icons class (e.g. <c>bi bi-puzzle</c>).</summary>
    public string? Icon { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
