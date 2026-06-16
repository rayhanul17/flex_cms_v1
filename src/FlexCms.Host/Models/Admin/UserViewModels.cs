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
    public string? ImageUrl { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public bool IsActive => Status == EntityStatus.Active;
    public bool ForcePasswordChange { get; set; }
    public DateTime? BlockedUntil { get; set; }
    public string? BlockReason { get; set; }

    /// <summary>True when an admin-block window is currently in effect (BlockedUntil in the future).</summary>
    public bool IsBlocked => BlockedUntil.HasValue && BlockedUntil.Value > DateTime.UtcNow;

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

    [MaxLength(500), Url]
    [Display(Name = "Profile Image URL")]
    public string? ImageUrl { get; set; }

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

    [MaxLength(500), Url]
    [Display(Name = "Profile Image URL")]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public List<Guid> SelectedRoleIds { get; set; } = [];
    public List<RoleSelectItem> AvailableRoles { get; set; } = [];

    // Read-only block status surfaced so the Edit form can show a "Blocked
    // until X — Unblock" banner without a second DB round-trip.
    public DateTime? BlockedUntil { get; set; }
    public string? BlockReason { get; set; }
    public bool IsBlocked => BlockedUntil.HasValue && BlockedUntil.Value > DateTime.UtcNow;
}

/// <summary>
/// Modal form posted by the Block dialog. Validation enforces that the
/// end-time is in the future — past-dated blocks are silently a no-op and
/// confuse moderators.
/// </summary>
public class BlockUserViewModel
{
    [Required]
    [Display(Name = "Blocked Until (UTC)")]
    public DateTime BlockedUntil { get; set; } = DateTime.UtcNow.AddDays(7);

    [Required, MaxLength(500)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = "";
}

public class RoleSelectItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
