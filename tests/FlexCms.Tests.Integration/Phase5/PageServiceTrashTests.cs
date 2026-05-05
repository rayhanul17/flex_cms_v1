using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase5;

/// <summary>
/// Tests for PageService trash (soft-delete, restore, hard-delete, GetDeletedAsync).
/// </summary>
public class PageServiceTrashTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly PageService _svc;

    public PageServiceTrashTests()
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
    public async Task GetDeletedAsync_returns_only_soft_deleted_pages()
    {
        await _svc.CreateAsync(new FcmsPage { Title = "Live", Slug = "live", Content = "" });
        var del = await _svc.CreateAsync(new FcmsPage { Title = "Deleted", Slug = "deleted", Content = "" });
        await _svc.DeleteAsync(del.Id);

        var trash = await _svc.GetDeletedAsync();

        Assert.Single(trash);
        Assert.Equal(del.Id, trash[0].Id);
    }

    [Fact]
    public async Task RestoreAsync_makes_page_visible_again_as_draft()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "Restore Me", Slug = "restore-me", Content = "", IsPublished = true });
        await _svc.DeleteAsync(page.Id);

        await _svc.RestoreAsync(page.Id);

        var found = await _svc.GetByIdAsync(page.Id);
        Assert.NotNull(found);
        Assert.False(found.IsDeleted);
        Assert.False(found.IsPublished); // restored as draft
        Assert.Null(found.DeletedAt);
    }

    [Fact]
    public async Task RestoreAsync_on_nonexistent_id_does_not_throw()
    {
        await _svc.RestoreAsync(Guid.NewGuid()); // should not throw
    }

    [Fact]
    public async Task HardDeleteAsync_removes_page_permanently()
    {
        var page = await _svc.CreateAsync(new FcmsPage { Title = "Gone", Slug = "gone", Content = "" });
        await _svc.DeleteAsync(page.Id);
        await _svc.HardDeleteAsync(page.Id);

        var inTrash = await _svc.GetDeletedAsync();
        Assert.DoesNotContain(inTrash, p => p.Id == page.Id);
        Assert.Equal(0, await _db.Pages.IgnoreQueryFilters().CountAsync(p => p.Id == page.Id));
    }

    [Fact]
    public async Task HardDeleteAsync_on_nonexistent_id_does_not_throw()
    {
        await _svc.HardDeleteAsync(Guid.NewGuid()); // should not throw
    }

    [Fact]
    public async Task DeleteAsync_sets_DeletedAt_timestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var page = await _svc.CreateAsync(new FcmsPage { Title = "Stamp", Slug = "stamp", Content = "" });
        await _svc.DeleteAsync(page.Id);

        var deleted = await _db.Pages.IgnoreQueryFilters().FirstAsync(p => p.Id == page.Id);
        Assert.NotNull(deleted.DeletedAt);
        Assert.True(deleted.DeletedAt >= before);
    }
}
