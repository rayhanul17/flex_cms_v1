using System.Collections.Concurrent;
using FlexCms.Framework.Clock;

namespace FlexCms.Framework.Cms.Editing;

public sealed class EditorPresenceService : IEditorPresenceService
{
    /// <summary>
    /// Heartbeats older than this are treated as gone. Set generously: tab-
    /// closes don't always send a Release, so a stale entry should fade
    /// without being misleading. Editor UI should beat at half this rate.
    /// </summary>
    public static readonly TimeSpan StaleWindow = TimeSpan.FromSeconds(45);

    // (entityType:entityId) → (userId → presence). Concurrent inner dict so
    // two editors arriving simultaneously don't lose one another.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, EditorPresence>> _byEntity
        = new(StringComparer.OrdinalIgnoreCase);

    public void Heartbeat(string entityType, Guid entityId, Guid userId, string userName)
    {
        if (string.IsNullOrEmpty(entityType)) return;
        var key = Key(entityType, entityId);
        var inner = _byEntity.GetOrAdd(key, _ => new ConcurrentDictionary<Guid, EditorPresence>());
        inner[userId] = new EditorPresence(userId, userName ?? "", FcmsTime.Now);
    }

    public IReadOnlyList<EditorPresence> GetActive(string entityType, Guid entityId)
    {
        if (!_byEntity.TryGetValue(Key(entityType, entityId), out var inner)) return [];
        var cutoff = FcmsTime.Now - StaleWindow;

        // Snapshot + opportunistic cleanup of stale entries — the cost is
        // amortized across the read so we don't need a separate reaper.
        var live = new List<EditorPresence>();
        foreach (var (uid, p) in inner)
        {
            if (p.LastSeen >= cutoff) live.Add(p);
            else inner.TryRemove(uid, out _);
        }
        return live;
    }

    public void Release(string entityType, Guid entityId, Guid userId)
    {
        if (_byEntity.TryGetValue(Key(entityType, entityId), out var inner))
            inner.TryRemove(userId, out _);
    }

    private static string Key(string entityType, Guid entityId) =>
        $"{entityType}:{entityId:N}";
}
