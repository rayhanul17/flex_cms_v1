using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/permissions")]
public class PermissionController : BaseAdminController
{
    private readonly IPermissionService _permService;
    private readonly IRepository<FcmsPermission> _permissions;

    public PermissionController(IPermissionService permService, IRepository<FcmsPermission> permissions)
    {
        _permService = permService;
        _permissions = permissions;
    }

    // ── List (read-only — assign/revoke happens on Role detail page) ──────────

    [HttpGet("")]
    [FcmsAuthorize("roles.permissions")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var all = await _permissions.GetAllAsync(ct);
        var groups = all
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Group) ? "Other" : p.Group)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.DisplayName).ToList());
        return View(groups);
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
