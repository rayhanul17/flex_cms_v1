using Microsoft.AspNetCore.Identity;

namespace FlexCms.Framework.Auth;

public class FcmsRole : IdentityRole<Guid>
{
    public override Guid Id { get => base.Id; set => base.Id = value; }

    public FcmsRole() { }
    public FcmsRole(string roleName) : base(roleName) { }

    /// <summary>
    /// Where to redirect after login. Empty = fall back to returnUrl or "/".
    /// SuperAdmin always goes to /admin regardless of this value.
    /// </summary>
    public string LoginRedirectUrl { get; set; } = string.Empty;

    /// <summary>
    /// When a user has multiple roles the highest Priority wins.
    /// Default 0. SuperAdmin is treated as int.MaxValue internally (always wins).
    /// </summary>
    public int Priority { get; set; }

    public List<IdentityRoleClaim<Guid>> Claims { get; set; } = [];
}
