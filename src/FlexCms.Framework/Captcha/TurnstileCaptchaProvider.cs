using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Captcha;

/// <summary>
/// Cloudflare Turnstile — POST <c>token</c> + <c>secret</c> + <c>remoteip</c>
/// to <c>siteverify</c>; response carries <c>success: true|false</c>.
/// Other providers (hCaptcha, reCAPTCHA) follow the same shape; this impl is
/// the default since Turnstile is free and privacy-focused.
/// </summary>
public sealed class TurnstileCaptchaProvider : IFcmsCaptchaProvider
{
    public const string DefaultEndpoint = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    public string ProviderId => CaptchaProviders.Turnstile;

    private readonly HttpClient _http;
    private readonly ILogger<TurnstileCaptchaProvider> _logger;

    public TurnstileCaptchaProvider(HttpClient http, ILogger<TurnstileCaptchaProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<CaptchaResult> VerifyAsync(string response, string? remoteIp, CaptchaSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(response)) return CaptchaResult.Fail("Empty captcha token.");
        if (string.IsNullOrWhiteSpace(settings.SecretKey)) return CaptchaResult.Fail("Captcha secret not configured.");

        try
        {
            var payload = new Dictionary<string, string>
            {
                ["secret"] = settings.SecretKey,
                ["response"] = response
            };
            if (!string.IsNullOrEmpty(remoteIp)) payload["remoteip"] = remoteIp;

            using var content = new FormUrlEncodedContent(payload);
            using var resp = await _http.PostAsync(DefaultEndpoint, content, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return CaptchaResult.Fail($"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean()
                ? CaptchaResult.Ok()
                : CaptchaResult.Fail("Captcha verification failed.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Turnstile verify failed");
            return CaptchaResult.Fail(ex.Message);
        }
    }
}
