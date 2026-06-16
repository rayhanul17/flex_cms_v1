using FlexCms.Framework.Auth;
using FlexCms.Framework.Caching;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Messaging;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin")]
[FcmsAuthorize(FcmsPermissions.SettingsView)]
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
    private readonly IFcmsCacheService _cache;

    private const string CacheKey = "fcms:dashboard:stats";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public DashboardController(
        IRepository<FcmsPage> pages,
        IRepository<FcmsPost> posts,
        IRepository<FcmsCategory> categories,
        IRepository<FcmsMedia> media,
        IRepository<FcmsLog> logs,
        IRepository<FcmsPendingMessage> msgs,
        UserManager<FcmsUser> users,
        RoleManager<FcmsRole> roles,
        IFcmsCacheService cache)
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
        // IFcmsCacheService.GetOrCreateAsync is stampede-protected: on cold
        // cache only ONE caller runs the COUNT(*) sweep — concurrent dashboard
        // refreshes wait on a per-key semaphore and read the populated value
        // when the first caller finishes. With raw IMemoryCache we hit the DB
        // N times per concurrent refresh.
        var vm = await _cache.GetOrCreateAsync(CacheKey, BuildAsync, CacheTtl, ct);
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
