using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Captcha;

/// <summary>
/// hCaptcha verification — same shape as <see cref="TurnstileCaptchaProvider"/>.
/// POST <c>secret</c> + <c>response</c> + optional <c>remoteip</c> to
/// <c>siteverify</c>; response carries <c>success: true|false</c>.
/// </summary>
public sealed class HcaptchaProvider : IFcmsCaptchaProvider
{
    public const string DefaultEndpoint = "https://hcaptcha.com/siteverify";
    public string ProviderId => CaptchaProviders.Hcaptcha;

    private readonly HttpClient _http;
    private readonly ILogger<HcaptchaProvider> _logger;

    public HcaptchaProvider(HttpClient http, ILogger<HcaptchaProvider> logger)
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
            _logger.LogWarning(ex, "hCaptcha verify failed");
            return CaptchaResult.Fail(ex.Message);
        }
    }
}
