using FlexCms.Framework.Auth;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/users")]
public class UserController : BaseAdminController
{
    private readonly UserManager<FcmsUser> _userManager;
    private readonly RoleManager<FcmsRole> _roleManager;

    // Uses UserManager.GetRolesAsync rather than a direct DbContext join — the
    // per-user N+1 cost is acceptable for typical admin user counts (<100).
    public UserController(UserManager<FcmsUser> userManager, RoleManager<FcmsRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = _userManager.Users
            .OrderByDescending(u => u.CreatedAt)
            .ToList();
        if (users.Count == 0) return View(new List<UserListItemViewModel>());

        var list = new List<UserListItemViewModel>(users.Count);
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            list.Add(new UserListItemViewModel
            {
                Id = u.Id,
                Email = u.Email ?? "",
                FullName = u.FullName,
                DisplayName = u.DisplayName,
                Status = u.Status,
                ForcePasswordChange = u.ForcePasswordChange,
                CreatedAt = u.CreatedAt,
                Roles = roles.ToList()
            });
        }

        return View(list);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpGet("create")]
    [FcmsAuthorize(FcmsPermissions.UsersCreate)]
    public async Task<IActionResult> Create()
        => View(new CreateUserViewModel { AvailableRoles = await GetRoleSelectItemsAsync() });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.UsersCreate)]
    [FcmsLog("users.create", "FcmsUser")]
    public async Task<IActionResult> Create(CreateUserViewModel model, CancellationToken ct)
    {
        model.AvailableRoles = await GetRoleSelectItemsAsync();
        if (!ModelState.IsValid) return View(model);

        var user = new FcmsUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            FullName = model.FullName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName.Trim(),
            ForcePasswordChange = model.ForcePasswordChange
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError("", err.Description);
            return View(model);
        }

        foreach (var roleId in model.SelectedRoleIds)
        {
            var role = await _roleManager.FindByIdAsync(roleId.ToString());
            if (role?.Name is not null)
                await _userManager.AddToRoleAsync(user, role.Name);
        }

        FcmsLogContext.SetEntityId(HttpContext, user.Id);
        FcmsLogContext.SetValue(HttpContext, user);
        ShowSuccess($"User '{model.Email}' created.");
        return RedirectToAction(nameof(Index));
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(FcmsPermissions.UsersEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var userRoles = await _userManager.GetRolesAsync(user);
        var allRoles = await GetRoleSelectItemsAsync();
        var selectedIds = allRoles
            .Where(r => userRoles.Contains(r.Name))
            .Select(r => r.Id)
            .ToList();

        return View(new EditUserViewModel
        {
            Id = user.Id,
            Email = user.Email ?? "",
            FullName = user.FullName,
            DisplayName = user.DisplayName,
            ForcePasswordChange = user.ForcePasswordChange,
            AvailableRoles = allRoles,
            SelectedRoleIds = selectedIds
        });
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.UsersEdit)]
    [FcmsLog("users.edit", "FcmsUser")]
    public async Task<IActionResult> Edit(Guid id, EditUserViewModel model, CancellationToken ct)
    {
        model.AvailableRoles = await GetRoleSelectItemsAsync();
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        user.Email = model.Email;
        user.UserName = model.Email;
        user.FullName = model.FullName.Trim();
        user.DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName.Trim();
        user.ForcePasswordChange = model.ForcePasswordChange;
        await _userManager.UpdateAsync(user);

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);

        foreach (var roleId in model.SelectedRoleIds)
        {
            var role = await _roleManager.FindByIdAsync(roleId.ToString());
            if (role?.Name is not null)
                await _userManager.AddToRoleAsync(user, role.Name);
        }

        FcmsLogContext.SetValue(HttpContext, user);
        ShowSuccess($"User '{model.Email}' updated.");
        return RedirectToAction(nameof(Index));
    }

    // ── Active toggle (AJAX) ──────────────────────────────────────────────────

    [HttpPost("{id:guid}/toggle-active")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.UsersEdit)]
    public async Task<IActionResult> ToggleActive(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return FcmsFail("User not found.");

        if (user.Status == FlexCms.Framework.Db.EntityStatus.Active)
        {
            user.Status = FlexCms.Framework.Db.EntityStatus.InActive;
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            await _userManager.UpdateAsync(user);
            await OpLog.LogAsync("users.deactivate", "FcmsUser", user.Id.ToString(), value: user);
            return FcmsOk("User deactivated.", new { newStatus = "InActive" });
        }
        else
        {
            user.Status = FlexCms.Framework.Db.EntityStatus.Active;
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.UpdateAsync(user);
            await OpLog.LogAsync("users.activate", "FcmsUser", user.Id.ToString(), value: user);
            return FcmsOk("User activated.", new { newStatus = "Active" });
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.UsersDelete)]
    [FcmsLog("users.delete", "FcmsUser")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return FcmsFail("User not found.");

        if (user.Id == FcmsContext.UserId)
            return FcmsFail("You cannot delete your own account.");

        FcmsLogContext.SetValue(HttpContext, user);
        await _userManager.DeleteAsync(user);
        ShowSuccess("User deleted.");
        return FcmsOk("User deleted.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<List<RoleSelectItem>> GetRoleSelectItemsAsync()
    {
        var roles = _roleManager.Roles
            .OrderBy(r => r.Name)
            .Select(r => new RoleSelectItem { Id = r.Id, Name = r.Name ?? "" })
            .ToList();
        return Task.FromResult(roles);
    }
}
