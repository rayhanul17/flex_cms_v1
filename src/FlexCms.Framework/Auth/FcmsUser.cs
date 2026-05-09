using System.Security.Claims;
using FlexCms.Framework.Db;
using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCms.Framework.Auth;

public class FcmsUser : IdentityUser<Guid>
{
    public override Guid Id { get => base.Id; set => base.Id = value; }

    /// <summary>Legal / full name — required for all accounts.</summary>
    public string FullName { get; set; } = "";

    /// <summary>
    /// Optional public-facing alias. Falls back to <see cref="FullName"/> when blank.
    /// Use <see cref="ResolvedDisplayName"/> everywhere a human-readable name is shown.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>Returns DisplayName if set, otherwise FullName.</summary>
    public string ResolvedDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? FullName : DisplayName;

    public bool ForcePasswordChange { get; set; }
    public DateTime CreatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;
    public DateTime UpdatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;

    /// <summary>
    /// Selected 2FA channel — only honored when Identity's
    /// <see cref="Microsoft.AspNetCore.Identity.IdentityUser.TwoFactorEnabled"/>
    /// is also true. Lets users pick email vs SMS without changing the
    /// Identity-managed boolean toggle.
    /// </summary>
    [BsonRepresentation(BsonType.Int32)]
    public Auth.TwoFactor.TwoFactorChannel TwoFactorChannel { get; set; } = Auth.TwoFactor.TwoFactorChannel.Email;

    /// <summary>BCrypt hash of the most-recent issued OTP. Cleared on consume.</summary>
    public string? PendingOtpHash { get; set; }
    public DateTime? PendingOtpExpiresAt { get; set; }
    /// <summary>Per-user attempt counter on the current pending OTP. Reset on issue. Locks the OTP after 5 wrong tries.</summary>
    public int PendingOtpAttempts { get; set; }

    /// <summary>
    /// User lifecycle status. Source of truth for the admin UI active/deactive toggle.
    /// Auth-time blocking is enforced via Identity's <c>LockoutEnd</c>; the controller
    /// keeps the two in sync (Active ⇔ LockoutEnd null/past).
    /// MongoDB: stored as <see cref="MongoDB.Bson.BsonType.Int32"/>.
    /// </summary>
    [BsonRepresentation(BsonType.Int32)]
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public List<string> Roles { get; set; } = [];

    // Embedded collections for MongoDB
    public List<IdentityUserClaim<Guid>> Claims { get; set; } = [];
    public List<IdentityUserLogin<Guid>> Logins { get; set; } = [];
    public List<IdentityUserToken<Guid>> Tokens { get; set; } = [];
}
