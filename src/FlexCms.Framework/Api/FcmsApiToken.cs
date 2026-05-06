using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Api;

/// <summary>
/// Personal access token for the Bearer-auth handler. The plaintext token is
/// shown ONCE at creation; only the hash + a short prefix live at rest so
/// even a DB dump doesn't yield usable credentials.
///
/// <para>
/// Token format: <c>fcms_{base64url-32-bytes}</c>. The raw bytes are SHA-256'd
/// before storage. Lookup is by <see cref="Hash"/> directly — the
/// <see cref="Prefix"/> is only used in the admin UI to help users distinguish
/// their tokens.
/// </para>
/// </summary>
public class FcmsApiToken : BaseEfEntity
{
    public Guid UserId { get; set; }

    /// <summary>Friendly name set by the user — e.g. "iPhone App", "CI bot".</summary>
    public string Name { get; set; } = "";

    /// <summary>SHA-256 hex hash of the raw token bytes. Constant 64 chars.</summary>
    public string Hash { get; set; } = "";

    /// <summary>First 8 chars of the raw token (after the <c>fcms_</c> prefix). Display only.</summary>
    public string Prefix { get; set; } = "";

    /// <summary>Comma-separated permission scopes. <c>*</c> grants everything the user has.</summary>
    public string Scopes { get; set; } = "";

    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}
