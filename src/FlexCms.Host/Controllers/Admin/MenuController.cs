using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/menu")]
public class MenuController : BaseAdminController
{
    private readonly IMenuService _menuService;

    public MenuController(IMenuService menuService)
    {
        _menuService = menuService;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet("")]
    [FcmsAuthorize("settings.manage")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await _menuService.GetMenuAsync("AdminSidebar", ct);
        return View(items);
    }

    // ── Rename (AJAX) ─────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/rename")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("settings.manage")]
    [FcmsLog("menu.rename", "FcmsMenuItem")]
    public async Task<IActionResult> Rename(Guid id, [FromForm] string? customName, CancellationToken ct)
    {
        await _menuService.RenameAsync(id, customName, ct);
        return FcmsOk("Renamed.");
    }

    // ── Reorder (AJAX — receives JSON array [{id, order}]) ────────────────────

    [HttpPost("reorder")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("settings.manage")]
    [FcmsLog("menu.reorder", "FcmsMenuItem")]
    public async Task<IActionResult> Reorder([FromBody] List<MenuOrderItem> items, CancellationToken ct)
    {
        if (items is null || items.Count == 0)
            return FcmsFail("No items provided.");

        var dict = items.ToDictionary(x => x.Id, x => x.Order);
        await _menuService.ReorderAsync(dict, ct);
        return FcmsOk("Order saved.");
    }

    public record MenuOrderItem(Guid Id, int Order);
}
