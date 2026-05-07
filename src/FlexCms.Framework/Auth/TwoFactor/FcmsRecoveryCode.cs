using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Auth.TwoFactor;

/// <summary>
/// Single-use backup code shown ONCE during 2FA enrollment. Stored as a
/// SHA-256 hash so the DB leak doesn't surrender working codes; one row
/// per code so used codes can be marked individually without rewriting
/// the entire bundle.
///
/// <para>
/// 10 codes per user is the standard convention (matches GitHub / Google).
/// When the user runs out, the next time they sign in they're prompted
/// to regenerate.
/// </para>
/// </summary>
public class FcmsRecoveryCode : BaseEfEntity
{
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hex of the code. Constant-time compared on use.</summary>
    public string CodeHash { get; set; } = "";

    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
}
