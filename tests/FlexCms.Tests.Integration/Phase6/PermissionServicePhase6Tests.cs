using FlexCms.Framework.Db;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase6;

/// <summary>
/// Phase 6 permission tests: verifies post-fix behaviour of PermissionService.
/// Specifically: Assign uses ExistsAsync (no full table scan), Revoke uses SoftDeleteAsync
/// (soft-delete not manual IsDeleted=true), SeedPermissionsAsync idempotency.
/// </summary>
public class PermissionServicePhase6Tests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly RoleManager<FcmsRole> _roleManager;
    private readonly EfUnitOfWork _uow;
    private readonly PermissionService _svc;

    public PermissionServicePhase6Tests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
        _cache = new MemoryCache(new MemoryCacheOptions());

#pragma warning disable CA2000
        _roleManager = new RoleManager<FcmsRole>(
            new FcmsInMemoryRoleStore(_db),
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<ILogger<RoleManager<FcmsRole>>>());
#pragma warning restore CA2000

        _uow = new EfUnitOfWork(_db);
        _svc = new PermissionService(
            new EfRepository<FcmsPermission>(_db),
            new EfRepository<FcmsRolePermission>(_db),
            _roleManager, _cache, _uow);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    // ── Assign idempotency ────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_does_not_create_duplicate_row()
    {
        var role = await CreateRoleAsync("Editor");

        await _svc.AssignAsync(role.Id, "media.upload");
        await _svc.AssignAsync(role.Id, "media.upload");

        var count = _db.RolePermissions
            .IgnoreQueryFilters()
            .Count(rp => rp.RoleId == role.Id && rp.PermissionKey == "media.upload" && rp.Status != EntityStatus.Deleted);
        Assert.Equal(1, count);
    }

    // ── Revoke uses soft-delete ───────────────────────────────────────────────

    [Fact]
    public async Task Revoke_soft_deletes_row_not_hard_deletes()
    {
        var role = await CreateRoleAsync("Revoker");
        await _svc.AssignAsync(role.Id, "audit.view");

        await _svc.RevokeAsync(role.Id, "audit.view");

        // Row still physically exists (soft-deleted)
        var raw = _db.RolePermissions
            .IgnoreQueryFilters()
            .FirstOrDefault(rp => rp.RoleId == role.Id && rp.PermissionKey == "audit.view");
        Assert.NotNull(raw);
        Assert.Equal(EntityStatus.Deleted, raw.Status);
    }

    [Fact]
    public async Task Revoke_then_reassign_creates_new_active_row()
    {
        var role = await CreateRoleAsync("Reassigner");
        await _svc.AssignAsync(role.Id, "media.delete");
        await _svc.RevokeAsync(role.Id, "media.delete");
        await _svc.AssignAsync(role.Id, "media.delete");

        var keys = await _svc.GetRolePermissionKeysAsync(role.Id);
        Assert.Contains("media.delete", keys);
    }

    // ── GetRolePermissionKeys uses FindAsync (not GetAllAsync) ────────────────

    [Fact]
    public async Task GetRolePermissionKeys_returns_only_this_roles_permissions()
    {
        var role1 = await CreateRoleAsync("RoleA");
        var role2 = await CreateRoleAsync("RoleB");
        await _svc.AssignAsync(role1.Id, "pages.create");
        await _svc.AssignAsync(role2.Id, "posts.edit");

        var keys = await _svc.GetRolePermissionKeysAsync(role1.Id);

        Assert.Contains("pages.create", keys);
        Assert.DoesNotContain("posts.edit", keys);
    }

    // ── SeedPermissionsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SeedPermissions_media_and_audit_permissions_idempotent()
    {
        var perms = new[]
        {
            new FcmsPermission { Key = "media.view",   DisplayName = "Media: View",    Group = "Media" },
            new FcmsPermission { Key = "media.upload", DisplayName = "Media: Upload",  Group = "Media" },
            new FcmsPermission { Key = "audit.view",   DisplayName = "Audit Log: View", Group = "Admin" },
            new FcmsPermission { Key = "audit.manage", DisplayName = "Audit Log: Manage", Group = "Admin" },
        };

        await _svc.SeedPermissionsAsync(perms);
        await _svc.SeedPermissionsAsync(perms); // second call — should not duplicate

        var count = _db.Permissions.Count(p => p.Status != EntityStatus.Deleted);
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task SeedPermissions_adds_new_keys_on_second_call()
    {
        await _svc.SeedPermissionsAsync([
            new FcmsPermission { Key = "media.view", DisplayName = "Media: View", Group = "Media" }
        ]);
        await _svc.SeedPermissionsAsync([
            new FcmsPermission { Key = "media.view",   DisplayName = "Media: View",   Group = "Media" },
            new FcmsPermission { Key = "media.delete", DisplayName = "Media: Delete", Group = "Media" }
        ]);

        var count = _db.Permissions.Count(p => p.Status != EntityStatus.Deleted);
        Assert.Equal(2, count);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<FcmsRole> CreateRoleAsync(string name)
    {
        var role = new FcmsRole { Name = name };
        await _roleManager.CreateAsync(role);
        return role;
    }
}

// ── Minimal IRoleStore backed by EF InMemory (file-scoped to avoid collision with Phase3 tests) ──

file sealed class FcmsInMemoryRoleStore : IRoleStore<FcmsRole>
{
    private readonly FcmsDbContext _db;
    public FcmsInMemoryRoleStore(FcmsDbContext db) => _db = db;

    public async Task<IdentityResult> CreateAsync(FcmsRole role, CancellationToken ct = default)
    { role.NormalizedName = role.Name?.ToUpperInvariant(); _db.Roles.Add(role); await _db.SaveChangesAsync(ct); return IdentityResult.Success; }
    public async Task<IdentityResult> DeleteAsync(FcmsRole role, CancellationToken ct = default)
    { _db.Roles.Remove(role); await _db.SaveChangesAsync(ct); return IdentityResult.Success; }
    public async Task<FcmsRole?> FindByIdAsync(string roleId, CancellationToken ct = default)
        => await _db.Roles.FindAsync([Guid.Parse(roleId)], ct);
    public async Task<FcmsRole?> FindByNameAsync(string normalizedRoleName, CancellationToken ct = default)
        => await _db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalizedRoleName, ct);
    public Task<string?> GetNormalizedRoleNameAsync(FcmsRole role, CancellationToken ct = default) => Task.FromResult(role.NormalizedName);
    public Task<string> GetRoleIdAsync(FcmsRole role, CancellationToken ct = default) => Task.FromResult(role.Id.ToString());
    public Task<string?> GetRoleNameAsync(FcmsRole role, CancellationToken ct = default) => Task.FromResult(role.Name);
    public Task SetNormalizedRoleNameAsync(FcmsRole role, string? name, CancellationToken ct = default) { role.NormalizedName = name; return Task.CompletedTask; }
    public Task SetRoleNameAsync(FcmsRole role, string? name, CancellationToken ct = default) { role.Name = name; return Task.CompletedTask; }
    public async Task<IdentityResult> UpdateAsync(FcmsRole role, CancellationToken ct = default)
    { _db.Roles.Update(role); await _db.SaveChangesAsync(ct); return IdentityResult.Success; }
    public void Dispose() { }
}
