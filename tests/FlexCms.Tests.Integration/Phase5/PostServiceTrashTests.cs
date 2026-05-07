using FlexCms.Framework.Db;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase5;

/// <summary>
/// Tests for PostService trash (soft-delete, restore, hard-delete, GetDeletedAsync).
/// </summary>
public class PostServiceTrashTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly PostService _svc;

    public PostServiceTrashTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new PostService(
            new EfRepository<FcmsPost>(_db),
            new EfRepository<FcmsTag>(_db),
            new EfRepository<FcmsPostTag>(_db),
            new EfRepository<FcmsPostTranslation>(_db),
            new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetDeletedAsync_returns_only_soft_deleted_posts()
    {
        await _svc.CreateAsync(new FcmsPost { Title = "Live", Slug = "live-p", Content = "" }, []);
        var del = await _svc.CreateAsync(new FcmsPost { Title = "Deleted", Slug = "deleted-p", Content = "" }, []);
        await _svc.DeleteAsync(del.Id);

        var trash = await _svc.GetDeletedAsync();

        Assert.Single(trash);
        Assert.Equal(del.Id, trash[0].Id);
    }

    [Fact]
    public async Task RestoreAsync_makes_post_visible_as_draft()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "Restore", Slug = "restore-p", Content = "", IsPublished = true }, []);
        await _svc.DeleteAsync(post.Id);

        await _svc.RestoreAsync(post.Id);

        var found = await _svc.GetByIdAsync(post.Id);
        Assert.NotNull(found);
        Assert.NotEqual(EntityStatus.Deleted, found.Status);
        Assert.False(found.IsPublished);
        Assert.Null(found.DeletedAt);
    }

    [Fact]
    public async Task HardDeleteAsync_removes_post_and_postTags()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "HardDel", Slug = "harddel", Content = "" }, ["dotnet", "csharp"]);
        await _svc.DeleteAsync(post.Id);
        await _svc.HardDeleteAsync(post.Id);

        Assert.Equal(0, await _db.Posts.IgnoreQueryFilters().CountAsync(p => p.Id == post.Id));
        Assert.Equal(0, await _db.PostTags.CountAsync(pt => pt.PostId == post.Id));
    }

    [Fact]
    public async Task HardDeleteAsync_on_nonexistent_does_not_throw()
    {
        await _svc.HardDeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task GetByCategoryAsync_returns_only_published_posts_in_category()
    {
        var cat = new FcmsCategory { Name = "Tech", Slug = "tech-cat" };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();

        await _svc.CreateAsync(new FcmsPost { Title = "Draft", Slug = "draft-cat", Content = "", CategoryId = cat.Id, IsPublished = false }, []);
        await _svc.CreateAsync(new FcmsPost { Title = "Live", Slug = "live-cat", Content = "", CategoryId = cat.Id, IsPublished = true, PublishedAt = DateTime.UtcNow }, []);

        var results = await _svc.GetByCategoryAsync(cat.Id);

        Assert.Single(results);
        Assert.Equal("Live", results[0].Title);
    }

    [Fact]
    public async Task SlugExistsAsync_excludes_own_id()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "Slug", Slug = "my-slug", Content = "" }, []);
        Assert.False(await _svc.SlugExistsAsync("my-slug", post.Id));
    }
}
