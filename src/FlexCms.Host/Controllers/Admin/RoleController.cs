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


    [HttpGet("create")]
    [FcmsAuthorize(FcmsPermissions.RolesCreate)]
    public IActionResult Create() => View(new CreateRoleViewModel());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.RolesCreate)]
    [FcmsLog("roles.create", "FcmsRole")]
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
            LoginRedirectUrl = NormalizeRedirect(model.LoginRedirectUrl),
            Priority = model.Priority
        };

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError("", err.Description);
            return View(model);
        }

        FcmsLogContext.SetEntityId(HttpContext, role.Id);
        FcmsLogContext.SetValue(HttpContext, role);
        ShowSuccess($"Role '{model.Name}' created.");
        return RedirectToAction(nameof(Index));
    }


    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(FcmsPermissions.RolesEdit)]
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
    [FcmsAuthorize(FcmsPermissions.RolesEdit)]
    [FcmsLog("roles.edit", "FcmsRole")]
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

        role.LoginRedirectUrl = NormalizeRedirect(model.LoginRedirectUrl);
        role.Priority = model.Priority;

        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError("", err.Description);
            return View(model);
        }

        FcmsLogContext.SetValue(HttpContext, role);
        ShowSuccess($"Role '{role.Name}' updated.");
        return RedirectToAction(nameof(Detail), new { id });
    }


    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null) return NotFound();

        var users = await _userManager.GetUsersInRoleAsync(role.Name ?? "");
        var assignedKeys = await _permService.GetRolePermissionKeysAsync(id, ct);
        var allPerms = await _permissions.GetAllAsync(ct);

        var groups = allPerms
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


    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.RolesDelete)]
    [FcmsLog("roles.delete", "FcmsRole")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null) return FcmsFail("Role not found.");

        if (role.Name == FcmsRoles.SuperAdmin)
            return FcmsFail("The SuperAdmin role cannot be deleted.");

        FcmsLogContext.SetValue(HttpContext, role);
        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
            return FcmsFail(string.Join(", ", result.Errors.Select(e => e.Description)));

        ShowSuccess($"Role '{role.Name}' deleted.");
        return FcmsOk("Role deleted.");
    }

    /// <summary>
    /// Empty/whitespace login-redirect URL defaults to "/" so admins aren't
    /// forced to type a slash for every new role.
    /// </summary>
    private static string NormalizeRedirect(string? url)
        => string.IsNullOrWhiteSpace(url) ? "/" : url.Trim();
}
