namespace FlexCms.Framework.Chat;

/// <summary>
/// Persistence + state-machine for chat threads + messages. The SignalR hub
/// (<see cref="ChatHub"/>) sits on top of this service for realtime delivery;
/// the AJAX fallback in <c>ChatController</c> uses the same API.
/// </summary>
public interface IChatService
{
    /// <summary>Get the user's current Open thread, creating one if none exists.</summary>
    Task<FcmsChatThread> GetOrCreateOpenThreadAsync(Guid userId, string userDisplayName, CancellationToken ct = default);

    /// <summary>Close the current open thread (if any) and create a fresh one.</summary>
    Task<FcmsChatThread> StartNewThreadAsync(Guid userId, string userDisplayName, CancellationToken ct = default);

    Task<FcmsChatThread?> GetThreadAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>Recent threads ordered by <see cref="FcmsChatThread.LastMessageAt"/> desc, capped at <paramref name="max"/>.</summary>
    Task<List<FcmsChatThread>> GetRecentThreadsAsync(int max = 50, CancellationToken ct = default);

    /// <summary>All messages in a thread, oldest-first.</summary>
    Task<List<FcmsChatMessage>> GetMessagesAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>Append a message; bumps the thread's <c>LastMessageAt</c> + preview.</summary>
    Task<FcmsChatMessage> AddMessageAsync(FcmsChatMessage message, CancellationToken ct = default);

    /// <summary>Mark all messages in <paramref name="threadId"/> from the OTHER side as read.</summary>
    Task<int> MarkReadAsync(Guid threadId, ChatSenderRole markerRole, CancellationToken ct = default);

    /// <summary>Flip a thread to <see cref="ChatThreadStatus.Resolved"/>; admin who resolved is recorded.</summary>
    Task ResolveThreadAsync(Guid threadId, Guid adminUserId, CancellationToken ct = default);
}
