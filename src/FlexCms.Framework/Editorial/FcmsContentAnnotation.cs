using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Editorial;

/// <summary>
/// Inline reviewer comment anchored to a region of a content item
/// (Phase 16 — Issue 109). E.g. reviewer highlights paragraph 3 + adds
/// "rephrase this for clarity"; author sees the highlight overlay when
/// they re-open the draft.
///
/// <para>
/// <see cref="AnchorJson"/> is opaque to the framework — the editor
/// component (Toast UI / TipTap / etc.) decides how to serialize a
/// selection range (offset + length / DOM path / line+col / etc.).
/// </para>
/// </summary>
public class FcmsContentAnnotation : BaseEfEntity
{
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }

    public Guid AuthorUserId { get; set; }

    /// <summary>Editor-specific anchor payload (JSON). Opaque to the framework.</summary>
    public string AnchorJson { get; set; } = "{}";

    public string Body { get; set; } = "";

    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
}
