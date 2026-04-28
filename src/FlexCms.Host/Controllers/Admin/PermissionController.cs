using FlexCms.Framework.Auth;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/permissions")]
public class PermissionController : BaseAdminController
{
    private readonly IPermissionService _permService;

    public PermissionController(IPermissionService permService)
    {
        _permService = permService;
    }

    // ── AJAX: assign permission to role ───────────────────────────────────────

    [HttpPost("assign")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("roles.permissions")]
    public async Task<IActionResult> Assign([FromBody] PermissionAssignRequest req, CancellationToken ct)
    {
        if (req.RoleId == Guid.Empty || string.IsNullOrWhiteSpace(req.PermissionKey))
            return FcmsFail("Invalid request.");

        await _permService.AssignAsync(req.RoleId, req.PermissionKey, ct);
        return FcmsOk();
    }

    // ── AJAX: revoke permission from role ─────────────────────────────────────

    [HttpPost("revoke")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("roles.permissions")]
    public async Task<IActionResult> Revoke([FromBody] PermissionAssignRequest req, CancellationToken ct)
    {
        if (req.RoleId == Guid.Empty || string.IsNullOrWhiteSpace(req.PermissionKey))
            return FcmsFail("Invalid request.");

        await _permService.RevokeAsync(req.RoleId, req.PermissionKey, ct);
        return FcmsOk();
    }
}

public sealed class PermissionAssignRequest
{
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = "";
}
