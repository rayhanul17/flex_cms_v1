namespace FlexCms.Framework.Cms.Drafts;

/// <summary>
/// One snapshot per (entity, user) — the editor POSTs every ~30 seconds
/// while typing. Loading the editor page checks for an existing snapshot
/// newer than the entity's <c>UpdatedAt</c> and offers "Restore unsaved
/// draft?" to the user.
/// </summary>
public interface IDraftSnapshotService
{
    /// <summary>Upsert by (entityType, entityId, userId).</summary>
    Task SaveAsync(string entityType, Guid entityId, Guid userId, DraftSnapshotPayload payload, CancellationToken ct = default);

    /// <summary>Latest snapshot for an entity-user pair, or null if none.</summary>
    Task<FcmsContentDraftSnapshot?> GetAsync(string entityType, Guid entityId, Guid userId, CancellationToken ct = default);

    /// <summary>Drop the snapshot — call after a successful explicit save.</summary>
    Task DiscardAsync(string entityType, Guid entityId, Guid userId, CancellationToken ct = default);
}

public sealed record DraftSnapshotPayload(string? Title, string? Content, string? Excerpt);
