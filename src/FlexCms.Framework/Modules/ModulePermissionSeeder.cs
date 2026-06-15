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

        foreach (var def in defs)
        {
            if (string.IsNullOrWhiteSpace(def.Key)) continue;

            var fullKey = prefix + def.Key.Trim().ToLowerInvariant();
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

        if (inserted > 0 || updated > 0)
        {
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Module {Id}: permissions seeded — {Inserted} added, {Updated} updated.",
                module.ModuleId, inserted, updated);
        }
    }
}
