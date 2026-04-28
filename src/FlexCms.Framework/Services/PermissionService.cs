using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace FlexCms.Framework.Services;

public class PermissionService : IPermissionService
{
    private readonly IRepository<FcmsPermission> _permissions;
    private readonly IRepository<FcmsRolePermission> _rolePerms;
    private readonly RoleManager<FcmsRole> _roleManager;
    private readonly IMemoryCache _cache;
    private readonly IFcmsUnitOfWork _uow;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static string CacheKey(Guid roleId) => $"perm_{roleId}";
    private static string RoleIdKey(string name) => $"roleid_{name.ToLowerInvariant()}";

    public PermissionService(
        IRepository<FcmsPermission> permissions,
        IRepository<FcmsRolePermission> rolePerms,
        RoleManager<FcmsRole> roleManager,
        IMemoryCache cache,
        IFcmsUnitOfWork uow)
    {
        _permissions = permissions;
        _rolePerms = rolePerms;
        _roleManager = roleManager;
        _cache = cache;
        _uow = uow;
    }

    public async Task<bool> HasPermissionAsync(
        ClaimsPrincipal user,
        string permissionExpr,
        CancellationToken ct = default)
    {
        if (user.IsInRole(FcmsRoles.SuperAdmin)) return true;

        var roleNames = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (roleNames.Count == 0) return false;

        var userPerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roleNames)
        {
            var roleId = await ResolveRoleIdAsync(roleName, ct);
            if (roleId == Guid.Empty) continue;
            var keys = await GetRolePermissionKeysAsync(roleId, ct);
            userPerms.UnionWith(keys);
        }

        return PermissionExpression.Evaluate(permissionExpr, userPerms);
    }

    public async Task<IReadOnlySet<string>> GetRolePermissionKeysAsync(Guid roleId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(roleId), out HashSet<string>? cached) && cached is not null)
            return cached;

        var all = await _rolePerms.GetAllAsync(ct);
        var keys = all
            .Where(rp => rp.RoleId == roleId && !rp.IsDeleted)
            .Select(rp => rp.PermissionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _cache.Set(CacheKey(roleId), keys, CacheTtl);
        return keys;
    }

    public async Task AssignAsync(Guid roleId, string permissionKey, CancellationToken ct = default)
    {
        var all = await _rolePerms.GetAllAsync(ct);
        var exists = all.Any(rp => rp.RoleId == roleId
            && string.Equals(rp.PermissionKey, permissionKey, StringComparison.OrdinalIgnoreCase)
            && !rp.IsDeleted);

        if (exists) return;

        await _rolePerms.AddAsync(new FcmsRolePermission
        {
            RoleId = roleId,
            PermissionKey = permissionKey
        }, ct);

        await _uow.SaveChangesAsync(ct);
        InvalidateRoleCache(roleId);
    }

    public async Task RevokeAsync(Guid roleId, string permissionKey, CancellationToken ct = default)
    {
        var all = await _rolePerms.GetAllAsync(ct);
        var rp = all.FirstOrDefault(r => r.RoleId == roleId
            && string.Equals(r.PermissionKey, permissionKey, StringComparison.OrdinalIgnoreCase)
            && !r.IsDeleted);

        if (rp is null) return;

        rp.IsDeleted = true;
        await _rolePerms.UpdateAsync(rp, ct);
        await _uow.SaveChangesAsync(ct);
        InvalidateRoleCache(roleId);
    }

    public async Task SeedPermissionsAsync(IEnumerable<FcmsPermission> permissions, CancellationToken ct = default)
    {
        var existing = (await _permissions.GetAllAsync(ct))
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool any = false;
        foreach (var perm in permissions)
        {
            if (existing.Contains(perm.Key)) continue;
            await _permissions.AddAsync(perm, ct);
            any = true;
        }

        if (any) await _uow.SaveChangesAsync(ct);
    }

    public void InvalidateRoleCache(Guid roleId)
        => _cache.Remove(CacheKey(roleId));

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<Guid> ResolveRoleIdAsync(string roleName, CancellationToken ct)
    {
        if (_cache.TryGetValue(RoleIdKey(roleName), out Guid cached))
            return cached;

        var role = await _roleManager.FindByNameAsync(roleName);
        var id = role?.Id ?? Guid.Empty;
        if (id != Guid.Empty)
            _cache.Set(RoleIdKey(roleName), id, TimeSpan.FromHours(1));

        return id;
    }
}
