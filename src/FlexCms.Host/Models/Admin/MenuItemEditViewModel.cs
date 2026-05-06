using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Admin;

public class MenuItemEditViewModel
{
    public Guid Id { get; set; }

    public string ModuleId { get; set; } = "core";

    public string Location { get; set; } = "AdminSidebar";

    [Required, StringLength(100)]
    [Display(Name = "Default Name")]
    public string? DefaultName { get; set; }

    [StringLength(100)]
    [Display(Name = "Custom Name (admin override)")]
    public string? CustomName { get; set; }

    [Display(Name = "Icon (Bootstrap Icons class)")]
    public string Icon { get; set; } = "bi bi-circle";

    [Required, StringLength(255)]
    [Display(Name = "URL (use # prefix for parent-only items)")]
    public string? Url { get; set; }

    [Display(Name = "Parent")]
    public Guid? ParentId { get; set; }

    [Display(Name = "Order (lower = higher in list)")]
    public int Order { get; set; }

    [Display(Name = "Required Permission Key")]
    public string? RequiredPermission { get; set; }

    public List<MenuItemSelectItem> AvailableParents { get; set; } = [];
    public List<MenuItemSelectItem> AvailablePermissions { get; set; } = [];
}

public class MenuItemSelectItem
{
    public Guid Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
}
