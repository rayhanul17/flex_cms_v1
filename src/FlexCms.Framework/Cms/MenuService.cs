using FlexCms.Framework.Db;
using FlexCms.Framework.Models;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace FlexCms.Framework.Cms;

public class MenuService : IMenuService
{
    private const string CacheKeyPrefix = "fcms_menu_";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    private readonly IRepository<FcmsMenuItem> _repo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IPermissionService _permService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _cache;

    public MenuService(
        IRepository<FcmsMenuItem> repo,
        IFcmsUnitOfWork uow,
        IPermissionService permService,
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache cache)
    {
        _repo = repo;
        _uow = uow;
        _permService = permService;
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
    }

    public async Task<List<FcmsMenuItem>> GetMenuAsync(string location, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyPrefix + location;

        if (!_cache.TryGetValue(cacheKey, out List<FcmsMenuItem>? allItems) || allItems is null)
        {
            allItems = await _repo.FindAsync(m => m.Location == location, ct);
            allItems = [.. allItems.OrderBy(m => m.Order)];
            _cache.Set(cacheKey, allItems, CacheTtl);
        }

        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null) return [];

        var filtered = new List<FcmsMenuItem>();
        foreach (var item in allItems)
        {
            if (item.RequiredPermission is null ||
                await _permService.HasPermissionAsync(user, item.RequiredPermission, ct))
            {
                filtered.Add(item);
            }
        }
        return filtered;
    }

    public async Task SeedAsync(string moduleId, List<FcmsMenuItemDef> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        // Existing items keyed by URL — URL is the stable identity per module
        var existing = (await _repo.FindAsync(m => m.ModuleId == moduleId, ct, includeDeleted: true))
            .ToDictionary(m => m.Url, StringComparer.OrdinalIgnoreCase);

        var anyChange = false;
        foreach (var def in items)
        {
            if (existing.TryGetValue(def.Url, out var existingItem))
            {
                // Refresh code-owned fields on upgrade; preserve CustomName + Order (admin's customizations)
                var changed = existingItem.DefaultName != def.DefaultName
                           || existingItem.Icon != def.Icon
                           || existingItem.RequiredPermission != def.RequiredPermission
                           || existingItem.Location != def.Location
                           || existingItem.IsDeleted; // restore if soft-deleted by deactivation

                if (!changed) continue;

                existingItem.DefaultName = def.DefaultName;
                existingItem.Icon = def.Icon;
                existingItem.RequiredPermission = def.RequiredPermission;
                existingItem.Location = def.Location;
                existingItem.IsDeleted = false;
                existingItem.DeletedAt = null;
                await _repo.UpdateAsync(existingItem, ct);
                anyChange = true;
                continue;
            }

            await _repo.AddAsync(new FcmsMenuItem
            {
                ModuleId = moduleId,
                Location = def.Location,
                DefaultName = def.DefaultName,
                Icon = def.Icon,
                Url = def.Url,
                ParentId = def.ParentId,
                Order = def.Order,
                RequiredPermission = def.RequiredPermission
            }, ct);
            anyChange = true;
        }

        if (anyChange)
        {
            await _uow.SaveChangesAsync(ct);
            InvalidateCache();
        }
    }

    public async Task RemoveModuleItemsAsync(string moduleId, CancellationToken ct = default)
    {
        var items = await _repo.FindAsync(m => m.ModuleId == moduleId, ct);
        if (items.Count == 0) return;

        await _repo.SoftDeleteRangeAsync(items, ct);
        await _uow.SaveChangesAsync(ct);
        InvalidateCache();
    }

    public async Task RenameAsync(Guid id, string? customName, CancellationToken ct = default)
    {
        var item = await _repo.GetByIdAsync(id, ct);
        if (item is null) return;

        item.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
        await _repo.UpdateAsync(item, ct);
        await _uow.SaveChangesAsync(ct);
        InvalidateCache(item.Location);
    }

    public async Task ReorderAsync(Dictionary<Guid, int> orders, CancellationToken ct = default)
    {
        if (orders.Count == 0) return;

        var items = await _repo.GetByIdsAsync(orders.Keys, ct);
        foreach (var item in items)
        {
            if (orders.TryGetValue(item.Id, out var order))
                item.Order = order;
        }

        await _repo.UpdateRangeAsync(items, ct);
        await _uow.SaveChangesAsync(ct);
        InvalidateCache();
    }

    public void InvalidateCache(string? location = null)
    {
        if (location is not null)
        {
            _cache.Remove(CacheKeyPrefix + location);
        }
        else
        {
            foreach (var loc in new[] { "AdminSidebar", "MainMenu", "FooterMenu" })
                _cache.Remove(CacheKeyPrefix + loc);
        }
    }
}
