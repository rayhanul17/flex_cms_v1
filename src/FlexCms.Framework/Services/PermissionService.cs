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

        // Resolve role ids first (cheap — cached or one query per first-seen
        // role name). Cache hits avoid the DB entirely for the typical
        // returning-user case.
        var roleIds = new List<Guid>(roleNames.Count);
        foreach (var rn in roleNames)
        {
            var rid = await ResolveRoleIdAsync(rn, ct);
            if (rid != Guid.Empty) roleIds.Add(rid);
        }
        if (roleIds.Count == 0) return false;

        // Split into "already cached" + "needs lookup". For the cold-cache
        // case, batch the misses into ONE query covering all uncached roles
        // — previous impl issued one query per role.
        var userPerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<Guid>();
        foreach (var rid in roleIds)
        {
            if (_cache.TryGetValue(CacheKey(rid), out HashSet<string>? cached) && cached is not null)
                userPerms.UnionWith(cached);
            else
                missing.Add(rid);
        }

        if (missing.Count > 0)
        {
            // One round-trip for every uncached role, then group by role id
            // and back-fill each role's cache so the next request is hot.
            var rows = await _rolePerms.FindAsync(rp => missing.Contains(rp.RoleId), ct);
            var byRole = rows
                .GroupBy(rp => rp.RoleId)
                .ToDictionary(g => g.Key,
                    g => g.Select(rp => rp.PermissionKey)
                          .ToHashSet(StringComparer.OrdinalIgnoreCase));
            foreach (var rid in missing)
            {
                var keys = byRole.TryGetValue(rid, out var k) ? k : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _cache.Set(CacheKey(rid), keys, CacheTtl);
                userPerms.UnionWith(keys);
            }
        }

        return PermissionExpression.Evaluate(permissionExpr, userPerms);
    }

    public async Task<IReadOnlySet<string>> GetRolePermissionKeysAsync(Guid roleId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(roleId), out HashSet<string>? cached) && cached is not null)
            return cached;

        var rows = await _rolePerms.FindAsync(rp => rp.RoleId == roleId, ct);
        var keys = rows
            .Select(rp => rp.PermissionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _cache.Set(CacheKey(roleId), keys, CacheTtl);
        return keys;
    }

    public async Task AssignAsync(Guid roleId, string permissionKey, CancellationToken ct = default)
    {
        var exists = await _rolePerms.ExistsAsync(
            rp => rp.RoleId == roleId &&
                  rp.PermissionKey == permissionKey, ct);

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
        var rp = await _rolePerms.FirstOrDefaultAsync(
            r => r.RoleId == roleId && r.PermissionKey == permissionKey, ct);

        if (rp is null) return;

        await _rolePerms.SoftDeleteAsync(rp, ct);
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
