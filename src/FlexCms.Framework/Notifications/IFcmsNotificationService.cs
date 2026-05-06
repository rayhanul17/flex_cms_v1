namespace FlexCms.Framework.Notifications;

/// <summary>
/// Per-user notification persistence + bell-icon read-state management.
/// Broadcast helpers expand to one row per recipient at insert time so the
/// "mark as read" UX stays a per-user toggle.
/// </summary>
public interface IFcmsNotificationService
{
    Task<FcmsNotification> NotifyUserAsync(
        Guid userId,
        string title,
        string? body = null,
        NotificationLevel level = NotificationLevel.Info,
        string? url = null,
        string? icon = null,
        CancellationToken ct = default);

    /// <summary>Insert one notification per active user. Returns the count inserted.</summary>
    Task<int> NotifyAllAsync(
        string title,
        string? body = null,
        NotificationLevel level = NotificationLevel.Info,
        string? url = null,
        string? icon = null,
        CancellationToken ct = default);

    /// <summary>Recent notifications for a user, newest first.</summary>
    Task<List<FcmsNotification>> GetRecentAsync(Guid userId, int max = 20, CancellationToken ct = default);

    /// <summary>Unread count — drives the bell-icon badge.</summary>
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Flip a single notification to read. No-op if it doesn't belong to <paramref name="userId"/>.</summary>
    Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    /// <summary>Flip all of <paramref name="userId"/>'s unread notifications to read. Returns the count flipped.</summary>
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}
