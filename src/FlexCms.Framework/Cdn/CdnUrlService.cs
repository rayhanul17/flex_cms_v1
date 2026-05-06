using FlexCms.Framework.Services;

namespace FlexCms.Framework.Cdn;

public sealed class CdnUrlService : ICdnUrlService
{
    public const string SettingsKey = "cdn:default";

    private readonly ISettingsService _settings;

    public CdnUrlService(ISettingsService settings) => _settings = settings;

    public async Task<string> ResolveAsync(string relativePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(relativePath)) return relativePath ?? "";
        // Already a full URL — return unchanged (avoids accidental double-prefixing).
        if (relativePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return relativePath;

        CdnSettings cfg;
        try { cfg = await _settings.GetAsync<CdnSettings>(SettingsKey, ct); }
        catch { return relativePath; }

        if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.BaseUrl)) return relativePath;

        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        var path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        return baseUrl + path;
    }
}
