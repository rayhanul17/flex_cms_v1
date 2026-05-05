using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Persisted admin sidebar menu item. Seeded by core + module activation;
/// admin can rename (CustomName) and reorder (Order) at runtime.
/// </summary>
public class FcmsMenuItem : BaseEfEntity
{
    /// <summary>Module that owns this item. "core" for built-in items.</summary>
    public string ModuleId { get; set; } = "core";

    /// <summary>Menu area: "AdminSidebar", "MainMenu", "FooterMenu".</summary>
    public string Location { get; set; } = "AdminSidebar";

    /// <summary>Original name from code — never overwritten after seed.</summary>
    public string DefaultName { get; set; } = string.Empty;

    /// <summary>Admin-editable display name. Null = use DefaultName.</summary>
    public string? CustomName { get; set; }

    public string Icon { get; set; } = "bi bi-circle";

    public string Url { get; set; } = string.Empty;

    /// <summary>Null = top-level item; Guid = sub-item under parent.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Lower = higher in list.</summary>
    public int Order { get; set; }

    /// <summary>Permission key required to see this item. Null = always visible.</summary>
    public string? RequiredPermission { get; set; }

    /// <summary>Display name shown in sidebar (CustomName ?? DefaultName).</summary>
    public string DisplayName => CustomName ?? DefaultName;
}
