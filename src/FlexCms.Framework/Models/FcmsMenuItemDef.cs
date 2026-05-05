namespace FlexCms.Framework.Models;

/// <summary>
/// Module-declared menu item definition. Used by <see cref="Modules.IFcmsModule.GetMenuItems"/>
/// to seed <see cref="Cms.FcmsMenuItem"/> rows on module activation.
/// </summary>
public class FcmsMenuItemDef
{
    public string Location { get; set; } = "AdminSidebar";
    public string DefaultName { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi bi-circle";
    public string Url { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int Order { get; set; }

    /// <summary>Null = always visible to authenticated users.</summary>
    public string? RequiredPermission { get; set; }
}
