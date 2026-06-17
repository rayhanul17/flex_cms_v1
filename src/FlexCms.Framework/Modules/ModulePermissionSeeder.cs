using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Modules;

/// <summary>
/// Upserts the permissions declared by a module into <c>fcms_permissions</c>.
/// Called from <see cref="ModuleActivationService"/> on every restart so a
/// module that adds, renames, or relabels permissions in a new version sees
/// its changes reflected without manual SQL.
///
/// <para>
/// Keys are namespaced as <c>{moduleId}.{def.Key}</c> (lowercase) to keep
/// modules from colliding on a short key like <c>"create"</c>. The module's
/// <see cref="IFcmsModule.ModuleName"/> is used as the default group label
/// when <see cref="FcmsPermissionDef.Group"/> is empty.
/// </para>
/// </summary>
public sealed class ModulePermissionSeeder
{
    private readonly IRepository<FcmsPermission> _permissions;
    private readonly IFcmsUnitOfWork _uow;
    private readonly ILogger<ModulePermissionSeeder> _logger;

    public ModulePermissionSeeder(
        IRepository<FcmsPermission> permissions,
        IFcmsUnitOfWork uow,
        ILogger<ModulePermissionSeeder> logger)
    {
        _permissions = permissions;
        _uow = uow;
        _logger = logger;
    }

    public async Task SeedAsync(IFcmsModule module, CancellationToken ct = default)
    {
        var defs = module.GetPermissions();
        if (defs.Count == 0) return;

        var prefix = module.ModuleId.ToLowerInvariant() + ".";
        var defaultGroup = string.IsNullOrWhiteSpace(module.ModuleName)
            ? module.ModuleId
            : module.ModuleName;

        // Pull every permission once and match by Key — cheaper than per-row queries
        // and avoids the FindAsync-with-soft-deleted gotcha.
        var existing = (await _permissions.GetAllAsync(ct))
            .ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

        var inserted = 0;
        var updated = 0;
        var declaredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in defs)
        {
            if (string.IsNullOrWhiteSpace(def.Key)) continue;

            var fullKey = prefix + def.Key.Trim().ToLowerInvariant();
            declaredKeys.Add(fullKey);
            var group = string.IsNullOrWhiteSpace(def.Group) ? defaultGroup : def.Group;
            var display = string.IsNullOrWhiteSpace(def.DisplayName) ? fullKey : def.DisplayName;

            if (existing.TryGetValue(fullKey, out var perm))
            {
                if (perm.DisplayName != display || perm.Group != group)
                {
                    perm.DisplayName = display;
                    perm.Group = group;
                    await _permissions.UpdateAsync(perm, ct);
                    updated++;
                }
            }
            else
            {
                await _permissions.AddAsync(new FcmsPermission
                {
                    Key = fullKey,
                    Group = group,
                    DisplayName = display
                }, ct);
                inserted++;
            }
        }

        // Soft-delete keys this module previously declared but doesn't any more
        // (renames / removals). We use the prefix to scope so we don't touch
        // another module's rows or framework-core permissions.
        var stale = existing.Values
            .Where(p => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && !declaredKeys.Contains(p.Key)
                        && p.Status != EntityStatus.Deleted)
            .ToList();
        foreach (var p in stale)
            await _permissions.SoftDeleteAsync(p, ct);

        if (inserted > 0 || updated > 0 || stale.Count > 0)
        {
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Module {Id}: permissions seeded — {Inserted} added, {Updated} updated, {Pruned} pruned.",
                module.ModuleId, inserted, updated, stale.Count);
        }
    }
}
