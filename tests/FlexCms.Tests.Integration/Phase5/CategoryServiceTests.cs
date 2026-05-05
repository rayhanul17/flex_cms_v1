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
        _svc = new CategoryService(new EfRepository<FcmsCategory>(_db), new EfRepository<FcmsPost>(_db), new EfUnitOfWork(_db), Substitute.For<IOperationLogService>());
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
}
