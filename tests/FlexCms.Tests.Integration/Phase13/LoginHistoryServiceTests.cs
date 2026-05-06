using FlexCms.Framework.Auth.History;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlexCms.Tests.Integration.Phase13;

public sealed class LoginHistoryServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly LoginHistoryService _svc;

    public LoginHistoryServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new LoginHistoryService(new EfRepository<FcmsLoginHistory>(_db), new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RecordAsync_persists_with_outcome_and_metadata()
    {
        await _svc.RecordAsync("alice", Guid.NewGuid(), LoginOutcome.Success, "1.2.3.4", "Mozilla/5.0");
        await _svc.RecordAsync("alice", null, LoginOutcome.InvalidCredentials, "1.2.3.4", "Mozilla/5.0", "wrong password");

        Assert.Equal(2, await _db.LoginHistory.CountAsync());
    }

    [Fact]
    public async Task GetRecentAsync_orders_newest_first_and_caps_at_max()
    {
        for (int i = 0; i < 30; i++)
        {
            await _svc.RecordAsync($"user{i}", null, LoginOutcome.Success, "ip", "ua");
            await Task.Delay(2);
        }

        var recent = await _svc.GetRecentAsync(max: 10);
        Assert.Equal(10, recent.Count);
        Assert.Equal("user29", recent[0].AttemptedUserName);
    }

    [Fact]
    public async Task GetFailedCountSinceAsync_filters_by_time_and_outcome()
    {
        // Insert via the service then back-date one row past the threshold —
        // FcmsDbContext.SaveChangesAsync overrides CreatedAt on Added entries
        // (auto-stamping is correct prod behaviour but means we have to rewrite
        // it explicitly with EntityState=Modified).
        await _svc.RecordAsync("old", null, LoginOutcome.InvalidCredentials, "ip", "ua");
        var oldRow = await _db.LoginHistory.FirstAsync();
        oldRow.CreatedAt = DateTime.UtcNow.AddHours(-2);
        _db.Entry(oldRow).State = EntityState.Modified;
        await _db.SaveChangesAsync();

        await _svc.RecordAsync("recent1", null, LoginOutcome.InvalidCredentials, "ip", "ua");
        await _svc.RecordAsync("recent2", null, LoginOutcome.LockedOut, "ip", "ua");
        await _svc.RecordAsync("recent3", null, LoginOutcome.Success, "ip", "ua");   // success excluded

        var sinceLastHour = DateTime.UtcNow.AddHours(-1);
        var failedRecent = await _svc.GetFailedCountSinceAsync(sinceLastHour);

        Assert.Equal(2, failedRecent);   // recent1 + recent2; old + success excluded
    }
}
