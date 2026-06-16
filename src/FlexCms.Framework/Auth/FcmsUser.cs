using System.Security.Claims;
using FlexCms.Framework.Db;
using Microsoft.AspNetCore.Identity;

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

    /// <summary>
    /// Optional avatar / profile photo URL — points at an entry in
    /// <c>fcms_medias</c> uploaded via the media picker. When null the admin
    /// sidebar + comment author block fall back to initials of
    /// <see cref="ResolvedDisplayName"/>.
    /// </summary>
    public string? ImageUrl { get; set; }

    public bool ForcePasswordChange { get; set; }
    public DateTime CreatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;
    public DateTime UpdatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;

    /// <summary>
    /// Admin-initiated block end-time. Distinct from Identity's
    /// <c>LockoutEnd</c> (which the framework uses for failed-login
    /// auto-lockout): <see cref="BlockedUntil"/> is set by a moderator
    /// clicking "Block" in the user admin and survives a manual
    /// LockoutEnd reset, so a re-enabled admin lockout doesn't accidentally
    /// re-permit a banned account. Null = not blocked.
    /// </summary>
    public DateTime? BlockedUntil { get; set; }

    /// <summary>
    /// Free-text reason captured when an admin blocks a user. Surfaced on
    /// the user detail page so the next moderator knows why.
    /// </summary>
    public string? BlockReason { get; set; }

    /// <summary>
    /// Selected 2FA channel — only honored when Identity's
    /// <see cref="Microsoft.AspNetCore.Identity.IdentityUser.TwoFactorEnabled"/>
    /// is also true. Lets users pick email vs SMS without changing the
    /// Identity-managed boolean toggle.
    /// </summary>
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
    /// </summary>
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
