namespace FlexCms.Framework.Captcha;

public sealed record CaptchaSettings
{
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "";
    public string SiteKey { get; init; } = "";
    public string SecretKey { get; init; } = "";

    /// <summary>
    /// After this many failures from one IP, login forms include the captcha
    /// even if it's globally disabled. Drives the adaptive-captcha feature.
    /// </summary>
    public int AdaptiveLoginThreshold { get; init; } = 3;
}

/// <summary>
/// Thin abstraction over Cloudflare Turnstile / hCaptcha / reCAPTCHA. Each
/// provider implementation just needs to call the verification API for its
/// service and return a normalized <see cref="CaptchaResult"/>. The framework
/// keeps the public site-key in plain settings; the secret-key encryption is
/// handled by the wrapping settings service (same pattern as SMTP/SMS).
/// </summary>
public interface IFcmsCaptchaProvider
{
    string ProviderId { get; }

    /// <summary>Verify <paramref name="response"/> (the token the client widget produces) against the provider.</summary>
    Task<CaptchaResult> VerifyAsync(string response, string? remoteIp, CaptchaSettings settings, CancellationToken ct = default);
}

public sealed record CaptchaResult(bool Success, string? Error = null, double? Score = null)
{
    public static CaptchaResult Ok(double? score = null) => new(true, null, score);
    public static CaptchaResult Fail(string error) => new(false, error);
}

public static class CaptchaProviders
{
    public const string Turnstile = "turnstile";
    public const string Hcaptcha = "hcaptcha";
    public const string Recaptcha = "recaptcha";

    public static IReadOnlyList<string> All { get; } = [Turnstile, Hcaptcha, Recaptcha];
}
