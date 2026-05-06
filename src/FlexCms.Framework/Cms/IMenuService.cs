using FlexCms.Framework.Models;

namespace FlexCms.Framework.Cms;

public interface IMenuService
{
    /// <summary>
    /// Returns menu items for a location, filtered by the current user's permissions.
    /// Result is cached per location (15 min); permission filtering is per-request.
    /// </summary>
    Task<List<FcmsMenuItem>> GetMenuAsync(string location, CancellationToken ct = default);

    /// <summary>Seed items for a module. Skips items that already exist (by ModuleId + Url).</summary>
    Task SeedAsync(string moduleId, List<FcmsMenuItemDef> items, CancellationToken ct = default);

    /// <summary>Remove all menu items belonging to a module.</summary>
    Task RemoveModuleItemsAsync(string moduleId, CancellationToken ct = default);

    /// <summary>Update display name of a single item.</summary>
    Task RenameAsync(Guid id, string? customName, CancellationToken ct = default);

    /// <summary>Bulk-update Order for a list of (id → order) pairs.</summary>
    Task ReorderAsync(Dictionary<Guid, int> orders, CancellationToken ct = default);

    /// <summary>Invalidate the cached menu for a location (or all locations if null).</summary>
    void InvalidateCache(string? location = null);

    /// <summary>Get all items for a location (no permission filter, includes admin-only fields). For admin UI.</summary>
    Task<List<FcmsMenuItem>> GetAllForAdminAsync(string location, CancellationToken ct = default);

    /// <summary>Get one item by id (no permission filter).</summary>
    Task<FcmsMenuItem?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Create or update a single item (admin UI). Returns the saved item.</summary>
    Task<FcmsMenuItem> SaveAsync(FcmsMenuItem item, CancellationToken ct = default);

    /// <summary>Soft-delete a menu item.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
