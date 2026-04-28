using FlexCms.Framework.Auth;
using System.Security.Claims;

namespace FlexCms.Framework.Services;

public interface IPermissionService
{
    /// <summary>
    /// Returns true if the user is SuperAdmin (bypasses all checks) OR holds all/any of the
    /// permissions expressed in <paramref name="permissionExpr"/>.
    /// Syntax: single key, "a&amp;b" (AND — must have both), "a|b" (OR — must have one).
    /// </summary>
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionExpr, CancellationToken ct = default);

    /// <summary>Returns all permission keys assigned to a role.</summary>
    Task<IReadOnlySet<string>> GetRolePermissionKeysAsync(Guid roleId, CancellationToken ct = default);

    Task AssignAsync(Guid roleId, string permissionKey, CancellationToken ct = default);
    Task RevokeAsync(Guid roleId, string permissionKey, CancellationToken ct = default);

    /// <summary>Seeds permissions discovered from [FcmsAuthorize] attributes. Idempotent.</summary>
    Task SeedPermissionsAsync(IEnumerable<FcmsPermission> permissions, CancellationToken ct = default);

    void InvalidateRoleCache(Guid roleId);
}
