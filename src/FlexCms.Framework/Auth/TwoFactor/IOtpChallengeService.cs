namespace FlexCms.Framework.Auth.TwoFactor;

/// <summary>
/// Issues + verifies email/SMS one-time passwords for the 2FA flow.
/// Channel is the user's <see cref="FcmsUser.TwoFactorChannel"/>; the
/// service picks the matching transport (Phase 8 email or SMS sender).
///
/// <para>
/// Codes are 6 digits, 5-minute expiry. Hash stored on the user (BCrypt);
/// per-OTP attempt counter caps brute-force at 5 tries. Resending replaces
/// the code (the older one becomes invalid).
/// </para>
/// </summary>
public interface IOtpChallengeService
{
    /// <summary>Generate + persist + send a fresh OTP. Returns the masked destination ("u***@example.com" / "01****1234") for the verify form.</summary>
    Task<OtpIssueResult> IssueAsync(FcmsUser user, CancellationToken ct = default);

    /// <summary>Constant-time check of <paramref name="code"/> against the user's pending OTP. Increments attempts on miss; clears the pending OTP on success.</summary>
    Task<OtpVerifyResult> VerifyAsync(FcmsUser user, string code, CancellationToken ct = default);

    /// <summary>Try a recovery code. On match: mark used + treat as a successful 2FA verification.</summary>
    Task<bool> VerifyRecoveryCodeAsync(FcmsUser user, string code, CancellationToken ct = default);

    /// <summary>Generate a fresh batch of 10 recovery codes (replaces existing). Returns the plaintext codes — caller MUST show once and never persist again.</summary>
    Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(FcmsUser user, int count = 10, CancellationToken ct = default);

    /// <summary>How many unused recovery codes remain — surfaces as a warning when low.</summary>
    Task<int> CountUnusedRecoveryCodesAsync(Guid userId, CancellationToken ct = default);
}

public sealed record OtpIssueResult(bool Success, string? MaskedDestination = null, string? Error = null);

public enum OtpVerifyResult
{
    Ok = 0,
    NoPending = 1,
    Expired = 2,
    Invalid = 3,
    TooManyAttempts = 4,
}
