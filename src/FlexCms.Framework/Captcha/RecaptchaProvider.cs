using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Captcha;

/// <summary>
/// Google reCAPTCHA verification (v2 + v3 share the same endpoint). v3 also
/// returns a <c>score</c> field 0.0–1.0; we surface it in
/// <see cref="CaptchaResult.Score"/> so callers can apply a stricter
/// threshold for sensitive forms (e.g. require &gt;0.7 for password reset).
/// </summary>
public sealed class RecaptchaProvider : IFcmsCaptchaProvider
{
    public const string DefaultEndpoint = "https://www.google.com/recaptcha/api/siteverify";
    public string ProviderId => CaptchaProviders.Recaptcha;

    private readonly HttpClient _http;
    private readonly ILogger<RecaptchaProvider> _logger;

    public RecaptchaProvider(HttpClient http, ILogger<RecaptchaProvider> logger)
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
            var ok = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
            double? score = null;
            if (doc.RootElement.TryGetProperty("score", out var scoreEl)
                && scoreEl.TryGetDouble(out var d))
                score = d;

            return ok
                ? CaptchaResult.Ok(score)
                : CaptchaResult.Fail("Captcha verification failed.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "reCAPTCHA verify failed");
            return CaptchaResult.Fail(ex.Message);
        }
    }
}
