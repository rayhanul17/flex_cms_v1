using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase5;

public class PostServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly PostService _svc;

    public PostServiceTests()
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
    public async Task CreateAsync_stores_post_with_no_tags()
    {
        var post = new FcmsPost { Title = "Hello World", Slug = "hello-world", Content = "Body" };
        var result = await _svc.CreateAsync(post, []);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(1, await _db.Posts.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_creates_and_links_tags()
    {
        var post = new FcmsPost { Title = "Tagged", Slug = "tagged", Content = "" };
        await _svc.CreateAsync(post, ["dotnet", "csharp"]);

        var tags = await _db.Tags.Where(t => t.Status != EntityStatus.Deleted).ToListAsync();
        var postTags = await _db.PostTags.ToListAsync();

        Assert.Equal(2, tags.Count);
        Assert.Equal(2, postTags.Count);
    }

    [Fact]
    public async Task CreateAsync_reuses_existing_tags()
    {
        _db.Tags.Add(new FcmsTag { Name = "Dotnet", Slug = "dotnet" });
        await _db.SaveChangesAsync();

        var post = new FcmsPost { Title = "Reuse", Slug = "reuse", Content = "" };
        await _svc.CreateAsync(post, ["dotnet", "csharp"]);

        Assert.Equal(2, await _db.Tags.CountAsync(t => t.Status != EntityStatus.Deleted));
    }

    [Fact]
    public async Task UpdateAsync_replaces_tags()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "Upd", Slug = "upd", Content = "" }, ["old-tag"]);

        post.Title = "Updated";
        await _svc.UpdateAsync(post, ["new-tag"]);

        var postTags = await _db.PostTags.Where(pt => pt.PostId == post.Id).ToListAsync();
        Assert.Single(postTags);
        var tag = await _db.Tags.FindAsync(postTags[0].TagId);
        Assert.Equal("new-tag", tag!.Slug);
    }

    [Fact]
    public async Task GetPublishedAsync_excludes_drafts()
    {
        await _svc.CreateAsync(new FcmsPost { Title = "Draft", Slug = "draft", Content = "", IsPublished = false }, []);
        await _svc.CreateAsync(new FcmsPost { Title = "Live", Slug = "live", Content = "", IsPublished = true, PublishedAt = DateTime.UtcNow }, []);

        var published = await _svc.GetPublishedAsync();

        Assert.Single(published);
        Assert.Equal("Live", published[0].Title);
    }

    [Fact]
    public async Task IncrementViewCountAsync_increments_count()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "Viewed", Slug = "viewed", Content = "" }, []);

        await _svc.IncrementViewCountAsync(post.Id);
        await _svc.IncrementViewCountAsync(post.Id);

        var updated = await _svc.GetByIdAsync(post.Id);
        Assert.Equal(2, updated!.ViewCount);
    }

    [Fact]
    public async Task GetTagSlugsAsync_returns_slugs_for_post()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "Tagged", Slug = "tagged", Content = "" }, ["dotnet", "csharp"]);

        var slugs = await _svc.GetTagSlugsAsync(post.Id);

        Assert.Equal(2, slugs.Count);
        Assert.Contains("dotnet", slugs);
        Assert.Contains("csharp", slugs);
    }

    [Fact]
    public async Task GetTagSlugsAsync_returns_empty_for_post_without_tags()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "NoTags", Slug = "no-tags", Content = "" }, []);

        var slugs = await _svc.GetTagSlugsAsync(post.Id);

        Assert.Empty(slugs);
    }

    [Fact]
    public async Task DeleteAsync_soft_deletes_post()
    {
        var post = await _svc.CreateAsync(new FcmsPost { Title = "Del", Slug = "del", Content = "" }, []);
        await _svc.DeleteAsync(post.Id);

        Assert.Null(await _svc.GetByIdAsync(post.Id));
        Assert.Equal(1, await _db.Posts.IgnoreQueryFilters().CountAsync(p => p.Status == EntityStatus.Deleted));
    }

    [Fact]
    public async Task GetBySlugAsync_includes_category_and_tags()
    {
        var cat = new FcmsCategory { Name = "Tech", Slug = "tech" };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();

        var post = new FcmsPost { Title = "Post", Slug = "post", Content = "", CategoryId = cat.Id };
        await _svc.CreateAsync(post, ["dotnet"]);

        var found = await _svc.GetBySlugAsync("post");

        Assert.NotNull(found);
        Assert.NotNull(found.Category);
        Assert.Equal("Tech", found.Category!.Name);
        Assert.Single(found.PostTags);
        Assert.Equal("dotnet", found.PostTags.First().Tag.Slug);
    }
}
