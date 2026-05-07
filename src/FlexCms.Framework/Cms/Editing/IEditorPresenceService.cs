namespace FlexCms.Framework.Cms.Editing;

/// <summary>
/// Tracks who is currently editing what — a soft signal so the editor UI
/// can show "User X is editing this page (last seen 12s ago)" before two
/// editors clobber each other's work.
///
/// <para>
/// "Currently editing" = called <see cref="HeartbeatAsync"/> within the last
/// <see cref="StaleWindow"/> seconds. The optimistic-concurrency check
/// (RowVersion) is the actual safety net — this service is just early
/// warning to keep editors out of each other's way.
/// </para>
///
/// <para>
/// In-memory single-instance impl — multi-node deployments need a Redis
/// or SignalR-based variant.
/// </para>
/// </summary>
public interface IEditorPresenceService
{
    /// <summary>
    /// Mark the user as currently editing the given entity. Editor UI
    /// should poll this every 15s while the page is open.
    /// </summary>
    void Heartbeat(string entityType, Guid entityId, Guid userId, string userName);

    /// <summary>Active editors of the entity — fresh heartbeats only.</summary>
    IReadOnlyList<EditorPresence> GetActive(string entityType, Guid entityId);

    /// <summary>Drop the heartbeat (called when the user navigates away).</summary>
    void Release(string entityType, Guid entityId, Guid userId);
}

public sealed record EditorPresence(Guid UserId, string UserName, DateTime LastSeen);
