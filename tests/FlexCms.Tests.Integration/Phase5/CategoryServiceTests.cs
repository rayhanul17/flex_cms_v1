using FlexCms.Framework.Db;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase5;

public class CategoryServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly CategoryService _svc;

    public CategoryServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new CategoryService(new EfRepository<FcmsCategory>(_db), new EfRepository<FcmsPost>(_db), new EfUnitOfWork(_db), Substitute.For<IFcmsLogService>());
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_stores_category()
    {
        var cat = await _svc.CreateAsync(new FcmsCategory { Name = "Tech", Slug = "tech" });

        Assert.NotEqual(Guid.Empty, cat.Id);
        Assert.Equal(1, await _db.Categories.CountAsync());
    }

    [Fact]
    public async Task GetBySlugAsync_returns_correct_category()
    {
        await _svc.CreateAsync(new FcmsCategory { Name = "Tech", Slug = "tech" });

        var found = await _svc.GetBySlugAsync("tech");

        Assert.NotNull(found);
        Assert.Equal("Tech", found.Name);
    }

    [Fact]
    public async Task DeleteAsync_soft_deletes_and_hides_from_queries()
    {
        var cat = await _svc.CreateAsync(new FcmsCategory { Name = "Gone", Slug = "gone" });
        await _svc.DeleteAsync(cat.Id);

        Assert.Null(await _svc.GetByIdAsync(cat.Id));
        Assert.Empty(await _svc.GetAllAsync());
    }

    [Fact]
    public async Task GetPostCountAsync_returns_correct_count()
    {
        var cat = await _svc.CreateAsync(new FcmsCategory { Name = "Tech", Slug = "tech" });
        _db.Posts.Add(new FcmsPost { Title = "P1", Slug = "p1", Content = "", CategoryId = cat.Id });
        _db.Posts.Add(new FcmsPost { Title = "P2", Slug = "p2", Content = "", CategoryId = cat.Id });
        await _db.SaveChangesAsync();

        var count = await _svc.GetPostCountAsync(cat.Id);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetPostCountAsync_excludes_deleted_posts()
    {
        var cat = await _svc.CreateAsync(new FcmsCategory { Name = "Tech2", Slug = "tech2" });
        var post = new FcmsPost { Title = "P1", Slug = "p1b", Content = "", CategoryId = cat.Id };
        var deleted = new FcmsPost { Title = "P2", Slug = "p2b", Content = "", CategoryId = cat.Id, Status = EntityStatus.Deleted };
        _db.Posts.AddRange(post, deleted);
        await _db.SaveChangesAsync();

        var count = await _svc.GetPostCountAsync(cat.Id);

        Assert.Equal(1, count);
    }
}
