using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Tests.Integration.Phase6;

/// <summary>
/// Integration tests for MediaFolderService — create, rename, delete (with reparenting), breadcrumb.
/// EfRepository follows UoW pattern (no auto-save), so tests call _db.SaveChangesAsync()
/// after each service write to mirror what the controller/UoW would do.
/// </summary>
public class MediaFolderServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly MediaFolderService _svc;

    public MediaFolderServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);

        var folderRepo = new EfRepository<FcmsMediaFolder>(_db);
        var mediaRepo = new EfRepository<FcmsMedia>(_db);
        _svc = new MediaFolderService(folderRepo, mediaRepo);
    }

    public void Dispose() => _db.Dispose();

    private Task Save() => _db.SaveChangesAsync();

    [Fact]
    public async Task CreateAsync_stores_folder_with_trimmed_name()
    {
        var folder = await _svc.CreateAsync("  Photos  ", null);
        await Save();

        Assert.NotEqual(Guid.Empty, folder.Id);
        Assert.Equal("Photos", folder.Name);
        Assert.Null(folder.ParentId);
    }

    [Fact]
    public async Task CreateAsync_nested_folder_sets_parent_id()
    {
        var parent = await _svc.CreateAsync("Root", null);
        await Save();
        var child = await _svc.CreateAsync("Child", parent.Id);
        await Save();

        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public async Task RenameAsync_updates_name()
    {
        var folder = await _svc.CreateAsync("OldName", null);
        await Save();
        await _svc.RenameAsync(folder.Id, "NewName");
        await Save();

        var updated = await _db.Set<FcmsMediaFolder>().FindAsync(folder.Id);
        Assert.Equal("NewName", updated!.Name);
    }

    [Fact]
    public async Task RenameAsync_nonexistent_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.RenameAsync(Guid.NewGuid(), "X"));
    }

    [Fact]
    public async Task DeleteAsync_soft_deletes_folder()
    {
        var folder = await _svc.CreateAsync("ToDelete", null);
        await Save();
        await _svc.DeleteAsync(folder.Id);
        await Save();

        var raw = await _db.Set<FcmsMediaFolder>()
            .IgnoreQueryFilters()
            .FirstAsync(f => f.Id == folder.Id);
        Assert.True(raw.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_reparents_media_to_parent_folder()
    {
        var parent = await _svc.CreateAsync("Parent", null);
        await Save();
        var child = await _svc.CreateAsync("Child", parent.Id);
        await Save();

        var media = new FcmsMedia
        {
            FileName = "f.pdf",
            OriginalFileName = "f.pdf",
            MimeType = "application/pdf",
            Extension = ".pdf",
            Url = "/f.pdf",
            FolderId = child.Id
        };
        _db.Set<FcmsMedia>().Add(media);
        await Save();

        await _svc.DeleteAsync(child.Id);
        await Save();

        var moved = await _db.Set<FcmsMedia>().FindAsync(media.Id);
        Assert.Equal(parent.Id, moved!.FolderId);
    }

    [Fact]
    public async Task DeleteAsync_media_in_root_folder_moves_to_root()
    {
        var folder = await _svc.CreateAsync("RootChild", null);
        await Save();
        var media = new FcmsMedia
        {
            FileName = "g.pdf",
            OriginalFileName = "g.pdf",
            MimeType = "application/pdf",
            Extension = ".pdf",
            Url = "/g.pdf",
            FolderId = folder.Id
        };
        _db.Set<FcmsMedia>().Add(media);
        await Save();

        await _svc.DeleteAsync(folder.Id);
        await Save();

        var moved = await _db.Set<FcmsMedia>().FindAsync(media.Id);
        Assert.Null(moved!.FolderId);
    }

    [Fact]
    public async Task DeleteAsync_nonexistent_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAllAsync_excludes_deleted_folders()
    {
        await _svc.CreateAsync("Keep", null);
        await Save();
        var del = await _svc.CreateAsync("Delete", null);
        await Save();
        await _svc.DeleteAsync(del.Id);
        await Save();

        var all = await _svc.GetAllAsync();
        Assert.Single(all, f => f.Name == "Keep");
        Assert.DoesNotContain(all, f => f.Name == "Delete");
    }

    [Fact]
    public async Task GetBreadcrumbAsync_returns_ordered_ancestors()
    {
        var root = await _svc.CreateAsync("Root", null);
        await Save();
        var mid = await _svc.CreateAsync("Mid", root.Id);
        await Save();
        var leaf = await _svc.CreateAsync("Leaf", mid.Id);
        await Save();

        var crumb = await _svc.GetBreadcrumbAsync(leaf.Id);

        Assert.Equal(3, crumb.Count);
        Assert.Equal("Root", crumb[0].Name);
        Assert.Equal("Mid", crumb[1].Name);
        Assert.Equal("Leaf", crumb[2].Name);
    }

    [Fact]
    public async Task GetBreadcrumbAsync_single_folder_returns_itself()
    {
        var folder = await _svc.CreateAsync("Solo", null);
        await Save();
        var crumb = await _svc.GetBreadcrumbAsync(folder.Id);
        Assert.Single(crumb);
        Assert.Equal("Solo", crumb[0].Name);
    }

    [Fact]
    public async Task GetBreadcrumbAsync_nonexistent_returns_empty()
    {
        var crumb = await _svc.GetBreadcrumbAsync(Guid.NewGuid());
        Assert.Empty(crumb);
    }
}
