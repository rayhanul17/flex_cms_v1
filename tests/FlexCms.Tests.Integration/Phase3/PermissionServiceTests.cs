using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Security.Claims;

namespace FlexCms.Tests.Integration.Phase3;

/// <summary>
/// Tests PermissionService using EF InMemory — no Docker required.
/// Covers: Assign, Revoke, HasPermissionAsync (SuperAdmin bypass, role-based, AND/OR), cache invalidation.
/// </summary>
public class PermissionServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly RoleManager<FcmsRole> _roleManager;
    private readonly EfUnitOfWork _uow;
    private readonly PermissionService _svc;

    public PermissionServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // fresh DB per test class
            .Options;
        _db = new FcmsDbContext(opts);

        _cache = new MemoryCache(new MemoryCacheOptions());

#pragma warning disable CA2000 // RoleManager takes ownership and disposes the store
        _roleManager = new RoleManager<FcmsRole>(
            new FcmsInMemoryRoleStore(_db),
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<ILogger<RoleManager<FcmsRole>>>());
#pragma warning restore CA2000

        var permRepo = new EfRepository<FcmsPermission>(_db);
        var rpRepo = new EfRepository<FcmsRolePermission>(_db);
        _uow = new EfUnitOfWork(_db);

        _svc = new PermissionService(permRepo, rpRepo, _roleManager, _cache, _uow);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    // ── Assign / Revoke ───────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_stores_role_permission()
    {
        var role = await CreateRoleAsync("Editor");

        await _svc.AssignAsync(role.Id, "posts.edit");

        var keys = await _svc.GetRolePermissionKeysAsync(role.Id);
        Assert.Contains("posts.edit", keys);
    }

    [Fact]
    public async Task Assign_is_idempotent()
    {
        var role = await CreateRoleAsync("Editor2");

        await _svc.AssignAsync(role.Id, "posts.edit");
        await _svc.AssignAsync(role.Id, "posts.edit"); // second call should not throw or duplicate

        var keys = await _svc.GetRolePermissionKeysAsync(role.Id);
        Assert.Single(keys, k => k == "posts.edit");
    }

    [Fact]
    public async Task Revoke_removes_role_permission()
    {
        var role = await CreateRoleAsync("Editor3");
        await _svc.AssignAsync(role.Id, "posts.delete");

        await _svc.RevokeAsync(role.Id, "posts.delete");

        var keys = await _svc.GetRolePermissionKeysAsync(role.Id);
        Assert.DoesNotContain("posts.delete", keys);
    }

    [Fact]
    public async Task Revoke_nonexistent_does_not_throw()
    {
        var role = await CreateRoleAsync("Editor4");
        await _svc.RevokeAsync(role.Id, "nonexistent.key"); // should not throw
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRolePermissionKeys_caches_result()
    {
        var role = await CreateRoleAsync("CacheRole");
        await _svc.AssignAsync(role.Id, "x.read");

        var first = await _svc.GetRolePermissionKeysAsync(role.Id);
        // Assign directly to DB without going through service (bypass cache)
        _db.RolePermissions.Add(new FcmsRolePermission { RoleId = role.Id, PermissionKey = "x.write" });
        await _db.SaveChangesAsync();

        // Should return cached result (not see new row)
        var second = await _svc.GetRolePermissionKeysAsync(role.Id);
        Assert.DoesNotContain("x.write", second);
    }

    [Fact]
    public async Task InvalidateRoleCache_forces_fresh_db_read()
    {
        var role = await CreateRoleAsync("CacheRole2");
        await _svc.AssignAsync(role.Id, "y.read");
        _ = await _svc.GetRolePermissionKeysAsync(role.Id); // populate cache

        // Add directly to DB
        _db.RolePermissions.Add(new FcmsRolePermission { RoleId = role.Id, PermissionKey = "y.write" });
        await _db.SaveChangesAsync();

        _svc.InvalidateRoleCache(role.Id);
        var fresh = await _svc.GetRolePermissionKeysAsync(role.Id);
        Assert.Contains("y.write", fresh);
    }

    // ── HasPermissionAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task HasPermission_SuperAdmin_always_true_regardless_of_perms()
    {
        var user = SuperAdminUser();
        // No permissions assigned anywhere
        Assert.True(await _svc.HasPermissionAsync(user, "users.delete"));
        Assert.True(await _svc.HasPermissionAsync(user, "totally.nonexistent"));
    }

    [Fact]
    public async Task HasPermission_user_with_role_that_has_perm_returns_true()
    {
        var role = await CreateRoleAsync("Writer");
        await _svc.AssignAsync(role.Id, "posts.create");

        var user = UserWithRole("Writer", role.Id);
        Assert.True(await _svc.HasPermissionAsync(user, "posts.create"));
    }

    [Fact]
    public async Task HasPermission_user_with_role_that_lacks_perm_returns_false()
    {
        var role = await CreateRoleAsync("Viewer");
        // No permissions assigned

        var user = UserWithRole("Viewer", role.Id);
        Assert.False(await _svc.HasPermissionAsync(user, "posts.create"));
    }

    [Fact]
    public async Task HasPermission_AND_both_in_role_returns_true()
    {
        var role = await CreateRoleAsync("Manager");
        await _svc.AssignAsync(role.Id, "users.create");
        await _svc.AssignAsync(role.Id, "users.edit");

        var user = UserWithRole("Manager", role.Id);
        Assert.True(await _svc.HasPermissionAsync(user, "users.create&users.edit"));
    }

    [Fact]
    public async Task HasPermission_AND_one_missing_returns_false()
    {
        var role = await CreateRoleAsync("Manager2");
        await _svc.AssignAsync(role.Id, "users.create");
        // users.edit NOT assigned

        var user = UserWithRole("Manager2", role.Id);
        Assert.False(await _svc.HasPermissionAsync(user, "users.create&users.edit"));
    }

    [Fact]
    public async Task HasPermission_OR_one_present_returns_true()
    {
        var role = await CreateRoleAsync("Contributor");
        await _svc.AssignAsync(role.Id, "posts.edit");
        // posts.create NOT assigned

        var user = UserWithRole("Contributor", role.Id);
        Assert.True(await _svc.HasPermissionAsync(user, "posts.create|posts.edit"));
    }

    [Fact]
    public async Task HasPermission_no_roles_returns_false()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));

        Assert.False(await _svc.HasPermissionAsync(user, "anything.read"));
    }

    // ── SeedPermissions ───────────────────────────────────────────────────────

    [Fact]
    public async Task SeedPermissions_inserts_new_and_skips_existing()
    {
        var initial = new FcmsPermission { Key = "posts.view", Group = "Posts", DisplayName = "View Posts" };
        await _svc.SeedPermissionsAsync([initial]);
        await _svc.SeedPermissionsAsync([
            new FcmsPermission { Key = "posts.view",   Group = "Posts", DisplayName = "View Posts" },
            new FcmsPermission { Key = "posts.create", Group = "Posts", DisplayName = "Create Posts" }
        ]);

        var all = _db.Permissions.Where(p => !p.IsDeleted).ToList();
        Assert.Equal(2, all.Count);
        Assert.Single(all, p => p.Key == "posts.view");
        Assert.Single(all, p => p.Key == "posts.create");  // xUnit2031 suppressed: filter is on a pre-materialized list
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<FcmsRole> CreateRoleAsync(string name)
    {
        var role = new FcmsRole { Name = name };
        await _roleManager.CreateAsync(role);
        return role;
    }

    private static ClaimsPrincipal SuperAdminUser()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, FcmsRoles.SuperAdmin)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal UserWithRole(string roleName, Guid roleId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, roleName)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}

// ── Minimal IRoleStore backed by EF InMemory ──────────────────────────────────

file sealed class FcmsInMemoryRoleStore : IRoleStore<FcmsRole>
{
    private readonly FcmsDbContext _db;
    public FcmsInMemoryRoleStore(FcmsDbContext db) => _db = db;

    public async Task<IdentityResult> CreateAsync(FcmsRole role, CancellationToken ct = default)
    {
        role.NormalizedName = role.Name?.ToUpperInvariant();
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(FcmsRole role, CancellationToken ct = default)
    {
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
        return IdentityResult.Success;
    }

    public async Task<FcmsRole?> FindByIdAsync(string roleId, CancellationToken ct = default)
        => await _db.Roles.FindAsync([Guid.Parse(roleId)], ct);

    public async Task<FcmsRole?> FindByNameAsync(string normalizedRoleName, CancellationToken ct = default)
        => await _db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalizedRoleName, ct);

    public Task<string?> GetNormalizedRoleNameAsync(FcmsRole role, CancellationToken ct = default)
        => Task.FromResult(role.NormalizedName);

    public Task<string> GetRoleIdAsync(FcmsRole role, CancellationToken ct = default)
        => Task.FromResult(role.Id.ToString());

    public Task<string?> GetRoleNameAsync(FcmsRole role, CancellationToken ct = default)
        => Task.FromResult(role.Name);

    public Task SetNormalizedRoleNameAsync(FcmsRole role, string? normalizedName, CancellationToken ct = default)
    { role.NormalizedName = normalizedName; return Task.CompletedTask; }

    public Task SetRoleNameAsync(FcmsRole role, string? roleName, CancellationToken ct = default)
    { role.Name = roleName; return Task.CompletedTask; }

    public async Task<IdentityResult> UpdateAsync(FcmsRole role, CancellationToken ct = default)
    {
        _db.Roles.Update(role);
        await _db.SaveChangesAsync(ct);
        return IdentityResult.Success;
    }

    public void Dispose() { }
}
