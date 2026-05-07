using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Editorial;

/// <summary>
/// Editorial review of a content item (Phase 16 — Issue 109). One row per
/// review request — the latest row's <see cref="ReviewStatus"/> drives the
/// "Approved" / "Pending" / "Changes Requested" badge in admin UI.
///
/// <para>
/// Workflow: Author saves a Draft → submits for review → Reviewer
/// approves OR requests changes → Author edits + resubmits → repeat.
/// AutoPublish=true on Approved → flips IsPublished. Otherwise the
/// approval just gates the manual publish button.
/// </para>
/// </summary>
public class FcmsContentReview : BaseEfEntity
{
    public string EntityType { get; set; } = "";   // "FcmsPage" | "FcmsPost" | module-defined
    public Guid EntityId { get; set; }

    public Guid SubmittedByUserId { get; set; }
    public Guid? AssignedToUserId { get; set; }

    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Submitted;

    /// <summary>Reviewer's comment when approving / requesting changes / rejecting.</summary>
    public string? ReviewerComment { get; set; }

    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }

    public bool AutoPublishOnApproval { get; set; }
}

public enum ReviewStatus
{
    Submitted = 0,
    Approved = 1,
    ChangesRequested = 2,
    Rejected = 3,
}
