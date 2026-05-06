using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Auth.History;

public enum LoginOutcome
{
    Success = 0,
    InvalidCredentials = 1,
    LockedOut = 2,
    NotAllowed = 3,
    EmailUnverified = 4,
    Other = 5
}

/// <summary>
/// Append-only login attempt log. Drives the admin Security Dashboard
/// (recent failures, suspicious-IP report, account-lockout overview).
/// Successes are logged too so the same dashboard can answer "who logged in
/// from where lately".
/// </summary>
public class FcmsLoginHistory : BaseEfEntity
{
    /// <summary>Username/email the caller TRIED. Stored even on failure so admins can trace targeted attacks.</summary>
    public string AttemptedUserName { get; set; } = "";

    /// <summary>Resolved on success only — null for failures (the user might not exist).</summary>
    public Guid? UserId { get; set; }

    public LoginOutcome Outcome { get; set; }

    public string IpAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";

    /// <summary>Free-form failure detail — lockout time-remaining, etc.</summary>
    public string? FailReason { get; set; }
}
