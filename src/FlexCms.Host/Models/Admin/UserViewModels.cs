using System.ComponentModel.DataAnnotations;
using FlexCms.Framework.Db;

namespace FlexCms.Host.Models.Admin;

public class UserListItemViewModel
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? DisplayName { get; set; }
    public string ResolvedDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? FullName : DisplayName;
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public bool IsActive => Status == EntityStatus.Active;
    public bool ForcePasswordChange { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Roles { get; set; } = [];
}

public class CreateUserViewModel
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = "";

    [Required, MaxLength(200)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = "";

    [MaxLength(200)]
    [Display(Name = "Display Name")]
    public string? DisplayName { get; set; }

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = "";

    [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = "";

    public bool ForcePasswordChange { get; set; }
    public List<Guid> SelectedRoleIds { get; set; } = [];
    public List<RoleSelectItem> AvailableRoles { get; set; } = [];
}

public class EditUserViewModel
{
    public Guid Id { get; set; }

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = "";

    [Required, MaxLength(200)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = "";

    [MaxLength(200)]
    [Display(Name = "Display Name")]
    public string? DisplayName { get; set; }

    public bool ForcePasswordChange { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Guid> SelectedRoleIds { get; set; } = [];
    public List<RoleSelectItem> AvailableRoles { get; set; } = [];
}

public class RoleSelectItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
