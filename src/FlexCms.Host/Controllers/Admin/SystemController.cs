using FlexCms.Framework.Auth;
using FlexCms.Framework.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/system")]
public class SystemController : BaseAdminController
{
    private readonly IFcmsGroupCacheService _cache;
    private readonly IHostApplicationLifetime _lifetime;

    public SystemController(IFcmsGroupCacheService cache, IHostApplicationLifetime lifetime)
    {
        _cache = cache;
        _lifetime = lifetime;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.SystemManage)]
    public IActionResult Index() => View();

    [HttpPost("cache/clear")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SystemManage)]
    [FcmsLog("system.cache.clear", "System")]
    public IActionResult ClearCache()
    {
        _cache.InvalidateAll();
        ShowSuccess("All cache cleared. Next requests will reload from database.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("restart")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SystemManage)]
    [FcmsLog("system.restart", "System")]
    public IActionResult Restart()
    {
        ShowSuccess("Application restart initiated. The site will be back in a few seconds.");
        // Response is flushed before StopApplication() completes the shutdown.
        // Hosting environment (IIS / systemd / Docker restart policy) brings it back.
        Response.OnCompleted(() =>
        {
            _lifetime.StopApplication();
            return Task.CompletedTask;
        });
        return RedirectToAction(nameof(Index));
    }
}
