using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Messaging;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin")]
public class DashboardController : BaseAdminController
{
    private readonly IRepository<FcmsPage> _pages;
    private readonly IRepository<FcmsPost> _posts;
    private readonly IRepository<FcmsCategory> _categories;
    private readonly IRepository<FcmsMedia> _media;
    private readonly IRepository<FcmsLog> _logs;
    private readonly IRepository<FcmsPendingMessage> _msgs;
    private readonly UserManager<FcmsUser> _users;
    private readonly RoleManager<FcmsRole> _roles;
    private readonly IMemoryCache _cache;

    private const string CacheKey = "fcms:dashboard:stats";

    public DashboardController(
        IRepository<FcmsPage> pages,
        IRepository<FcmsPost> posts,
        IRepository<FcmsCategory> categories,
        IRepository<FcmsMedia> media,
        IRepository<FcmsLog> logs,
        IRepository<FcmsPendingMessage> msgs,
        UserManager<FcmsUser> users,
        RoleManager<FcmsRole> roles,
        IMemoryCache cache)
    {
        _pages = pages;
        _posts = posts;
        _categories = categories;
        _media = media;
        _logs = logs;
        _msgs = msgs;
        _users = users;
        _roles = roles;
        _cache = cache;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Cache the heavy COUNT(*) sweep for 5 minutes — dashboard reload should
        // not hammer the DB on every refresh.
        var vm = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await BuildAsync(ct);
        }) ?? await BuildAsync(ct);

        return View(vm);
    }

    private async Task<DashboardViewModel> BuildAsync(CancellationToken ct)
    {
        var pages = (int)await _pages.CountAsync(p => true, ct);
        var posts = (int)await _posts.CountAsync(p => true, ct);
        var publishedPosts = (int)await _posts.CountAsync(p => p.IsPublished, ct);
        var categories = (int)await _categories.CountAsync(c => true, ct);
        var media = (int)await _media.CountAsync(m => true, ct);
        var pending = (int)await _msgs.CountAsync(m => m.DeliveryStatus == MessageDeliveryStatus.Pending, ct);
        var failed = (int)await _msgs.CountAsync(m => m.DeliveryStatus == MessageDeliveryStatus.Failed, ct);
        var recent = (await _logs.FindAsync(l => true, ct))
            .OrderByDescending(l => l.CreatedAt)
            .Take(10)
            .ToList();

        return new DashboardViewModel
        {
            Pages = pages,
            Posts = posts,
            PublishedPosts = publishedPosts,
            Categories = categories,
            MediaFiles = media,
            Users = _users.Users.Count(),
            Roles = _roles.Roles.Count(),
            PendingMessages = pending,
            FailedMessages = failed,
            RecentActivity = recent,
            AppVersion = typeof(DashboardController).Assembly.GetName().Version?.ToString(),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription
        };
    }
}
