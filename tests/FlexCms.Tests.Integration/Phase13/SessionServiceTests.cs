using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Sessions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlexCms.Tests.Integration.Phase13;

public sealed class SessionServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly SessionService _svc;

    public SessionServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new SessionService(new EfRepository<FcmsUserSession>(_db), new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RecordLoginAsync_inserts_session_row()
    {
        var u = Guid.NewGuid();
        var s = await _svc.RecordLoginAsync(u, "sess1", "1.2.3.4", "Mozilla/5.0", "Chrome on Windows");

        Assert.False(s.IsRevoked);
        Assert.Equal(1, await _db.UserSessions.CountAsync());
    }

    [Fact]
    public async Task GetActiveAsync_returns_only_non_revoked_for_user()
    {
        var u = Guid.NewGuid();
        var other = Guid.NewGuid();
        await _svc.RecordLoginAsync(u, "s1", "ip", "ua", "");
        await _svc.RecordLoginAsync(u, "s2", "ip", "ua", "");
        await _svc.RecordLoginAsync(other, "s3", "ip", "ua", "");
        await _svc.RevokeAsync("s1", null, "manual");

        var active = await _svc.GetActiveAsync(u);

        Assert.Single(active);
        Assert.Equal("s2", active[0].SessionId);
    }

    [Fact]
    public async Task IsValidAsync_distinguishes_active_revoked_unknown()
    {
        var u = Guid.NewGuid();
        await _svc.RecordLoginAsync(u, "s1", "ip", "ua", "");
        Assert.True(await _svc.IsValidAsync("s1"));

        await _svc.RevokeAsync("s1", null, "x");
        Assert.False(await _svc.IsValidAsync("s1"));

        Assert.False(await _svc.IsValidAsync("never-existed"));
        Assert.False(await _svc.IsValidAsync(""));
    }

    [Fact]
    public async Task RevokeAllForUserAsync_flips_only_active_rows_for_user()
    {
        var u = Guid.NewGuid();
        var other = Guid.NewGuid();
        await _svc.RecordLoginAsync(u, "s1", "ip", "ua", "");
        await _svc.RecordLoginAsync(u, "s2", "ip", "ua", "");
        var preRevoked = await _svc.RecordLoginAsync(u, "s3", "ip", "ua", "");
        await _svc.RevokeAsync("s3", null, "x");
        await _svc.RecordLoginAsync(other, "s4", "ip", "ua", "");

        var n = await _svc.RevokeAllForUserAsync(u, null, "password change");

        Assert.Equal(2, n);   // pre-revoked s3 not re-flipped, other-user s4 untouched
        Assert.False(await _svc.IsValidAsync("s1"));
        Assert.False(await _svc.IsValidAsync("s2"));
        Assert.True(await _svc.IsValidAsync("s4"));
    }

    [Fact]
    public async Task TouchAsync_bumps_LastSeenAt_for_active_session()
    {
        var u = Guid.NewGuid();
        var s = await _svc.RecordLoginAsync(u, "s1", "ip", "ua", "");
        var firstSeen = s.LastSeenAt;
        await Task.Delay(10);

        await _svc.TouchAsync("s1");

        var reloaded = await _db.UserSessions.AsNoTracking().FirstAsync();
        Assert.True(reloaded.LastSeenAt > firstSeen);
    }
}
