using FlexCms.Framework.Cms;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Tests.Integration.Phase5;

/// <summary>
/// Tests for ScheduledPublish and TrashCleanup logic.
/// Invokes the core DB queries directly — no real timer, no Docker.
/// Two DbContext instances on the same InMemory DB simulate the
/// scope-per-tick pattern used by the background services.
/// </summary>
public class ScheduledPublishTests : IDisposable
{
    private readonly DbContextOptions<FcmsDbContext> _opts;
    private readonly FcmsDbContext _db;

    public ScheduledPublishTests()
    {
        _opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(_opts);
    }

    public void Dispose() => _db.Dispose();

    private FcmsDbContext NewScope() => new(_opts);

    private async Task SimulatePublishTickAsync()
    {
        await using var db = NewScope();
        var now = FcmsTime.Now;
        var pages = await db.Pages
            .Where(p => p.Status != EntityStatus.Deleted && !p.IsPublished && p.PublishedAt != null && p.PublishedAt <= now)
            .ToListAsync();
        var posts = await db.Posts
            .Where(p => p.Status != EntityStatus.Deleted && !p.IsPublished && p.PublishedAt != null && p.PublishedAt <= now)
            .ToListAsync();
        foreach (var p in pages) { p.IsPublished = true; p.UpdatedAt = now; }
        foreach (var p in posts) { p.IsPublished = true; p.UpdatedAt = now; }
        if (pages.Count + posts.Count > 0) await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Page_with_past_PublishedAt_is_published()
    {
        var page = new FcmsPage
        {
            Title = "Scheduled",
            Slug = "scheduled-page",
            Content = "",
            IsPublished = false,
            PublishedAt = FcmsTime.Now.AddHours(-1)
        };
        _db.Pages.Add(page);
        await _db.SaveChangesAsync();

        await SimulatePublishTickAsync();

        await _db.Entry(page).ReloadAsync();
        Assert.True(page.IsPublished);
    }

    [Fact]
    public async Task Page_with_future_PublishedAt_is_not_published()
    {
        var page = new FcmsPage
        {
            Title = "Future",
            Slug = "future-page",
            Content = "",
            IsPublished = false,
            PublishedAt = FcmsTime.Now.AddHours(1)
        };
        _db.Pages.Add(page);
        await _db.SaveChangesAsync();

        await SimulatePublishTickAsync();

        await _db.Entry(page).ReloadAsync();
        Assert.False(page.IsPublished);
    }

    [Fact]
    public async Task Post_with_past_PublishedAt_is_published()
    {
        var post = new FcmsPost
        {
            Title = "Scheduled Post",
            Slug = "scheduled-post",
            Content = "",
            IsPublished = false,
            PublishedAt = FcmsTime.Now.AddHours(-2)
        };
        _db.Posts.Add(post);
        await _db.SaveChangesAsync();

        await SimulatePublishTickAsync();

        await _db.Entry(post).ReloadAsync();
        Assert.True(post.IsPublished);
    }

    [Fact]
    public async Task Already_published_page_is_not_touched()
    {
        var page = new FcmsPage
        {
            Title = "Already Live",
            Slug = "already-live",
            Content = "",
            IsPublished = true,
            PublishedAt = FcmsTime.Now.AddHours(-5)
        };
        _db.Pages.Add(page);
        await _db.SaveChangesAsync();

        var updatedAt = page.UpdatedAt;
        await SimulatePublishTickAsync();

        await _db.Entry(page).ReloadAsync();
        Assert.True(page.IsPublished);
        Assert.Equal(updatedAt, page.UpdatedAt); // unchanged
    }
}

public class TrashCleanupTests : IDisposable
{
    private readonly DbContextOptions<FcmsDbContext> _opts;
    private readonly FcmsDbContext _db;

    public TrashCleanupTests()
    {
        _opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(_opts);
    }

    public void Dispose() => _db.Dispose();

    private async Task SimulatePurgeAsync(int retentionDays = 30)
    {
        await using var db = new FcmsDbContext(_opts);
        var cutoff = FcmsTime.Now.AddDays(-retentionDays);

        var oldPages = await db.Pages.IgnoreQueryFilters()
            .Where(p => p.Status == EntityStatus.Deleted && p.DeletedAt != null && p.DeletedAt < cutoff)
            .ToListAsync();

        var oldPosts = await db.Posts.IgnoreQueryFilters()
            .Where(p => p.Status == EntityStatus.Deleted && p.DeletedAt != null && p.DeletedAt < cutoff)
            .ToListAsync();

        if (oldPages.Count > 0) db.Pages.RemoveRange(oldPages);

        if (oldPosts.Count > 0)
        {
            var postIds = oldPosts.Select(p => p.Id).ToList();
            var tags = await db.PostTags.Where(pt => postIds.Contains(pt.PostId)).ToListAsync();
            db.PostTags.RemoveRange(tags);
            db.Posts.RemoveRange(oldPosts);
        }

        if (oldPages.Count + oldPosts.Count > 0) await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Pages_older_than_retention_are_hard_deleted()
    {
        var old = new FcmsPage
        {
            Title = "Old",
            Slug = "old-page",
            Content = "",
            Status = EntityStatus.Deleted,
            DeletedAt = DateTime.UtcNow.AddDays(-31)
        };
        _db.Pages.Add(old);
        await _db.SaveChangesAsync();

        await SimulatePurgeAsync(30);

        Assert.Equal(0, await _db.Pages.IgnoreQueryFilters().CountAsync(p => p.Id == old.Id));
    }

    [Fact]
    public async Task Pages_within_retention_are_kept()
    {
        var recent = new FcmsPage
        {
            Title = "Recent",
            Slug = "recent-page",
            Content = "",
            Status = EntityStatus.Deleted,
            DeletedAt = DateTime.UtcNow.AddDays(-5)
        };
        _db.Pages.Add(recent);
        await _db.SaveChangesAsync();

        await SimulatePurgeAsync(30);

        Assert.Equal(1, await _db.Pages.IgnoreQueryFilters().CountAsync(p => p.Id == recent.Id));
    }

    [Fact]
    public async Task Post_hard_delete_also_removes_PostTags()
    {
        var post = new FcmsPost
        {
            Title = "Del Post",
            Slug = "del-post-cleanup",
            Content = "",
            Status = EntityStatus.Deleted,
            DeletedAt = DateTime.UtcNow.AddDays(-40)
        };
        _db.Posts.Add(post);
        var tag = new FcmsTag { Name = "Tag", Slug = "tag" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();

        _db.PostTags.Add(new FcmsPostTag { PostId = post.Id, TagId = tag.Id });
        await _db.SaveChangesAsync();

        await SimulatePurgeAsync(30);

        Assert.Equal(0, await _db.Posts.IgnoreQueryFilters().CountAsync(p => p.Id == post.Id));
        Assert.Equal(0, await _db.PostTags.CountAsync(pt => pt.PostId == post.Id));
    }

    [Fact]
    public async Task Live_pages_are_not_affected_by_purge()
    {
        var live = new FcmsPage { Title = "Live", Slug = "live-safe", Content = "", Status = EntityStatus.Active };
        _db.Pages.Add(live);
        await _db.SaveChangesAsync();

        await SimulatePurgeAsync(30);

        Assert.Equal(1, await _db.Pages.CountAsync(p => p.Id == live.Id));
    }
}
