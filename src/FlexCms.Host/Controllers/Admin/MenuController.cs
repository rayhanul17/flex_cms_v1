using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/menu")]
public class MenuController : BaseAdminController
{
    private readonly IMenuService _menuService;
    private readonly IRepository<FcmsPermission> _permissions;

    public MenuController(IMenuService menuService, IRepository<FcmsPermission> permissions)
    {
        _menuService = menuService;
        _permissions = permissions;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await _menuService.GetAllForAdminAsync("AdminSidebar", ct);
        return View(items);
    }

    // ── Create / Edit (single combined view) ──────────────────────────────────

    [HttpGet("create")]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    public async Task<IActionResult> Create(CancellationToken ct)
        => View("Edit", await BuildVmAsync(new FcmsMenuItem { ModuleId = "core" }, ct));

    [HttpGet("{id:guid}/edit")]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var item = await _menuService.GetByIdAsync(id, ct);
        if (item is null) return NotFound();
        return View(await BuildVmAsync(item, ct));
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    [FcmsLog("menu.save", "FcmsMenuItem")]
    public async Task<IActionResult> Save(MenuItemEditViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View("Edit", await BuildVmAsync(ToEntity(vm), ct, vm));

        var item = ToEntity(vm);
        if (string.IsNullOrWhiteSpace(item.ModuleId)) item.ModuleId = "core";
        if (string.IsNullOrWhiteSpace(item.Location)) item.Location = "AdminSidebar";

        await _menuService.SaveAsync(item, ct);
        FcmsLogContext.SetEntityId(HttpContext, item.Id);
        FcmsLogContext.SetValue(HttpContext, new { item.DefaultName, item.CustomName, item.Url, item.Icon, item.ParentId, item.Order, item.RequiredPermission });
        ShowSuccess(vm.Id == Guid.Empty ? "Menu item created." : "Menu item updated.");
        return RedirectToAction(nameof(Index));
    }

    // ── Delete (AJAX) ─────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    [FcmsLog("menu.delete", "FcmsMenuItem")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var item = await _menuService.GetByIdAsync(id, ct);
        if (item is not null)
            FcmsLogContext.SetValue(HttpContext, new { item.DefaultName, item.Url });
        await _menuService.DeleteAsync(id, ct);
        return FcmsOk("Deleted.");
    }

    // ── Rename (AJAX — inline edit on Index) ─────────────────────────────────

    [HttpPost("{id:guid}/rename")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    [FcmsLog("menu.rename", "FcmsMenuItem")]
    public async Task<IActionResult> Rename(Guid id, [FromForm] string? customName, CancellationToken ct)
    {
        FcmsLogContext.SetValue(HttpContext, new { CustomName = customName });
        await _menuService.RenameAsync(id, customName, ct);
        return FcmsOk("Renamed.");
    }

    // ── Reorder (AJAX) ────────────────────────────────────────────────────────

    [HttpPost("reorder")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    [FcmsLog("menu.reorder", "FcmsMenuItem")]
    public async Task<IActionResult> Reorder([FromBody] List<MenuOrderItem> items, CancellationToken ct)
    {
        if (items is null || items.Count == 0)
            return FcmsFail("No items provided.");

        var dict = items.ToDictionary(x => x.Id, x => x.Order);
        await _menuService.ReorderAsync(dict, ct);
        return FcmsOk("Order saved.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<MenuItemEditViewModel> BuildVmAsync(
        FcmsMenuItem item, CancellationToken ct, MenuItemEditViewModel? source = null)
    {
        var allItems = await _menuService.GetAllForAdminAsync(item.Location, ct);
        var permissions = await _permissions.GetAllAsync(ct);

        var vm = source ?? new MenuItemEditViewModel
        {
            Id = item.Id,
            ModuleId = item.ModuleId,
            Location = item.Location,
            DefaultName = item.DefaultName,
            CustomName = item.CustomName,
            Icon = string.IsNullOrWhiteSpace(item.Icon) ? "bi bi-circle" : item.Icon,
            Url = item.Url,
            ParentId = item.ParentId,
            Order = item.Order,
            RequiredPermission = item.RequiredPermission
        };

        // Top-level items only (can't nest a parent under another parent for now)
        vm.AvailableParents = [.. allItems
            .Where(m => m.ParentId is null && m.Id != item.Id)
            .Select(m => new MenuItemSelectItem { Id = m.Id, Name = m.DisplayName })];

        vm.AvailablePermissions = [.. permissions
            .OrderBy(p => p.Group).ThenBy(p => p.DisplayName)
            .Select(p => new MenuItemSelectItem { Key = p.Key, Name = $"{p.Group} — {p.DisplayName}" })];

        return vm;
    }

    private static FcmsMenuItem ToEntity(MenuItemEditViewModel vm) => new()
    {
        Id = vm.Id,
        ModuleId = string.IsNullOrWhiteSpace(vm.ModuleId) ? "core" : vm.ModuleId,
        Location = string.IsNullOrWhiteSpace(vm.Location) ? "AdminSidebar" : vm.Location,
        DefaultName = vm.DefaultName ?? "",
        CustomName = string.IsNullOrWhiteSpace(vm.CustomName) ? null : vm.CustomName,
        Icon = string.IsNullOrWhiteSpace(vm.Icon) ? "bi bi-circle" : vm.Icon,
        Url = vm.Url ?? "",
        ParentId = vm.ParentId == Guid.Empty ? null : vm.ParentId,
        Order = vm.Order,
        RequiredPermission = string.IsNullOrWhiteSpace(vm.RequiredPermission) ? null : vm.RequiredPermission
    };

    public record MenuOrderItem(Guid Id, int Order);
}
