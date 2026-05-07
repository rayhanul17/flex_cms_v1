namespace FlexCms.Framework.Editorial;

/// <summary>
/// Editorial workflow operations (Phase 16 — Issue 109): submit for review,
/// approve / request changes / reject, manage inline annotations.
/// </summary>
public interface IEditorialService
{
    /// <summary>Author submits a draft for review. Creates a Submitted row.</summary>
    Task<FcmsContentReview> SubmitForReviewAsync(string entityType, Guid entityId, Guid authorUserId, Guid? assignToUserId, bool autoPublish, CancellationToken ct = default);

    /// <summary>Reviewer approves. Updates the latest review row + (optionally) publishes.</summary>
    Task ApproveAsync(Guid reviewId, Guid reviewerUserId, string? comment, CancellationToken ct = default);

    /// <summary>Reviewer requests changes. Author re-submits when ready.</summary>
    Task RequestChangesAsync(Guid reviewId, Guid reviewerUserId, string comment, CancellationToken ct = default);

    /// <summary>Reviewer rejects (terminal — author must start a new review).</summary>
    Task RejectAsync(Guid reviewId, Guid reviewerUserId, string comment, CancellationToken ct = default);

    /// <summary>Latest review for an entity — drives the badge in admin UI.</summary>
    Task<FcmsContentReview?> GetLatestAsync(string entityType, Guid entityId, CancellationToken ct = default);

    /// <summary>Inline annotations for an entity (open + resolved both, view filters).</summary>
    Task<IReadOnlyList<FcmsContentAnnotation>> GetAnnotationsAsync(string entityType, Guid entityId, CancellationToken ct = default);

    Task<FcmsContentAnnotation> AddAnnotationAsync(string entityType, Guid entityId, Guid authorUserId, string anchorJson, string body, CancellationToken ct = default);

    Task ResolveAnnotationAsync(Guid annotationId, Guid resolvedByUserId, CancellationToken ct = default);
}
