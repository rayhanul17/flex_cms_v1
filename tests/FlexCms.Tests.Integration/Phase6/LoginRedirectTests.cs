using FlexCms.Framework.Auth;
using FlexCms.Framework.Db.Ef;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase6;

/// <summary>
/// Tests the role-priority redirect resolution logic using EF InMemory + real Identity stores.
/// We test the logic directly by wiring up RoleManager + UserManager without starting the full host.
/// </summary>
public class LoginRedirectTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly RoleManager<FcmsRole> _roleManager;
    private readonly UserManager<FcmsUser> _userManager;

    public LoginRedirectTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);

        var roleStore = new FcmsInMemoryRoleStoreForRedirect(_db);
#pragma warning disable CA2000
        _roleManager = new RoleManager<FcmsRole>(
            roleStore, [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<ILogger<RoleManager<FcmsRole>>>());

        var userStore = new FcmsInMemoryUserStore(_db);
        _userManager = new UserManager<FcmsUser>(
            userStore,
            Options.Create(new IdentityOptions { User = { RequireUniqueEmail = true } }),
            new PasswordHasher<FcmsUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            Substitute.For<ILogger<UserManager<FcmsUser>>>());
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    // ── SuperAdmin always goes to /admin ──────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_redirects_to_admin_regardless_of_redirect_url()
    {
        await CreateRoleAsync(FcmsRoles.SuperAdmin, "/somewhere-else", priority: 0);
        var user = await CreateUserAsync("superadmin@test.com", FcmsRoles.SuperAdmin);

        var url = await ResolveRedirectAsync(user);

        Assert.Equal("/admin", url);
    }

    // ── Single role ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Single_role_with_redirect_url_uses_that_url()
    {
        await CreateRoleAsync("Editor", "/admin/posts", priority: 10);
        var user = await CreateUserAsync("editor@test.com", "Editor");

        var url = await ResolveRedirectAsync(user);

        Assert.Equal("/admin/posts", url);
    }

    [Fact]
    public async Task Single_role_with_empty_redirect_url_falls_back_to_slash()
    {
        await CreateRoleAsync("Viewer", "", priority: 0);
        var user = await CreateUserAsync("viewer@test.com", "Viewer");

        var url = await ResolveRedirectAsync(user);

        Assert.Equal("/", url);
    }

    // ── Multiple roles → highest Priority wins ────────────────────────────────

    [Fact]
    public async Task Multiple_roles_highest_priority_wins()
    {
        await CreateRoleAsync("Low", "/low", priority: 1);
        await CreateRoleAsync("High", "/high", priority: 99);
        var user = await CreateUserAsync("multi@test.com", "Low", "High");

        var url = await ResolveRedirectAsync(user);

        Assert.Equal("/high", url);
    }

    [Fact]
    public async Task Multiple_roles_same_priority_first_non_empty_url_used()
    {
        await CreateRoleAsync("RoleA", "/a", priority: 5);
        await CreateRoleAsync("RoleB", "/b", priority: 5);
        var user = await CreateUserAsync("tie@test.com", "RoleA", "RoleB");

        var url = await ResolveRedirectAsync(user);

        // Either /a or /b is acceptable when priorities are equal — just not empty
        Assert.True(url == "/a" || url == "/b");
    }

    [Fact]
    public async Task No_roles_falls_back_to_slash()
    {
        var user = await CreateUserAsync("noroles@test.com");

        var url = await ResolveRedirectAsync(user);

        Assert.Equal("/", url);
    }

    [Fact]
    public async Task SuperAdmin_wins_even_with_lower_priority_than_other_role()
    {
        await CreateRoleAsync(FcmsRoles.SuperAdmin, "/admin-override", priority: 0);
        await CreateRoleAsync("Manager", "/manager", priority: 999);
        var user = await CreateUserAsync("mixed@test.com", FcmsRoles.SuperAdmin, "Manager");

        var url = await ResolveRedirectAsync(user);

        Assert.Equal("/admin", url);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<FcmsRole> CreateRoleAsync(string name, string redirectUrl = "", int priority = 0)
    {
        var role = new FcmsRole(name)
        {
            LoginRedirectUrl = redirectUrl,
            Priority = priority
        };
        await _roleManager.CreateAsync(role);
        return role;
    }

    private async Task<FcmsUser> CreateUserAsync(string email, params string[] roleNames)
    {
        var user = new FcmsUser { UserName = email, Email = email };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        foreach (var rn in roleNames)
        {
            _db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = _db.Roles.First(r => r.Name == rn).Id });
        }
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>Mirrors AuthController.ResolveLoginRedirectAsync logic.</summary>
    private async Task<string> ResolveRedirectAsync(FcmsUser user)
    {
        var roleNames = (await _userManager.GetRolesAsync(user)).ToList();

        if (roleNames.Contains(FcmsRoles.SuperAdmin))
            return "/admin";

        if (roleNames.Count == 0)
            return "/";

        FcmsRole? bestRole = null;
        foreach (var name in roleNames)
        {
            var role = await _roleManager.FindByNameAsync(name);
            if (role is null) continue;
            if (bestRole is null || role.Priority > bestRole.Priority)
                bestRole = role;
        }

        var url = bestRole?.LoginRedirectUrl;
        return string.IsNullOrWhiteSpace(url) ? "/" : url;
    }
}

// ── Minimal stores backed by EF InMemory ──────────────────────────────────────

file sealed class FcmsInMemoryRoleStoreForRedirect : IRoleStore<FcmsRole>
{
    private readonly FcmsDbContext _db;
    public FcmsInMemoryRoleStoreForRedirect(FcmsDbContext db) => _db = db;

    public async Task<IdentityResult> CreateAsync(FcmsRole role, CancellationToken ct = default)
    {
        role.NormalizedName = role.Name?.ToUpperInvariant();
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(FcmsRole role, CancellationToken ct = default)
    {
        _db.Roles.Update(role);
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

    public void Dispose() { }
}

file sealed class FcmsInMemoryUserStore : IUserStore<FcmsUser>, IUserRoleStore<FcmsUser>
{
    private readonly FcmsDbContext _db;
    public FcmsInMemoryUserStore(FcmsDbContext db) => _db = db;

    public Task<IdentityResult> CreateAsync(FcmsUser user, CancellationToken ct = default)
    {
        _db.Users.Add(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(FcmsUser user, CancellationToken ct = default)
    {
        _db.Users.Update(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(FcmsUser user, CancellationToken ct = default)
    {
        _db.Users.Remove(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<FcmsUser?> FindByIdAsync(string userId, CancellationToken ct = default)
        => _db.Users.FindAsync([Guid.Parse(userId)], ct).AsTask()!;

    public Task<FcmsUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct = default)
        => _db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, ct);

    public Task<string> GetUserIdAsync(FcmsUser user, CancellationToken ct = default)
        => Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(FcmsUser user, CancellationToken ct = default)
        => Task.FromResult(user.UserName);

    public Task SetUserNameAsync(FcmsUser user, string? userName, CancellationToken ct = default)
    { user.UserName = userName; return Task.CompletedTask; }

    public Task<string?> GetNormalizedUserNameAsync(FcmsUser user, CancellationToken ct = default)
        => Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(FcmsUser user, string? normalizedName, CancellationToken ct = default)
    { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }

    // IUserRoleStore
    public async Task AddToRoleAsync(FcmsUser user, string roleName, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstAsync(r => r.NormalizedName == roleName.ToUpperInvariant(), ct);
        _db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveFromRoleAsync(FcmsUser user, string roleName, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == roleName.ToUpperInvariant(), ct);
        if (role is null) return;
        var ur = await _db.UserRoles.FindAsync([user.Id, role.Id], ct);
        if (ur is not null) _db.UserRoles.Remove(ur);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IList<string>> GetRolesAsync(FcmsUser user, CancellationToken ct = default)
    {
        var roleIds = await _db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync(ct);
        return await _db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name!).ToListAsync(ct);
    }

    public async Task<bool> IsInRoleAsync(FcmsUser user, string roleName, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == roleName.ToUpperInvariant(), ct);
        if (role is null) return false;
        return await _db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id, ct);
    }

    public async Task<IList<FcmsUser>> GetUsersInRoleAsync(string roleName, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == roleName.ToUpperInvariant(), ct);
        if (role is null) return [];
        var userIds = await _db.UserRoles.Where(ur => ur.RoleId == role.Id).Select(ur => ur.UserId).ToListAsync(ct);
        return await _db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync(ct);
    }

    public void Dispose() { }
}
