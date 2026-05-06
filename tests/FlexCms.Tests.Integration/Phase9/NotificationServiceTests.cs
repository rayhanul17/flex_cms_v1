using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlexCms.Tests.Integration.Phase9;

/// <summary>
/// Phase 9 — notification flow against EF in-memory: per-user insert,
/// broadcast expansion, recent-list ordering, unread-count + mark-read
/// state transitions.
/// </summary>
public sealed class NotificationServiceTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly FcmsDbContext _db;
    private readonly UserManager<FcmsUser> _users;
    private readonly FcmsNotificationService _svc;

    public NotificationServiceTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddDbContext<FcmsDbContext>(o => o.UseInMemoryDatabase(dbName));
        sc.AddScoped<DbContext>(sp => sp.GetRequiredService<FcmsDbContext>());
        sc.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        sc.AddScoped<IFcmsUnitOfWork, EfUnitOfWork>();
        sc.AddIdentityCore<FcmsUser>()
            .AddRoles<FcmsRole>()
            .AddEntityFrameworkStores<FcmsDbContext>();

        _sp = sc.BuildServiceProvider();
        _db = _sp.GetRequiredService<FcmsDbContext>();
        _users = _sp.GetRequiredService<UserManager<FcmsUser>>();
#pragma warning disable CA2000
        _svc = new FcmsNotificationService(
            new EfRepository<FcmsNotification>(_db),
            new EfUnitOfWork(_db),
            _users);
#pragma warning restore CA2000
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp.Dispose();
    }

    private async Task<FcmsUser> SeedUserAsync(string name)
    {
        var u = new FcmsUser { UserName = name, Email = $"{name}@x.test" };
        var r = await _users.CreateAsync(u);
        Assert.True(r.Succeeded, string.Join(", ", r.Errors.Select(e => e.Description)));
        return u;
    }

    [Fact]
    public async Task NotifyUserAsync_inserts_one_row_with_correct_payload()
    {
        var u = await SeedUserAsync("alice");

        var n = await _svc.NotifyUserAsync(u.Id, "Hi", "Body", NotificationLevel.Success, url: "/admin", icon: "bi bi-check");

        Assert.NotEqual(Guid.Empty, n.Id);
        Assert.Equal("Hi", n.Title);
        Assert.Equal(NotificationLevel.Success, n.Level);
        Assert.False(n.IsRead);
        Assert.Equal(1, await _db.Notifications.CountAsync());
    }

    [Fact]
    public async Task NotifyAllAsync_inserts_one_row_per_user()
    {
        await SeedUserAsync("a");
        await SeedUserAsync("b");
        await SeedUserAsync("c");

        var n = await _svc.NotifyAllAsync("ping");

        Assert.Equal(3, n);
        Assert.Equal(3, await _db.Notifications.CountAsync());
    }

    [Fact]
    public async Task GetRecentAsync_orders_newest_first_and_caps_at_max()
    {
        var u = await SeedUserAsync("alice");
        for (int i = 0; i < 25; i++)
            await _svc.NotifyUserAsync(u.Id, $"n{i}");

        var recent = await _svc.GetRecentAsync(u.Id, max: 10);
        Assert.Equal(10, recent.Count);
        // newest first
        Assert.Equal("n24", recent.First().Title);
    }

    [Fact]
    public async Task GetUnreadCountAsync_only_counts_unread_for_user()
    {
        var alice = await SeedUserAsync("alice");
        var bob = await SeedUserAsync("bob");

        await _svc.NotifyUserAsync(alice.Id, "x");
        await _svc.NotifyUserAsync(alice.Id, "y");
        await _svc.NotifyUserAsync(bob.Id, "z");

        Assert.Equal(2, await _svc.GetUnreadCountAsync(alice.Id));
        Assert.Equal(1, await _svc.GetUnreadCountAsync(bob.Id));
    }

    [Fact]
    public async Task MarkReadAsync_only_affects_owner()
    {
        var alice = await SeedUserAsync("alice");
        var bob = await SeedUserAsync("bob");

        var n = await _svc.NotifyUserAsync(alice.Id, "x");

        // Bob trying to read Alice's notification → no-op
        await _svc.MarkReadAsync(bob.Id, n.Id);
        Assert.False((await _db.Notifications.AsNoTracking().FirstAsync(x => x.Id == n.Id)).IsRead);

        // Alice marking her own → flips to read
        await _svc.MarkReadAsync(alice.Id, n.Id);
        var updated = await _db.Notifications.AsNoTracking().FirstAsync(x => x.Id == n.Id);
        Assert.True(updated.IsRead);
        Assert.NotNull(updated.ReadAt);
    }

    [Fact]
    public async Task MarkAllReadAsync_flips_only_unread_rows_for_user()
    {
        var u = await SeedUserAsync("alice");
        await _svc.NotifyUserAsync(u.Id, "x");
        await _svc.NotifyUserAsync(u.Id, "y");
        var preRead = await _svc.NotifyUserAsync(u.Id, "z");
        await _svc.MarkReadAsync(u.Id, preRead.Id);

        var n = await _svc.MarkAllReadAsync(u.Id);

        Assert.Equal(2, n);   // pre-read row not re-flipped
        Assert.Equal(0, await _svc.GetUnreadCountAsync(u.Id));
    }
}
