using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Admin;

public class RoleListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int UserCount { get; set; }
    public int PermissionCount { get; set; }
}

public class CreateRoleViewModel
{
    [Required, MaxLength(256)]
    public string Name { get; set; } = "";
}

public class RoleDetailViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
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
