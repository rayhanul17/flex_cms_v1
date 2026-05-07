using FlexCms.Framework.Cms.Drafts;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlexCms.Tests.Integration.Hardening;

/// <summary>
/// Verifies the autosave upsert semantics: one row per (entityType,
/// entityId, userId), repeat saves overwrite in place, GetAsync returns
/// the latest snapshot, DiscardAsync removes it. EF InMemory; same
/// patterns the FcmsLogService tests use.
/// </summary>
public class DraftSnapshotServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly DraftSnapshotService _svc;

    public DraftSnapshotServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new DraftSnapshotService(
            new EfRepository<FcmsContentDraftSnapshot>(_db),
            new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SaveAsync_inserts_when_no_existing_snapshot()
    {
        var entityId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await _svc.SaveAsync("FcmsPost", entityId, userId,
            new DraftSnapshotPayload("Hello", "World", "excerpt"));
        var snap = await _svc.GetAsync("FcmsPost", entityId, userId);
        Assert.NotNull(snap);
        Assert.Equal("Hello", snap!.Title);
        Assert.Equal("World", snap.Content);
    }

    [Fact]
    public async Task SaveAsync_overwrites_existing_snapshot_for_same_user()
    {
        var entityId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await _svc.SaveAsync("FcmsPost", entityId, userId, new DraftSnapshotPayload("v1", "v1", null));
        await _svc.SaveAsync("FcmsPost", entityId, userId, new DraftSnapshotPayload("v2", "v2", null));
        // One row per (entity, user) — second save updates in place.
        var rows = await _db.ContentDraftSnapshots.ToListAsync();
        Assert.Single(rows);
        Assert.Equal("v2", rows[0].Title);
    }

    [Fact]
    public async Task SaveAsync_isolates_snapshots_per_user()
    {
        var entityId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await _svc.SaveAsync("FcmsPost", entityId, userA, new DraftSnapshotPayload("Alice", "A", null));
        await _svc.SaveAsync("FcmsPost", entityId, userB, new DraftSnapshotPayload("Bob", "B", null));

        var snapA = await _svc.GetAsync("FcmsPost", entityId, userA);
        var snapB = await _svc.GetAsync("FcmsPost", entityId, userB);
        Assert.Equal("Alice", snapA!.Title);
        Assert.Equal("Bob", snapB!.Title);
    }

    [Fact]
    public async Task DiscardAsync_removes_the_snapshot()
    {
        var entityId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await _svc.SaveAsync("FcmsPost", entityId, userId, new DraftSnapshotPayload("x", "y", null));
        await _svc.DiscardAsync("FcmsPost", entityId, userId);
        Assert.Null(await _svc.GetAsync("FcmsPost", entityId, userId));
    }

    [Fact]
    public async Task SaveAsync_with_empty_entityType_is_a_noop()
    {
        // Defensive guard against callers passing default values from a
        // malformed POST payload.
        await _svc.SaveAsync("", Guid.NewGuid(), Guid.NewGuid(), new DraftSnapshotPayload("x", "y", null));
        Assert.Empty(await _db.ContentDraftSnapshots.ToListAsync());
    }
}
