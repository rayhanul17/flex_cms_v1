using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms.Drafts;

/// <summary>
/// Periodic auto-save snapshot of in-progress edits — keeps a long-form
/// editor's typing safe across browser crashes / network drops without
/// pretending each tick is a real revision (those still need an explicit
/// save). One row per (entity, user); subsequent autosaves overwrite the
/// row in place.
///
/// <para>
/// Distinct from <see cref="Revisions.FcmsContentRevision"/>: revisions
/// are immutable history; snapshots are the "last unsaved typing" the
/// editor can offer to restore on the next page open. Snapshots are
/// destroyed on explicit save.
/// </para>
/// </summary>
public class FcmsContentDraftSnapshot : BaseEfEntity
{
    public string EntityType { get; set; } = "";   // "FcmsPage" | "FcmsPost" | module
    public Guid EntityId { get; set; }
    public Guid UserId { get; set; }

    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? Excerpt { get; set; }

    /// <summary>Server time of the last autosave POST.</summary>
    public DateTime CapturedAt { get; set; }
}
