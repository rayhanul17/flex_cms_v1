using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Tests.Integration.Phase6;

/// <summary>
/// Integration tests for the new EfRepository methods added in Phase 6:
/// GetByIdsAsync, UpdateRangeAsync, SoftDeleteRangeAsync, FindAsync(QueryFilter),
/// FindPagedAsync(QueryFilter), FindPagedAsync(predicate+orderBy+page+pageSize).
/// Uses EF InMemory with FcmsMedia as the test entity.
/// </summary>
public class EfRepositoryExtTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly EfRepository<FcmsMedia> _repo;

    public EfRepositoryExtTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
        _repo = new EfRepository<FcmsMedia>(_db);
    }

    public void Dispose() => _db.Dispose();

    // ── GetByIdsAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdsAsync_returns_matching_entities()
    {
        var a = await AddAsync("a.pdf");
        var b = await AddAsync("b.pdf");
        await AddAsync("c.pdf");

        var result = await _repo.GetByIdsAsync([a.Id, b.Id]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.Id == a.Id);
        Assert.Contains(result, m => m.Id == b.Id);
    }

    [Fact]
    public async Task GetByIdsAsync_excludes_soft_deleted()
    {
        var a = await AddAsync("a.pdf");
        a.Status = EntityStatus.Deleted;
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdsAsync([a.Id]);

        Assert.Empty(result);
    }

    // ── UpdateRangeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRangeAsync_persists_all_changes()
    {
        var a = await AddAsync("x.pdf");
        var b = await AddAsync("y.pdf");
        a.AltText = "updated-A";
        b.AltText = "updated-B";

        await _repo.UpdateRangeAsync([a, b]);

        var ra = await _db.Set<FcmsMedia>().FindAsync(a.Id);
        var rb = await _db.Set<FcmsMedia>().FindAsync(b.Id);
        Assert.Equal("updated-A", ra!.AltText);
        Assert.Equal("updated-B", rb!.AltText);
    }

    // ── SoftDeleteRangeAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeleteRangeAsync_marks_all_deleted()
    {
        var a = await AddAsync("del1.pdf");
        var b = await AddAsync("del2.pdf");

        await _repo.SoftDeleteRangeAsync([a, b]);

        var ra = await _db.Set<FcmsMedia>().IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == a.Id);
        var rb = await _db.Set<FcmsMedia>().IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == b.Id);
        Assert.Equal(EntityStatus.Deleted, ra!.Status);
        Assert.Equal(EntityStatus.Deleted, rb!.Status);
    }

    [Fact]
    public async Task SoftDeleteRangeAsync_hides_from_normal_queries()
    {
        var a = await AddAsync("hide1.pdf");
        await _repo.SoftDeleteRangeAsync([a]);
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdsAsync([a.Id]);
        Assert.Empty(result);
    }

    // ── FindAsync(QueryFilter) ────────────────────────────────────────────────

    [Fact]
    public async Task FindAsync_QueryFilter_applies_where_condition()
    {
        var folderId = Guid.NewGuid();
        await AddAsync("f1.pdf", folderId);
        await AddAsync("f2.pdf", folderId);
        await AddAsync("f3.pdf", Guid.NewGuid());

        var filter = new QueryFilter<FcmsMedia>().Where(m => m.FolderId == folderId);
        var result = await _repo.FindAsync(filter);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task FindAsync_QueryFilter_applies_ordering()
    {
        await AddAsync("z.pdf");
        await AddAsync("a.pdf");

        var filter = new QueryFilter<FcmsMedia>().OrderBy(m => m.FileName);
        var result = await _repo.FindAsync(filter);

        Assert.Equal("a.pdf", result[0].FileName);
        Assert.Equal("z.pdf", result[1].FileName);
    }

    [Fact]
    public async Task FindAsync_QueryFilter_applies_paging()
    {
        for (int i = 0; i < 5; i++) await AddAsync($"file{i}.pdf");

        var filter = new QueryFilter<FcmsMedia>()
            .OrderBy(m => m.FileName)
            .Page(2, 2);
        var result = await _repo.FindAsync(filter);

        Assert.Equal(2, result.Count);
    }

    // ── FindPagedAsync(QueryFilter) ───────────────────────────────────────────

    [Fact]
    public async Task FindPagedAsync_QueryFilter_returns_correct_page_and_total()
    {
        for (int i = 0; i < 7; i++) await AddAsync($"p{i}.pdf");

        var filter = new QueryFilter<FcmsMedia>()
            .OrderBy(m => m.FileName)
            .Page(2, 3);
        var paged = await _repo.FindPagedAsync(filter);

        Assert.Equal(7, paged.Total);
        Assert.Equal(3, paged.Items.Count);
        Assert.Equal(2, paged.Page);
        Assert.Equal(3, paged.TotalPages);
        Assert.True(paged.HasPreviousPage);
        Assert.True(paged.HasNextPage);
    }

    [Fact]
    public async Task FindPagedAsync_QueryFilter_last_page_has_no_next()
    {
        for (int i = 0; i < 5; i++) await AddAsync($"q{i}.pdf");

        var filter = new QueryFilter<FcmsMedia>().OrderBy(m => m.FileName).Page(3, 2);
        var paged = await _repo.FindPagedAsync(filter);

        Assert.Single(paged.Items); // 5 total, page 3 of 2 = 1 item
        Assert.False(paged.HasNextPage);
    }

    // ── FindPagedAsync(predicate+orderBy) ─────────────────────────────────────

    [Fact]
    public async Task FindPagedAsync_predicate_overload_filters_and_pages()
    {
        var folderId = Guid.NewGuid();
        for (int i = 0; i < 4; i++) await AddAsync($"folder{i}.pdf", folderId);
        await AddAsync("other.pdf", Guid.NewGuid());

        var paged = await _repo.FindPagedAsync(
            m => m.FolderId == folderId,
            m => m.FileName,
            page: 1, pageSize: 2);

        Assert.Equal(4, paged.Total);
        Assert.Equal(2, paged.Items.Count);
    }

    // ── CountAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountAsync_counts_non_deleted()
    {
        await AddAsync("c1.pdf");
        var del = await AddAsync("c2.pdf");
        del.Status = EntityStatus.Deleted;
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _repo.CountAsync());
    }

    // ── ExistsAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_returns_true_when_match()
    {
        await AddAsync("exist.pdf");
        Assert.True(await _repo.ExistsAsync(m => m.FileName == "exist.pdf"));
    }

    [Fact]
    public async Task ExistsAsync_returns_false_when_no_match()
    {
        Assert.False(await _repo.ExistsAsync(m => m.FileName == "ghost.pdf"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<FcmsMedia> AddAsync(string fileName, Guid? folderId = null)
    {
        var media = new FcmsMedia
        {
            FileName = fileName,
            OriginalFileName = fileName,
            MimeType = "application/pdf",
            Extension = ".pdf",
            Url = "/" + fileName,
            FolderId = folderId
        };
        _db.Set<FcmsMedia>().Add(media);
        await _db.SaveChangesAsync();
        return media;
    }
}
