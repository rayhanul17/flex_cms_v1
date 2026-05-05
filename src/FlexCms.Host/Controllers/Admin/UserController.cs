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

    public UserController(UserManager<FcmsUser> userManager, RoleManager<FcmsRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = _userManager.Users.OrderByDescending(u => u.CreatedAt).ToList();
        var list = new List<UserListItemViewModel>();

        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            list.Add(new UserListItemViewModel
            {
                Id = u.Id,
                Email = u.Email ?? "",
                DisplayName = u.UserName,
                IsActive = !u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd < DateTimeOffset.UtcNow,
                ForcePasswordChange = u.ForcePasswordChange,
                CreatedAt = u.CreatedAt,
                Roles = [.. roles]
            });
        }

        return View(list);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpGet("create")]
    [FcmsAuthorize("users.create")]
    public async Task<IActionResult> Create()
        => View(new CreateUserViewModel { AvailableRoles = await GetRoleSelectItemsAsync() });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("users.create")]
    public async Task<IActionResult> Create(CreateUserViewModel model, CancellationToken ct)
    {
        model.AvailableRoles = await GetRoleSelectItemsAsync();
        if (!ModelState.IsValid) return View(model);

        var user = new FcmsUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
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

        await OpLog.LogAsync("users.create", "FcmsUser", user.Id.ToString(), ct: ct);
        ShowSuccess($"User '{model.Email}' created.");
        return RedirectToAction(nameof(Index));
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize("users.edit")]
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
            DisplayName = user.UserName,
            ForcePasswordChange = user.ForcePasswordChange,
            AvailableRoles = allRoles,
            SelectedRoleIds = selectedIds
        });
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("users.edit")]
    public async Task<IActionResult> Edit(Guid id, EditUserViewModel model, CancellationToken ct)
    {
        model.AvailableRoles = await GetRoleSelectItemsAsync();
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        user.Email = model.Email;
        user.UserName = model.Email;
        user.ForcePasswordChange = model.ForcePasswordChange;
        await _userManager.UpdateAsync(user);

        // Sync roles
        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);

        foreach (var roleId in model.SelectedRoleIds)
        {
            var role = await _roleManager.FindByIdAsync(roleId.ToString());
            if (role?.Name is not null)
                await _userManager.AddToRoleAsync(user, role.Name);
        }

        await OpLog.LogAsync("users.edit", "FcmsUser", user.Id.ToString(), ct: ct);
        ShowSuccess($"User '{model.Email}' updated.");
        return RedirectToAction(nameof(Index));
    }

    // ── Active toggle (AJAX) ──────────────────────────────────────────────────

    [HttpPost("{id:guid}/toggle-active")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("users.edit")]
    public async Task<IActionResult> ToggleActive(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return FcmsFail("User not found.");

        // Lockout to disable: set LockoutEnd far future; remove to enable
        var isCurrentlyLocked = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow;
        if (isCurrentlyLocked)
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
            await OpLog.LogAsync("users.activate", "FcmsUser", user.Id.ToString());
            return FcmsOk("User activated.");
        }
        else
        {
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            await OpLog.LogAsync("users.deactivate", "FcmsUser", user.Id.ToString());
            return FcmsOk("User deactivated.");
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("users.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return FcmsFail("User not found.");

        if (user.Id == FcmsContext.UserId)
            return FcmsFail("You cannot delete your own account.");

        await _userManager.DeleteAsync(user);
        await OpLog.LogAsync("users.delete", "FcmsUser", user.Id.ToString());
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
