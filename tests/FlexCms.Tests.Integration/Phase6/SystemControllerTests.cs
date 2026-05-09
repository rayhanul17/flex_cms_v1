using FlexCms.Framework.Caching;
using FlexCms.Host.Controllers.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Integration.Phase6;

public class SystemControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class TestState
    {
        public IFcmsGroupCacheService Cache { get; } =
            new FcmsGroupCacheService(new MemoryCache(new MemoryCacheOptions()));
        public IHostApplicationLifetime Lifetime { get; } =
            Substitute.For<IHostApplicationLifetime>();

        public SystemController Build()
        {
            var ctrl = new SystemController(Cache, Lifetime);
            var httpContext = new DefaultHttpContext();
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
            ctrl.TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>());
            return ctrl;
        }
    }

    // ── Index ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Index_returns_view()
    {
        var result = new TestState().Build().Index();
        Assert.IsType<ViewResult>(result);
    }

    // ── ClearCache ────────────────────────────────────────────────────────────

    [Fact]
    public void ClearCache_empties_all_cache_groups()
    {
        var s = new TestState();
        s.Cache.Set("settings",    "site",  "v", TimeSpan.FromMinutes(5));
        s.Cache.Set("permissions", "perm1", "v", TimeSpan.FromMinutes(5));
        s.Cache.Set("menu",        "admin", "v", TimeSpan.FromMinutes(5));

        s.Build().ClearCache();

        Assert.Null(s.Cache.Get<string>("settings",    "site"));
        Assert.Null(s.Cache.Get<string>("permissions", "perm1"));
        Assert.Null(s.Cache.Get<string>("menu",        "admin"));
    }

    [Fact]
    public void ClearCache_redirects_to_Index()
    {
        var ctrl = new TestState().Build();
        var result = ctrl.ClearCache();
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SystemController.Index), redirect.ActionName);
    }

    [Fact]
    public void ClearCache_sets_success_toast_in_TempData()
    {
        var ctrl = new TestState().Build();
        ctrl.ClearCache();
        Assert.Equal("success", ctrl.TempData["Toast.Type"]);
        Assert.NotNull(ctrl.TempData["Toast.Message"]);
    }

    [Fact]
    public void ClearCache_on_empty_cache_does_not_throw()
    {
        var ex = Record.Exception(() => new TestState().Build().ClearCache());
        Assert.Null(ex);
    }

    [Fact]
    public void After_ClearCache_new_entries_can_be_stored_and_retrieved()
    {
        var s = new TestState();
        s.Cache.Set("grp", "k", "old", TimeSpan.FromMinutes(5));
        s.Build().ClearCache();

        s.Cache.Set("grp", "k2", "new", TimeSpan.FromMinutes(5));
        Assert.Equal("new", s.Cache.Get<string>("grp", "k2"));
    }

    // ── Restart ───────────────────────────────────────────────────────────────

    [Fact]
    public void Restart_redirects_to_Index()
    {
        var result = new TestState().Build().Restart();
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SystemController.Index), redirect.ActionName);
    }

    [Fact]
    public void Restart_sets_success_toast_in_TempData()
    {
        var ctrl = new TestState().Build();
        ctrl.Restart();
        Assert.Equal("success", ctrl.TempData["Toast.Type"]);
        Assert.NotNull(ctrl.TempData["Toast.Message"]);
    }

    [Fact]
    public void Restart_does_not_call_StopApplication_synchronously()
    {
        // StopApplication() must be deferred to Response.OnCompleted so the
        // redirect response flushes before the process stops.
        var s = new TestState();
        s.Build().Restart();
        s.Lifetime.DidNotReceive().StopApplication();
    }
}
