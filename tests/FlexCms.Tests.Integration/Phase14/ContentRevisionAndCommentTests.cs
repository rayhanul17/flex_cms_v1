using FlexCms.Framework.Cms.Comments;
using FlexCms.Framework.Cms.Revisions;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlexCms.Tests.Integration.Phase14;

public sealed class ContentRevisionAndCommentTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly ContentRevisionService _rev;
    private readonly CommentService _cmt;

    public ContentRevisionAndCommentTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _rev = new ContentRevisionService(new EfRepository<FcmsContentRevision>(_db), new EfUnitOfWork(_db));
        _cmt = new CommentService(new EfRepository<FcmsComment>(_db), new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SnapshotAsync_auto_increments_version_per_entity()
    {
        var pageId = Guid.NewGuid();
        var v1 = await _rev.SnapshotAsync("FcmsPage", pageId, "T1", "<p>v1</p>");
        var v2 = await _rev.SnapshotAsync("FcmsPage", pageId, "T2", "<p>v2</p>");
        var v3 = await _rev.SnapshotAsync("FcmsPage", pageId, "T3", "<p>v3</p>");

        Assert.Equal(1, v1.Version);
        Assert.Equal(2, v2.Version);
        Assert.Equal(3, v3.Version);
    }

    [Fact]
    public async Task SnapshotAsync_versions_are_per_entity_independently()
    {
        var page1 = Guid.NewGuid();
        var page2 = Guid.NewGuid();
        await _rev.SnapshotAsync("FcmsPage", page1, "x", "");
        await _rev.SnapshotAsync("FcmsPage", page1, "x", "");
        var p2v1 = await _rev.SnapshotAsync("FcmsPage", page2, "x", "");

        Assert.Equal(1, p2v1.Version);
    }

    [Fact]
    public async Task GetForAsync_returns_newest_first()
    {
        var pageId = Guid.NewGuid();
        await _rev.SnapshotAsync("FcmsPage", pageId, "T1", "");
        await _rev.SnapshotAsync("FcmsPage", pageId, "T2", "");
        await _rev.SnapshotAsync("FcmsPage", pageId, "T3", "");

        var list = await _rev.GetForAsync("FcmsPage", pageId);
        Assert.Equal(3, list[0].Version);
        Assert.Equal(2, list[1].Version);
        Assert.Equal(1, list[2].Version);
    }

    // ── Comments ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitAsync_clean_comment_lands_pending()
    {
        var c = await _cmt.SubmitAsync(new FcmsComment
        {
            EntityType = "FcmsPost",
            EntityId = Guid.NewGuid(),
            AuthorName = "Alice",
            Body = "Nice article."
        });
        Assert.Equal(CommentStatus.Pending, c.CommentStatus);
    }

    [Fact]
    public async Task SubmitAsync_spammy_comment_lands_spam()
    {
        var body = string.Join(" ", Enumerable.Range(0, 8).Select(i => $"https://x.com/{i}"));
        var c = await _cmt.SubmitAsync(new FcmsComment
        {
            EntityType = "FcmsPost",
            EntityId = Guid.NewGuid(),
            AuthorName = "Bot",
            Body = body
        });
        Assert.Equal(CommentStatus.Spam, c.CommentStatus);
        Assert.True(c.SpamScore >= 5);
    }

    [Fact]
    public async Task GetApprovedAsync_returns_only_approved_for_entity()
    {
        var post = Guid.NewGuid();
        var pending = await _cmt.SubmitAsync(new FcmsComment { EntityType = "FcmsPost", EntityId = post, Body = "p" });
        var approved = await _cmt.SubmitAsync(new FcmsComment { EntityType = "FcmsPost", EntityId = post, Body = "a" });
        await _cmt.SetStatusAsync(approved.Id, CommentStatus.Approved, null);

        var list = await _cmt.GetApprovedAsync("FcmsPost", post);
        Assert.Single(list);
        Assert.Equal(approved.Id, list[0].Id);
    }

    [Fact]
    public async Task SetStatusAsync_records_moderator_metadata()
    {
        var c = await _cmt.SubmitAsync(new FcmsComment { EntityType = "FcmsPost", EntityId = Guid.NewGuid(), Body = "p" });
        var mod = Guid.NewGuid();

        await _cmt.SetStatusAsync(c.Id, CommentStatus.Approved, mod);

        var reloaded = await _db.Comments.AsNoTracking().FirstAsync();
        Assert.Equal(CommentStatus.Approved, reloaded.CommentStatus);
        Assert.Equal(mod, reloaded.ModeratedByUserId);
        Assert.NotNull(reloaded.ModeratedAt);
    }
}
