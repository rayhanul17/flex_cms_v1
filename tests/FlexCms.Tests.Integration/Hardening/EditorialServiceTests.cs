using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Editorial;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlexCms.Tests.Integration.Hardening;

/// <summary>
/// Workflow round-trip: submit → approve / request-changes / reject; verify
/// auto-publish only flips IsPublished when the toggle is set + the entity
/// existed; verify GetLatestAsync returns the most recent review.
/// </summary>
public class EditorialServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly EditorialService _svc;

    public EditorialServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new EditorialService(
            new EfRepository<FcmsContentReview>(_db),
            new EfRepository<FcmsContentAnnotation>(_db),
            new EfRepository<FcmsPage>(_db),
            new EfRepository<FcmsPost>(_db),
            new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    private async Task<FcmsPost> SeedPostAsync(bool published = false)
    {
        var post = new FcmsPost { Id = Guid.NewGuid(), Title = "Hello", Slug = "hello", Content = "Body", IsPublished = published };
        _db.Posts.Add(post);
        await _db.SaveChangesAsync();
        return post;
    }

    [Fact]
    public async Task SubmitForReview_creates_a_Submitted_row()
    {
        var post = await SeedPostAsync();
        var author = Guid.NewGuid();
        var review = await _svc.SubmitForReviewAsync(nameof(FcmsPost), post.Id, author, assignToUserId: null, autoPublish: false);
        Assert.Equal(ReviewStatus.Submitted, review.ReviewStatus);
        Assert.Equal(author, review.SubmittedByUserId);
    }

    [Fact]
    public async Task ApproveAsync_with_AutoPublish_flips_IsPublished_on_FcmsPost()
    {
        var post = await SeedPostAsync(published: false);
        var author = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var review = await _svc.SubmitForReviewAsync(nameof(FcmsPost), post.Id, author, null, autoPublish: true);

        await _svc.ApproveAsync(review.Id, reviewer, "Looks good");

        var refreshed = await _db.Posts.FirstAsync(p => p.Id == post.Id);
        Assert.True(refreshed.IsPublished);
        Assert.NotNull(refreshed.PublishedAt);

        var latest = await _svc.GetLatestAsync(nameof(FcmsPost), post.Id);
        Assert.Equal(ReviewStatus.Approved, latest!.ReviewStatus);
        Assert.Equal("Looks good", latest.ReviewerComment);
    }

    [Fact]
    public async Task ApproveAsync_without_AutoPublish_leaves_post_unpublished()
    {
        var post = await SeedPostAsync(published: false);
        var review = await _svc.SubmitForReviewAsync(nameof(FcmsPost), post.Id,
            Guid.NewGuid(), null, autoPublish: false);

        await _svc.ApproveAsync(review.Id, Guid.NewGuid(), null);

        var refreshed = await _db.Posts.FirstAsync(p => p.Id == post.Id);
        Assert.False(refreshed.IsPublished);
    }

    [Fact]
    public async Task RequestChangesAsync_marks_review_and_keeps_history()
    {
        var post = await SeedPostAsync();
        var review = await _svc.SubmitForReviewAsync(nameof(FcmsPost), post.Id,
            Guid.NewGuid(), null, autoPublish: false);

        await _svc.RequestChangesAsync(review.Id, Guid.NewGuid(), "Fix the second paragraph");

        var rows = await _db.ContentReviews.ToListAsync();
        // Same row updated — the original SubmitForReview row is the only one.
        Assert.Single(rows);
        Assert.Equal(ReviewStatus.ChangesRequested, rows[0].ReviewStatus);
        Assert.Equal("Fix the second paragraph", rows[0].ReviewerComment);
    }

    [Fact]
    public async Task RejectAsync_marks_terminal_state()
    {
        var post = await SeedPostAsync();
        var review = await _svc.SubmitForReviewAsync(nameof(FcmsPost), post.Id,
            Guid.NewGuid(), null, autoPublish: false);

        await _svc.RejectAsync(review.Id, Guid.NewGuid(), "Off-topic");
        var latest = await _svc.GetLatestAsync(nameof(FcmsPost), post.Id);
        Assert.Equal(ReviewStatus.Rejected, latest!.ReviewStatus);
    }

    [Fact]
    public async Task GetLatestAsync_returns_most_recent_review_for_entity()
    {
        // Author submits → editor requests changes → author resubmits — second
        // SubmitForReview creates a SECOND row; GetLatestAsync should return it.
        var post = await SeedPostAsync();
        var author = Guid.NewGuid();

        var first = await _svc.SubmitForReviewAsync(nameof(FcmsPost), post.Id, author, null, false);
        await _svc.RequestChangesAsync(first.Id, Guid.NewGuid(), "Tighten");
        // Sleep one tick so CreatedAt diverges (CreatedAt is the order key).
        await Task.Delay(15);
        var second = await _svc.SubmitForReviewAsync(nameof(FcmsPost), post.Id, author, null, false);

        var latest = await _svc.GetLatestAsync(nameof(FcmsPost), post.Id);
        Assert.Equal(second.Id, latest!.Id);
        Assert.Equal(ReviewStatus.Submitted, latest.ReviewStatus);
    }

    [Fact]
    public async Task AddAnnotationAsync_then_ResolveAnnotationAsync()
    {
        var post = await SeedPostAsync();
        var author = Guid.NewGuid();

        var ann = await _svc.AddAnnotationAsync(nameof(FcmsPost), post.Id, author,
            "{\"start\":0,\"end\":10}", "Rephrase");
        Assert.False(ann.IsResolved);

        await _svc.ResolveAnnotationAsync(ann.Id, author);

        var rows = await _svc.GetAnnotationsAsync(nameof(FcmsPost), post.Id);
        Assert.Single(rows);
        Assert.True(rows[0].IsResolved);
        Assert.NotNull(rows[0].ResolvedAt);
    }
}
