using FlexCms.Framework.Db;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Tests.Unit.Phase1;

/// <summary>
/// Verifies FcmsDbContext audit fields (CreatedAt, UpdatedAt) and soft-delete filter.
/// Uses EF InMemory — no Docker required.
/// </summary>
public class AuditTrailTests : IDisposable
{
    private readonly FcmsDbContext _db;

    public AuditTrailTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
    }

    public void Dispose() => _db.Dispose();

    // ── CreatedAt / UpdatedAt ─────────────────────────────────────────────────

    [Fact]
    public async Task SaveChangesAsync_sets_UpdatedAt_on_modify()
    {
        var page = new FcmsPage { Title = "T", Slug = "t2", Content = "" };
        _db.Pages.Add(page);
        await _db.SaveChangesAsync();

        var createdAt = page.CreatedAt;

        await Task.Delay(5);
        page.Title = "Updated";
        _db.Pages.Update(page);
        await _db.SaveChangesAsync();

        Assert.Equal(createdAt, page.CreatedAt); // CreatedAt must not change
        Assert.True(page.UpdatedAt >= createdAt);
    }

    // ── Soft-delete global query filter ──────────────────────────────────────

    [Fact]
    public async Task SoftDeleted_entity_is_filtered_from_normal_queries()
    {
        var page = new FcmsPage { Title = "T", Slug = "sd", Content = "" };
        _db.Pages.Add(page);
        await _db.SaveChangesAsync();

        page.Status = EntityStatus.Deleted;
        _db.Pages.Update(page);
        await _db.SaveChangesAsync();

        var found = await _db.Pages.FirstOrDefaultAsync(p => p.Id == page.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task SoftDeleted_entity_visible_with_IgnoreQueryFilters()
    {
        var page = new FcmsPage { Title = "T", Slug = "sd2", Content = "" };
        _db.Pages.Add(page);
        await _db.SaveChangesAsync();

        page.Status = EntityStatus.Deleted;
        _db.Pages.Update(page);
        await _db.SaveChangesAsync();

        var found = await _db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == page.Id);
        Assert.NotNull(found);
        Assert.Equal(EntityStatus.Deleted, found.Status);
    }

    [Fact]
    public async Task SoftDeleted_entity_count_matches_IgnoreQueryFilters()
    {
        var p1 = new FcmsPage { Title = "A", Slug = "a", Content = "" };
        var p2 = new FcmsPage { Title = "B", Slug = "b", Content = "" };
        _db.Pages.AddRange(p1, p2);
        await _db.SaveChangesAsync();

        p1.Status = EntityStatus.Deleted;
        _db.Pages.Update(p1);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _db.Pages.CountAsync()); // filter active
        Assert.Equal(2, await _db.Pages.IgnoreQueryFilters().CountAsync()); // all
    }
}
