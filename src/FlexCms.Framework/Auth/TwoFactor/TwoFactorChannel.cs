namespace FlexCms.Framework.Auth.TwoFactor;

/// <summary>
/// Where to send the 6-digit login code when 2FA is enabled. Email is the
/// default — works without a phone number, costs nothing per send, no
/// SIM-swap risk. SMS is a per-deployment opt-in (admin must enable the
/// SMS gateway in Phase 8 settings + accept the per-message cost).
///
/// <para>
/// Standalone authenticator apps (TOTP) are intentionally NOT a channel —
/// FlexCMS targets BD admins for whom installing Google Authenticator is
/// unfamiliar; reusing the email/SMS infra they already configured for
/// password reset matches their mental model.
/// </para>
/// </summary>
public enum TwoFactorChannel
{
    Disabled = 0,
    Email = 1,
    Sms = 2,
}
