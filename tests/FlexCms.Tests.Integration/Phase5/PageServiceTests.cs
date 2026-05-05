using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase5;

public class PageServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly PageService _svc;

    public PageServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new PageService(new EfRepository<FcmsPage>(_db), new EfUnitOfWork(_db), Substitute.For<IOperationLogService>());
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_stores_page_and_returns_with_id()
    {
        var page = new FcmsPage { Title = "About", Slug = "about", Content = "Hello" };
        var result = await _svc.CreateAsync(page);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("About", result.Title);
        Assert.Equal(1, await _db.Pages.CountAsync());
    }

    [Fact]
    public async Task GetBySlugAsync_returns_correct_page()
    {
        await _svc.CreateAsync(new FcmsPage { Title = "Home", Slug = "home", Content = "" });

        var found = await _svc.GetBySlugAsync("home");

        Assert.NotNull(found);
        Assert.Equal("Home", found.Title);
    }

    [Fact]
    public async Task SlugExistsAsync_returns_true_for_existing_slug()
    {
        await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "" });

        Assert.True(await _svc.SlugExistsAsync("about"));
    }

    [Fact]
    public async Task SlugExistsAsync_excludes_own_id()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "About", Slug = "about", Content = "" });

        Assert.False(await _svc.SlugExistsAsync("about", page.Id));
    }

    [Fact]
    public async Task GetPublishedAsync_returns_only_published_pages()
    {
        await _svc.CreateAsync(new FcmsPage { Title = "Draft", Slug = "draft", Content = "", IsPublished = false });
        await _svc.CreateAsync(new FcmsPage { Title = "Live", Slug = "live", Content = "", IsPublished = true });

        var published = await _svc.GetPublishedAsync();

        Assert.Single(published);
        Assert.Equal("Live", published[0].Title);
    }

    [Fact]
    public async Task GetChildrenAsync_returns_children_of_parent()
    {
        var parent = await _svc.CreateAsync(new FcmsPage { Title = "Parent", Slug = "parent", Content = "" });
        await _svc.CreateAsync(new FcmsPage { Title = "Child", Slug = "child", Content = "", ParentId = parent.Id });
        await _svc.CreateAsync(new FcmsPage { Title = "Other", Slug = "other", Content = "" });

        var children = await _svc.GetChildrenAsync(parent.Id);

        Assert.Single(children);
        Assert.Equal("Child", children[0].Title);
    }

    [Fact]
    public async Task DeleteAsync_soft_deletes_page()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "Bye", Slug = "bye", Content = "" });
        await _svc.DeleteAsync(page.Id);

        Assert.Null(await _svc.GetByIdAsync(page.Id));
        // Row still exists physically
        Assert.Equal(1, await _db.Pages.IgnoreQueryFilters().CountAsync(p => p.Status == EntityStatus.Deleted));
    }
}
