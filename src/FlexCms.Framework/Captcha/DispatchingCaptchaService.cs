using FlexCms.Framework.Services;

namespace FlexCms.Framework.Captcha;

/// <summary>
/// Resolves the active provider per-call by reading
/// <c>CaptchaSettings.Provider</c> from <see cref="ISettingsService"/>.
/// Wraps <see cref="IFcmsCaptchaProvider.VerifyAsync"/> so callers (login
/// form, registration, comments) don't have to know which gateway is
/// configured.
/// </summary>
public interface ICaptchaService
{
    /// <summary>True if captcha is enabled in settings.</summary>
    Task<bool> IsEnabledAsync(CancellationToken ct = default);

    Task<CaptchaResult> VerifyAsync(string responseToken, string? remoteIp, CancellationToken ct = default);
}

public sealed class DispatchingCaptchaService : ICaptchaService
{
    public const string SettingsKey = "captcha:default";

    private readonly ISettingsService _settings;
    private readonly Dictionary<string, IFcmsCaptchaProvider> _providers;

    public DispatchingCaptchaService(ISettingsService settings, IEnumerable<IFcmsCaptchaProvider> providers)
    {
        _settings = settings;
        _providers = providers.ToDictionary(p => p.ProviderId, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        try { return (await GetSettingsAsync(ct)).Enabled; }
        catch { return false; }
    }

    public async Task<CaptchaResult> VerifyAsync(string responseToken, string? remoteIp, CancellationToken ct = default)
    {
        CaptchaSettings cfg;
        try { cfg = await GetSettingsAsync(ct); }
        catch (Exception ex) { return CaptchaResult.Fail($"Captcha settings unavailable: {ex.Message}"); }

        if (!cfg.Enabled) return CaptchaResult.Fail("Captcha not enabled.");
        if (!_providers.TryGetValue(cfg.Provider, out var provider))
            return CaptchaResult.Fail($"Unknown captcha provider '{cfg.Provider}'.");
        return await provider.VerifyAsync(responseToken, remoteIp, cfg, ct);
    }

    private async Task<CaptchaSettings> GetSettingsAsync(CancellationToken ct)
    {
        var dto = await _settings.GetAsync<CaptchaSettingsDto>(SettingsKey, ct);
        return new CaptchaSettings
        {
            Enabled = dto.Enabled,
            Provider = string.IsNullOrWhiteSpace(dto.Provider) ? CaptchaProviders.Turnstile : dto.Provider,
            SiteKey = dto.SiteKey ?? "",
            SecretKey = dto.SecretKey ?? "",
            AdaptiveLoginThreshold = dto.AdaptiveLoginThreshold == 0 ? 3 : dto.AdaptiveLoginThreshold
        };
    }

    /// <summary>Settings DTO matches <see cref="CaptchaSettings"/> fields but is stored as <c>class</c> so EF can persist it.</summary>
    public sealed class CaptchaSettingsDto
    {
        public bool Enabled { get; set; }
        public string Provider { get; set; } = "";
        public string SiteKey { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public int AdaptiveLoginThreshold { get; set; } = 3;
    }
}
