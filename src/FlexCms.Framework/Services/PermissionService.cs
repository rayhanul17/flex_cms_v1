using FlexCms.Framework.Auth;
using FlexCms.Framework.Caching;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace FlexCms.Framework.Services;

public class PermissionService : IPermissionService
{
    private readonly IRepository<FcmsPermission> _permissions;
    private readonly IRepository<FcmsRolePermission> _rolePerms;
    private readonly RoleManager<FcmsRole> _roleManager;
    private readonly IFcmsGroupCacheService _cache;
    private readonly IFcmsUnitOfWork _uow;
    // Optional — when present, AssignAsync/RevokeAsync emit a dedicated audit
    // entry ("Permission.Assigned" / "Permission.Revoked") in addition to the
    // generic FcmsRolePermission write captured by the EF interceptor /
    // Mongo AuditingRepository decorator. Without this you only see
    // "RolePermission.Created" rows in the audit log, which makes it hard
    // to track WHICH permission was granted to WHICH role.
    private readonly IFcmsLogService? _audit;

    private const string Group = "permissions";
    private static readonly TimeSpan PermTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RoleTtl = TimeSpan.FromHours(1);

    private static string PermKey(Guid roleId) => $"perm_{roleId}";
    private static string RoleIdKey(string name) => $"roleid_{name.ToLowerInvariant()}";

    public PermissionService(
        IRepository<FcmsPermission> permissions,
        IRepository<FcmsRolePermission> rolePerms,
        RoleManager<FcmsRole> roleManager,
        IFcmsGroupCacheService cache,
        IFcmsUnitOfWork uow,
        IFcmsLogService? audit = null)
    {
        _permissions = permissions;
        _rolePerms = rolePerms;
        _roleManager = roleManager;
        _cache = cache;
        _uow = uow;
        _audit = audit;
    }

    public async Task<bool> HasPermissionAsync(
        ClaimsPrincipal user,
        string permissionExpr,
        CancellationToken ct = default)
    {
        if (user.IsInRole(FcmsRoles.SuperAdmin)) return true;

        var roleNames = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (roleNames.Count == 0) return false;

        var roleIds = new List<Guid>(roleNames.Count);
        foreach (var rn in roleNames)
        {
            var rid = await ResolveRoleIdAsync(rn, ct);
            if (rid != Guid.Empty) roleIds.Add(rid);
        }
        if (roleIds.Count == 0) return false;

        var userPerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<Guid>();
        foreach (var rid in roleIds)
        {
            var cached = _cache.Get<HashSet<string>>(Group, PermKey(rid));
            if (cached is not null)
                userPerms.UnionWith(cached);
            else
                missing.Add(rid);
        }

        if (missing.Count > 0)
        {
            var rows = await _rolePerms.FindAsync(rp => missing.Contains(rp.RoleId), ct);
            var byRole = rows
                .GroupBy(rp => rp.RoleId)
                .ToDictionary(g => g.Key,
                    g => g.Select(rp => rp.PermissionKey)
                          .ToHashSet(StringComparer.OrdinalIgnoreCase));
            foreach (var rid in missing)
            {
                var keys = byRole.TryGetValue(rid, out var k) ? k : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _cache.Set(Group, PermKey(rid), keys, PermTtl);
                userPerms.UnionWith(keys);
            }
        }

        return PermissionExpression.Evaluate(permissionExpr, userPerms);
    }

    public async Task<IReadOnlySet<string>> GetRolePermissionKeysAsync(Guid roleId, CancellationToken ct = default)
    {
        var cached = _cache.Get<HashSet<string>>(Group, PermKey(roleId));
        if (cached is not null) return cached;

        var rows = await _rolePerms.FindAsync(rp => rp.RoleId == roleId, ct);
        var keys = rows
            .Select(rp => rp.PermissionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _cache.Set(Group, PermKey(roleId), keys, PermTtl);
        return keys;
    }

    public async Task AssignAsync(Guid roleId, string permissionKey, CancellationToken ct = default)
    {
        var exists = await _rolePerms.ExistsAsync(
            rp => rp.RoleId == roleId && rp.PermissionKey == permissionKey, ct);

        if (exists) return;

        await _rolePerms.AddAsync(new FcmsRolePermission
        {
            RoleId = roleId,
            PermissionKey = permissionKey
        }, ct);

        await _uow.SaveChangesAsync(ct);
        InvalidateRoleCache(roleId);

        if (_audit is not null)
            await _audit.LogAsync(
                action: "Permission.Assigned",
                entityType: nameof(FcmsRolePermission),
                entityId: roleId.ToString(),
                value: new { roleId, permissionKey },
                module: "auth",
                severity: FcmsLogSeverity.Info,
                ct: ct);
    }

    public async Task RevokeAsync(Guid roleId, string permissionKey, CancellationToken ct = default)
    {
        var rp = await _rolePerms.FirstOrDefaultAsync(
            r => r.RoleId == roleId && r.PermissionKey == permissionKey, ct);

        if (rp is null) return;

        await _rolePerms.SoftDeleteAsync(rp, ct);
        await _uow.SaveChangesAsync(ct);
        InvalidateRoleCache(roleId);

        if (_audit is not null)
            await _audit.LogAsync(
                action: "Permission.Revoked",
                entityType: nameof(FcmsRolePermission),
                entityId: roleId.ToString(),
                value: new { roleId, permissionKey },
                module: "auth",
                severity: FcmsLogSeverity.Warning,
                ct: ct);
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
        => _cache.Invalidate(Group, PermKey(roleId));

    private async Task<Guid> ResolveRoleIdAsync(string roleName, CancellationToken ct)
    {
        var cached = _cache.Get<Guid?>(Group, RoleIdKey(roleName));
        if (cached.HasValue) return cached.Value;

        var role = await _roleManager.FindByNameAsync(roleName);
        var id = role?.Id ?? Guid.Empty;
        if (id != Guid.Empty)
            _cache.Set(Group, RoleIdKey(roleName), (Guid?)id, RoleTtl);

        return id;
    }
}
