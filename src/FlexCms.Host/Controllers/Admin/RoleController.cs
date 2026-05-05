using FlexCms.Framework.Auth;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/roles")]
public class RoleController : BaseAdminController
{
    private readonly RoleManager<FcmsRole> _roleManager;
    private readonly UserManager<FcmsUser> _userManager;
    private readonly IPermissionService _permService;
    private readonly IRepository<FcmsPermission> _permissions;

    public RoleController(
        RoleManager<FcmsRole> roleManager,
        UserManager<FcmsUser> userManager,
        IPermissionService permService,
        IRepository<FcmsPermission> permissions)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _permService = permService;
        _permissions = permissions;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var roles = _roleManager.Roles.OrderByDescending(r => r.Priority).ThenBy(r => r.Name).ToList();
        var list = new List<RoleListItemViewModel>();

        foreach (var role in roles)
        {
            var users = await _userManager.GetUsersInRoleAsync(role.Name ?? "");
            var assigned = await _permService.GetRolePermissionKeysAsync(role.Id, ct);
            list.Add(new RoleListItemViewModel
            {
                Id = role.Id,
                Name = role.Name ?? "",
                UserCount = users.Count,
                PermissionCount = assigned.Count,
                Priority = role.Priority,
                LoginRedirectUrl = role.LoginRedirectUrl
            });
        }

        return View(list);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpGet("create")]
    [FcmsAuthorize("roles.create")]
    public IActionResult Create() => View(new CreateRoleViewModel());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("roles.create")]
    public async Task<IActionResult> Create(CreateRoleViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        if (await _roleManager.RoleExistsAsync(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "A role with this name already exists.");
            return View(model);
        }

        var role = new FcmsRole
        {
            Name = model.Name,
            LoginRedirectUrl = model.LoginRedirectUrl.Trim(),
            Priority = model.Priority
        };

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError("", err.Description);
            return View(model);
        }

        await OpLog.LogAsync("roles.create", "FcmsRole", role.Id.ToString());
        ShowSuccess($"Role '{model.Name}' created.");
        return RedirectToAction(nameof(Index));
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize("roles.edit")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null) return NotFound();

        return View(new EditRoleViewModel
        {
            Id = role.Id,
            Name = role.Name ?? "",
            LoginRedirectUrl = role.LoginRedirectUrl,
            Priority = role.Priority
        });
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("roles.edit")]
    public async Task<IActionResult> Edit(Guid id, EditRoleViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null) return NotFound();

        // SuperAdmin name is immutable
        if (role.Name != FcmsRoles.SuperAdmin)
        {
            var conflict = await _roleManager.FindByNameAsync(model.Name);
            if (conflict is not null && conflict.Id != id)
            {
                ModelState.AddModelError(nameof(model.Name), "A role with this name already exists.");
                return View(model);
            }
            role.Name = model.Name;
        }

        role.LoginRedirectUrl = model.LoginRedirectUrl.Trim();
        role.Priority = model.Priority;

        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError("", err.Description);
            return View(model);
        }

        await OpLog.LogAsync("roles.edit", "FcmsRole", role.Id.ToString());
        ShowSuccess($"Role '{role.Name}' updated.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    // ── Detail (Permissions + Users tabs) ─────────────────────────────────────

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null) return NotFound();

        var users = await _userManager.GetUsersInRoleAsync(role.Name ?? "");
        var assignedKeys = await _permService.GetRolePermissionKeysAsync(id, ct);
        var allPerms = await _permissions.GetAllAsync(ct);

        var groups = allPerms
            .Where(p => !p.IsDeleted)
            .GroupBy(p => p.Group)
            .OrderBy(g => g.Key)
            .Select(g => new PermissionGroupViewModel
            {
                Group = g.Key,
                Permissions = g.OrderBy(p => p.DisplayName).Select(p => new PermissionItemViewModel
                {
                    Key = p.Key,
                    DisplayName = p.DisplayName,
                    IsAssigned = assignedKeys.Contains(p.Key)
                }).ToList()
            }).ToList();

        return View(new RoleDetailViewModel
        {
            Id = role.Id,
            Name = role.Name ?? "",
            LoginRedirectUrl = role.LoginRedirectUrl,
            Priority = role.Priority,
            Users = users.Select(u => new RoleUserItem { Id = u.Id, Email = u.Email ?? "" }).ToList(),
            PermissionGroups = groups,
            AssignedPermissionKeys = [.. assignedKeys]
        });
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("roles.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null) return FcmsFail("Role not found.");

        if (role.Name == FcmsRoles.SuperAdmin)
            return FcmsFail("The SuperAdmin role cannot be deleted.");

        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
            return FcmsFail(string.Join(", ", result.Errors.Select(e => e.Description)));

        await OpLog.LogAsync("roles.delete", "FcmsRole", role.Id.ToString());
        ShowSuccess($"Role '{role.Name}' deleted.");
        return FcmsOk("Role deleted.");
    }
}
