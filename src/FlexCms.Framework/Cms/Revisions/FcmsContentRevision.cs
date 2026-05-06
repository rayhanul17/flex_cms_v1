using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms.Revisions;

/// <summary>
/// Snapshot row capturing the full content of a page or post at edit-save
/// time. Allows side-by-side diff and one-click restore. Append-only —
/// admins clean up old revisions via a retention setting (future work).
/// </summary>
public class FcmsContentRevision : BaseEfEntity
{
    /// <summary>Type of the parent — typically <c>nameof(FcmsPage)</c> or <c>nameof(FcmsPost)</c>.</summary>
    public string EntityType { get; set; } = "";

    public Guid EntityId { get; set; }

    /// <summary>Monotonically increasing per-entity. Latest = largest number.</summary>
    public int Version { get; set; }

    /// <summary>Cached title at the time of the snapshot (display only).</summary>
    public string Title { get; set; } = "";

    /// <summary>Full content snapshot — typically HTML; stored verbatim.</summary>
    public string ContentSnapshot { get; set; } = "";

    public Guid? AuthorUserId { get; set; }
    public string? Comment { get; set; }
}
