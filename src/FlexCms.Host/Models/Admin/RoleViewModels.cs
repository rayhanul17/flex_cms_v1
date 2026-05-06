using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Admin;

public class RoleListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int UserCount { get; set; }
    public int PermissionCount { get; set; }
    public int Priority { get; set; }
    public string LoginRedirectUrl { get; set; } = "";
}

public class CreateRoleViewModel
{
    [Required, MaxLength(256)]
    public string Name { get; set; } = "";

    [MaxLength(512)]
    [Display(Name = "Login Redirect URL")]
    public string LoginRedirectUrl { get; set; } = "";

    [Display(Name = "Priority")]
    [Range(0, 9999)]
    public int Priority { get; set; }
}

public class EditRoleViewModel
{
    public Guid Id { get; set; }

    [Required, MaxLength(256)]
    public string Name { get; set; } = "";

    [MaxLength(512)]
    [Display(Name = "Login Redirect URL")]
    public string LoginRedirectUrl { get; set; } = "";

    [Display(Name = "Priority")]
    [Range(0, 9999)]
    public int Priority { get; set; }
}

public class RoleDetailViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string LoginRedirectUrl { get; set; } = "";
    public int Priority { get; set; }
    public List<RoleUserItem> Users { get; set; } = [];
    public List<PermissionGroupViewModel> PermissionGroups { get; set; } = [];
    public HashSet<string> AssignedPermissionKeys { get; set; } = [];
}

public class RoleUserItem
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
}

public class PermissionGroupViewModel
{
    public string Group { get; set; } = "";
    public List<PermissionItemViewModel> Permissions { get; set; } = [];
}

public class PermissionItemViewModel
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsAssigned { get; set; }
}
