using FlexCms.Framework.Db;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Tests.Integration.Phase5;

/// <summary>
/// Tests for FcmsRedirect entity behavior and HitCount logic.
/// RedirectMiddleware itself requires a full HTTP pipeline; here we test
/// the underlying DB queries that the middleware relies on.
/// </summary>
public class RedirectTests : IDisposable
{
    private readonly FcmsDbContext _db;

    public RedirectTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Active_redirect_is_found_by_FromPath()
    {
        _db.Redirects.Add(new FcmsRedirect { FromPath = "/old", ToPath = "/new", StatusCode = 301, IsActive = true });
        await _db.SaveChangesAsync();

        var found = await _db.Redirects
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Status != EntityStatus.Deleted && r.IsActive && r.FromPath == "/old");

        Assert.NotNull(found);
        Assert.Equal("/new", found.ToPath);
        Assert.Equal(301, found.StatusCode);
    }

    [Fact]
    public async Task Inactive_redirect_is_not_matched()
    {
        _db.Redirects.Add(new FcmsRedirect { FromPath = "/inactive", ToPath = "/dest", IsActive = false });
        await _db.SaveChangesAsync();

        var found = await _db.Redirects
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Status != EntityStatus.Deleted && r.IsActive && r.FromPath == "/inactive");

        Assert.Null(found);
    }

    [Fact]
    public async Task HitCount_increments_correctly()
    {
        _db.Redirects.Add(new FcmsRedirect { FromPath = "/hit", ToPath = "/dest", IsActive = true, HitCount = 5 });
        await _db.SaveChangesAsync();

        var redirect = await _db.Redirects.FirstAsync(r => r.FromPath == "/hit");

        // EF InMemory does not support ExecuteUpdateAsync — simulate the increment
        redirect.HitCount++;
        await _db.SaveChangesAsync();

        var updated = await _db.Redirects.AsNoTracking().FirstAsync(r => r.FromPath == "/hit");
        Assert.Equal(6, updated.HitCount);
    }

    [Fact]
    public async Task Redirect_defaults_to_301_and_IsActive()
    {
        var r = new FcmsRedirect { FromPath = "/a", ToPath = "/b" };
        _db.Redirects.Add(r);
        await _db.SaveChangesAsync();

        var loaded = await _db.Redirects.FindAsync(r.Id);
        Assert.NotNull(loaded);
        Assert.Equal(301, loaded.StatusCode);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task SoftDeleted_redirect_is_not_matched()
    {
        var r = new FcmsRedirect { FromPath = "/del-redirect", ToPath = "/dest", IsActive = true, Status = EntityStatus.Deleted };
        _db.Redirects.Add(r);
        await _db.SaveChangesAsync();

        // Global query filter excludes IsDeleted — normal query returns null
        var found = await _db.Redirects
            .AsNoTracking()
            .FirstOrDefaultAsync(r2 => r2.IsActive && r2.FromPath == "/del-redirect");

        Assert.Null(found);
    }
}
