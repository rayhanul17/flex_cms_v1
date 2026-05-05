using System.Linq.Expressions;
using System.Security.Claims;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Models;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase6;

public class MenuServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class TestState
    {
        public List<FcmsMenuItem> Store { get; } = new();
        public IRepository<FcmsMenuItem> Repo { get; }
        public IFcmsUnitOfWork Uow { get; } = Substitute.For<IFcmsUnitOfWork>();
        public IPermissionService Perm { get; } = Substitute.For<IPermissionService>();
        public IHttpContextAccessor Http { get; } = Substitute.For<IHttpContextAccessor>();
        public IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());

        public TestState()
        {
            Repo = Substitute.For<IRepository<FcmsMenuItem>>();

            Repo.AddAsync(Arg.Any<FcmsMenuItem>(), Arg.Any<CancellationToken>())
                .Returns(call => { Store.Add(call.Arg<FcmsMenuItem>()); return Task.CompletedTask; });

            Repo.UpdateAsync(Arg.Any<FcmsMenuItem>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            Repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(Store.FirstOrDefault(m => m.Id == call.Arg<Guid>())));

            Repo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var ids = call.Arg<IEnumerable<Guid>>().ToHashSet();
                    return Task.FromResult(Store.Where(m => ids.Contains(m.Id)).ToList());
                });

            Repo.UpdateRangeAsync(Arg.Any<IEnumerable<FcmsMenuItem>>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            Repo.SoftDeleteRangeAsync(Arg.Any<IEnumerable<FcmsMenuItem>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    foreach (var item in call.Arg<IEnumerable<FcmsMenuItem>>())
                        item.Status = EntityStatus.Deleted;
                    return Task.CompletedTask;
                });

            Repo.FindAsync(Arg.Any<Expression<Func<FcmsMenuItem, bool>>>(),
                            Arg.Any<CancellationToken>(),
                            Arg.Any<bool>())
                .Returns(call =>
                {
                    var pred = call.Arg<Expression<Func<FcmsMenuItem, bool>>>().Compile();
                    var includeDeleted = call.ArgAt<bool>(2);
                    var result = Store.Where(pred);
                    if (!includeDeleted) result = result.Where(m => m.Status != EntityStatus.Deleted);
                    return Task.FromResult(result.ToList());
                });

            // Authenticated user by default
            var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) };
            Http.HttpContext.Returns(ctx);
        }

        public MenuService Build() => new(Repo, Uow, Perm, Http, Cache);
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_inserts_new_items()
    {
        var s = new TestState();
        var svc = s.Build();

        await svc.SeedAsync("blog", [
            new() { DefaultName = "Posts", Url = "/admin/blog/posts", Icon = "bi bi-newspaper" }
        ]);

        Assert.Single(s.Store);
        Assert.Equal("blog", s.Store[0].ModuleId);
        Assert.Equal("Posts", s.Store[0].DefaultName);
    }

    [Fact]
    public async Task SeedAsync_skips_duplicate_url_unchanged()
    {
        var s = new TestState();
        s.Store.Add(new FcmsMenuItem
        {
            ModuleId = "blog",
            Url = "/admin/blog/posts",
            DefaultName = "Posts",
            Icon = "bi bi-newspaper",
            Location = "AdminSidebar"
        });
        var svc = s.Build();

        await svc.SeedAsync("blog", [
            new() { DefaultName = "Posts", Url = "/admin/blog/posts", Icon = "bi bi-newspaper" }
        ]);

        Assert.Single(s.Store); // no duplicate
    }

    [Fact]
    public async Task SeedAsync_refreshes_DefaultName_and_Icon_on_upgrade()
    {
        var s = new TestState();
        s.Store.Add(new FcmsMenuItem
        {
            ModuleId = "blog",
            Url = "/admin/blog/posts",
            DefaultName = "Posts",
            Icon = "bi bi-newspaper",
            CustomName = "My Articles",
            Order = 99
        });
        var svc = s.Build();

        await svc.SeedAsync("blog", [
            new() { DefaultName = "Articles", Url = "/admin/blog/posts", Icon = "bi bi-pencil", Order = 10 }
        ]);

        var item = s.Store.Single();
        Assert.Equal("Articles", item.DefaultName);            // refreshed
        Assert.Equal("bi bi-pencil", item.Icon);                // refreshed
        Assert.Equal("My Articles", item.CustomName);           // preserved (admin's choice)
        Assert.Equal(99, item.Order);                           // preserved
    }

    [Fact]
    public async Task SeedAsync_restores_soft_deleted_item_on_reactivation()
    {
        var s = new TestState();
        s.Store.Add(new FcmsMenuItem
        {
            ModuleId = "blog",
            Url = "/admin/blog/posts",
            DefaultName = "Posts",
            Icon = "bi bi-newspaper",
            Status = EntityStatus.Deleted,
            DeletedAt = DateTime.UtcNow
        });
        var svc = s.Build();

        await svc.SeedAsync("blog", [
            new() { DefaultName = "Posts", Url = "/admin/blog/posts", Icon = "bi bi-newspaper" }
        ]);

        var item = s.Store.Single();
        Assert.NotEqual(EntityStatus.Deleted, item.Status);
        Assert.Null(item.DeletedAt);
    }

    // ── Removal ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveModuleItemsAsync_soft_deletes_only_module_items()
    {
        var s = new TestState();
        s.Store.Add(new FcmsMenuItem { ModuleId = "core", Url = "/admin/users", DefaultName = "Users" });
        s.Store.Add(new FcmsMenuItem { ModuleId = "blog", Url = "/admin/blog/posts", DefaultName = "Posts" });
        var svc = s.Build();

        await svc.RemoveModuleItemsAsync("blog");

        Assert.NotEqual(EntityStatus.Deleted, s.Store.Single(m => m.ModuleId == "core").Status);
        Assert.Equal(EntityStatus.Deleted, s.Store.Single(m => m.ModuleId == "blog").Status);
    }

    // ── Permission filtering ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMenuAsync_includes_items_with_no_required_permission()
    {
        var s = new TestState();
        s.Store.Add(new FcmsMenuItem
        {
            ModuleId = "core",
            Location = "AdminSidebar",
            Url = "/admin",
            DefaultName = "Dashboard",
            RequiredPermission = null
        });
        var svc = s.Build();

        var result = await svc.GetMenuAsync("AdminSidebar");

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMenuAsync_excludes_items_user_lacks_permission_for()
    {
        var s = new TestState();
        s.Store.Add(new FcmsMenuItem
        {
            ModuleId = "core",
            Location = "AdminSidebar",
            Url = "/admin/users",
            DefaultName = "Users",
            RequiredPermission = "users.manage"
        });
        s.Perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), "users.manage", Arg.Any<CancellationToken>())
              .Returns(false);
        var svc = s.Build();

        var result = await svc.GetMenuAsync("AdminSidebar");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMenuAsync_includes_items_user_has_permission_for()
    {
        var s = new TestState();
        s.Store.Add(new FcmsMenuItem
        {
            ModuleId = "core",
            Location = "AdminSidebar",
            Url = "/admin/users",
            DefaultName = "Users",
            RequiredPermission = "users.manage"
        });
        s.Perm.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), "users.manage", Arg.Any<CancellationToken>())
              .Returns(true);
        var svc = s.Build();

        var result = await svc.GetMenuAsync("AdminSidebar");

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMenuAsync_returns_empty_when_no_HttpContext_user()
    {
        var s = new TestState();
        s.Http.HttpContext.Returns((HttpContext?)null);
        s.Store.Add(new FcmsMenuItem { ModuleId = "core", Location = "AdminSidebar", Url = "/admin", DefaultName = "Dashboard" });
        var svc = s.Build();

        var result = await svc.GetMenuAsync("AdminSidebar");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMenuAsync_orders_by_Order_ascending()
    {
        var s = new TestState();
        s.Store.Add(new FcmsMenuItem { ModuleId = "core", Location = "AdminSidebar", Url = "/c", DefaultName = "C", Order = 30 });
        s.Store.Add(new FcmsMenuItem { ModuleId = "core", Location = "AdminSidebar", Url = "/a", DefaultName = "A", Order = 10 });
        s.Store.Add(new FcmsMenuItem { ModuleId = "core", Location = "AdminSidebar", Url = "/b", DefaultName = "B", Order = 20 });
        var svc = s.Build();

        var result = await svc.GetMenuAsync("AdminSidebar");

        Assert.Equal(["A", "B", "C"], result.Select(m => m.DefaultName));
    }

    // ── Rename / DisplayName ──────────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_sets_CustomName()
    {
        var s = new TestState();
        var item = new FcmsMenuItem { Id = Guid.NewGuid(), Location = "AdminSidebar", DefaultName = "Posts", Url = "/p" };
        s.Store.Add(item);
        var svc = s.Build();

        await svc.RenameAsync(item.Id, "Articles");

        Assert.Equal("Articles", item.CustomName);
    }

    [Fact]
    public async Task RenameAsync_with_blank_clears_CustomName()
    {
        var s = new TestState();
        var item = new FcmsMenuItem { Id = Guid.NewGuid(), Location = "AdminSidebar", DefaultName = "Posts", CustomName = "Articles", Url = "/p" };
        s.Store.Add(item);
        var svc = s.Build();

        await svc.RenameAsync(item.Id, "  ");

        Assert.Null(item.CustomName);
    }

    [Fact]
    public void DisplayName_falls_back_to_DefaultName_when_CustomName_null()
    {
        var item = new FcmsMenuItem { DefaultName = "Posts", CustomName = null };
        Assert.Equal("Posts", item.DisplayName);
    }

    [Fact]
    public void DisplayName_uses_CustomName_when_set()
    {
        var item = new FcmsMenuItem { DefaultName = "Posts", CustomName = "Articles" };
        Assert.Equal("Articles", item.DisplayName);
    }

    // ── Reorder ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReorderAsync_updates_Order_for_specified_ids()
    {
        var s = new TestState();
        var a = new FcmsMenuItem { Id = Guid.NewGuid(), Url = "/a", DefaultName = "A", Order = 10 };
        var b = new FcmsMenuItem { Id = Guid.NewGuid(), Url = "/b", DefaultName = "B", Order = 20 };
        s.Store.Add(a);
        s.Store.Add(b);
        var svc = s.Build();

        await svc.ReorderAsync(new Dictionary<Guid, int> { [b.Id] = 5, [a.Id] = 15 });

        Assert.Equal(15, a.Order);
        Assert.Equal(5, b.Order);
    }
}
