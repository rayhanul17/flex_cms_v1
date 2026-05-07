using FlexCms.Framework.Cms;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Editorial;

public sealed class EditorialService : IEditorialService
{
    private readonly IRepository<FcmsContentReview> _reviews;
    private readonly IRepository<FcmsContentAnnotation> _annotations;
    private readonly IRepository<FcmsPage> _pages;
    private readonly IRepository<FcmsPost> _posts;
    private readonly IFcmsUnitOfWork _uow;

    public EditorialService(
        IRepository<FcmsContentReview> reviews,
        IRepository<FcmsContentAnnotation> annotations,
        IRepository<FcmsPage> pages,
        IRepository<FcmsPost> posts,
        IFcmsUnitOfWork uow)
    {
        _reviews = reviews;
        _annotations = annotations;
        _pages = pages;
        _posts = posts;
        _uow = uow;
    }

    public async Task<FcmsContentReview> SubmitForReviewAsync(string entityType, Guid entityId, Guid authorUserId, Guid? assignToUserId, bool autoPublish, CancellationToken ct = default)
    {
        var row = new FcmsContentReview
        {
            EntityType = entityType,
            EntityId = entityId,
            SubmittedByUserId = authorUserId,
            AssignedToUserId = assignToUserId,
            ReviewStatus = ReviewStatus.Submitted,
            AutoPublishOnApproval = autoPublish,
        };
        await _reviews.AddAsync(row, ct);
        await _uow.SaveChangesAsync(ct);
        return row;
    }

    public async Task ApproveAsync(Guid reviewId, Guid reviewerUserId, string? comment, CancellationToken ct = default)
    {
        var review = await _reviews.GetByIdAsync(reviewId, ct);
        if (review is null) return;
        review.ReviewStatus = ReviewStatus.Approved;
        review.ReviewerComment = comment;
        review.ReviewedAt = FcmsTime.Now;
        review.ReviewedByUserId = reviewerUserId;
        await _reviews.UpdateAsync(review, ct);

        // Optional auto-publish on approval — only flips the flag when the
        // entity exists + isn't already published. Doesn't set PublishedAt
        // if the author scheduled a future date — let the scheduler do it.
        if (review.AutoPublishOnApproval)
        {
            switch (review.EntityType)
            {
                case nameof(FcmsPage):
                    var page = await _pages.GetByIdAsync(review.EntityId, ct);
                    if (page is not null && !page.IsPublished)
                    {
                        page.IsPublished = true;
                        page.PublishedAt ??= FcmsTime.Now;
                        await _pages.UpdateAsync(page, ct);
                    }
                    break;
                case nameof(FcmsPost):
                    var post = await _posts.GetByIdAsync(review.EntityId, ct);
                    if (post is not null && !post.IsPublished)
                    {
                        post.IsPublished = true;
                        post.PublishedAt ??= FcmsTime.Now;
                        await _posts.UpdateAsync(post, ct);
                    }
                    break;
            }
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task RequestChangesAsync(Guid reviewId, Guid reviewerUserId, string comment, CancellationToken ct = default)
    {
        var review = await _reviews.GetByIdAsync(reviewId, ct);
        if (review is null) return;
        review.ReviewStatus = ReviewStatus.ChangesRequested;
        review.ReviewerComment = comment;
        review.ReviewedAt = FcmsTime.Now;
        review.ReviewedByUserId = reviewerUserId;
        await _reviews.UpdateAsync(review, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid reviewId, Guid reviewerUserId, string comment, CancellationToken ct = default)
    {
        var review = await _reviews.GetByIdAsync(reviewId, ct);
        if (review is null) return;
        review.ReviewStatus = ReviewStatus.Rejected;
        review.ReviewerComment = comment;
        review.ReviewedAt = FcmsTime.Now;
        review.ReviewedByUserId = reviewerUserId;
        await _reviews.UpdateAsync(review, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<FcmsContentReview?> GetLatestAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var rows = await _reviews.FindAsync(r => r.EntityType == entityType && r.EntityId == entityId, ct);
        return rows.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
    }

    public async Task<IReadOnlyList<FcmsContentAnnotation>> GetAnnotationsAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var rows = await _annotations.FindAsync(a => a.EntityType == entityType && a.EntityId == entityId, ct);
        return rows.OrderBy(a => a.CreatedAt).ToList();
    }

    public async Task<FcmsContentAnnotation> AddAnnotationAsync(string entityType, Guid entityId, Guid authorUserId, string anchorJson, string body, CancellationToken ct = default)
    {
        var row = new FcmsContentAnnotation
        {
            EntityType = entityType,
            EntityId = entityId,
            AuthorUserId = authorUserId,
            AnchorJson = anchorJson ?? "{}",
            Body = body ?? "",
        };
        await _annotations.AddAsync(row, ct);
        await _uow.SaveChangesAsync(ct);
        return row;
    }

    public async Task ResolveAnnotationAsync(Guid annotationId, Guid resolvedByUserId, CancellationToken ct = default)
    {
        var ann = await _annotations.GetByIdAsync(annotationId, ct);
        if (ann is null) return;
        ann.IsResolved = true;
        ann.ResolvedAt = FcmsTime.Now;
        ann.ResolvedByUserId = resolvedByUserId;
        await _annotations.UpdateAsync(ann, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
