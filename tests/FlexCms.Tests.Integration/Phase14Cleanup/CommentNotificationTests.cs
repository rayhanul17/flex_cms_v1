using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms.Comments;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlexCms.Tests.Integration.Phase14Cleanup;

/// <summary>
/// Phase 14 cleanup — verifies the comment-submission flow now fires
/// per-admin notifications when a Pending comment lands. Spam-flagged
/// comments deliberately do NOT notify (avoids drowning admins in alerts).
/// </summary>
public sealed class CommentNotificationTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly FcmsDbContext _db;
    private readonly UserManager<FcmsUser> _users;
    private readonly RoleManager<FcmsRole> _roles;
    private readonly FcmsNotificationService _notifications;
    private readonly CommentService _svc;

    public CommentNotificationTests()
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
        _roles = _sp.GetRequiredService<RoleManager<FcmsRole>>();
#pragma warning disable CA2000
        _notifications = new FcmsNotificationService(
            new EfRepository<FcmsNotification>(_db),
            new EfUnitOfWork(_db),
            _users);
        _svc = new CommentService(
            new EfRepository<FcmsComment>(_db),
            new EfUnitOfWork(_db),
            _notifications,
            _roles,
            _users);
#pragma warning restore CA2000
    }

    public void Dispose() { _db.Dispose(); _sp.Dispose(); }

    private async Task<FcmsUser> SeedAdminAsync(string name)
    {
        if (await _roles.FindByNameAsync(FcmsRoles.SuperAdmin) is null)
            await _roles.CreateAsync(new FcmsRole { Name = FcmsRoles.SuperAdmin });
        var u = new FcmsUser { UserName = name, Email = $"{name}@x.test" };
        await _users.CreateAsync(u);
        await _users.AddToRoleAsync(u, FcmsRoles.SuperAdmin);
        return u;
    }

    [Fact]
    public async Task Pending_comment_sends_one_notification_per_admin()
    {
        var alice = await SeedAdminAsync("alice");
        var bob = await SeedAdminAsync("bob");

        await _svc.SubmitAsync(new FcmsComment
        {
            EntityType = "FcmsPost",
            EntityId = Guid.NewGuid(),
            AuthorName = "Visitor",
            Body = "Nice article!"
        });

        Assert.Equal(2, await _db.Notifications.CountAsync());
        Assert.Equal(1, await _svc_unread(_notifications, alice.Id));
        Assert.Equal(1, await _svc_unread(_notifications, bob.Id));
    }

    [Fact]
    public async Task Spam_comment_does_NOT_notify_admins()
    {
        await SeedAdminAsync("alice");

        var spammy = string.Join(" ", Enumerable.Range(0, 8).Select(i => $"https://x.com/{i}"));
        await _svc.SubmitAsync(new FcmsComment
        {
            EntityType = "FcmsPost",
            EntityId = Guid.NewGuid(),
            AuthorName = "Bot",
            Body = spammy
        });

        Assert.Equal(0, await _db.Notifications.CountAsync());
    }

    [Fact]
    public async Task Notification_body_includes_truncated_comment_preview()
    {
        await SeedAdminAsync("alice");
        var longBody = new string('x', 200);

        await _svc.SubmitAsync(new FcmsComment
        {
            EntityType = "FcmsPost",
            EntityId = Guid.NewGuid(),
            AuthorName = "Visitor",
            Body = longBody
        });

        var notif = await _db.Notifications.AsNoTracking().FirstAsync();
        Assert.Contains("Visitor:", notif.Body!, StringComparison.Ordinal);
        Assert.True(notif.Body!.Length < 200, "Long body must be truncated for the notification preview.");
    }

    private async Task<int> _svc_unread(FcmsNotificationService svc, Guid userId)
        => await svc.GetUnreadCountAsync(userId);
}
