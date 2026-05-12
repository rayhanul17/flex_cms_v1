using FlexCms.Core.Models.Settings;
using FlexCms.Framework.Services;
using FlexCms.Framework.Setup;

namespace FlexCms.Host.Hosting;

/// <summary>
/// Bootstrap <see cref="SiteSettings"/> from <c>setup.json</c> on first
/// production-mode boot so the Settings page doesn't show defaults for
/// values the admin already provided through the Setup Wizard
/// (site name, tagline, base URL, default language, timezone).
///
/// Idempotent: only writes when the persisted SiteSettings is still at
/// the default state. After the admin edits and saves the Settings page,
/// this bootstrap won't overwrite.
///
/// Lives in the Host project (not Framework) because Framework cannot
/// reference Core (where SiteSettings lives) without a circular dep.
/// </summary>
public class SiteSettingsBootstrapService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SetupHelper _setupHelper;
    private readonly ILogger<SiteSettingsBootstrapService> _logger;

    public SiteSettingsBootstrapService(
        IServiceScopeFactory scopeFactory,
        SetupHelper setupHelper,
        ILogger<SiteSettingsBootstrapService> logger)
    {
        _scopeFactory = scopeFactory;
        _setupHelper = setupHelper;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var setup = _setupHelper.Read();
        if (setup is null || !setup.IsSetupComplete) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetService<ISettingsService>();
        if (settings is null) return;

        const string siteKey = "site:general";
        SiteSettings current;
        try { current = await settings.GetAsync<SiteSettings>(siteKey, ct: ct); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SiteSettings bootstrap: failed to read existing SiteSettings — skipping.");
            return;
        }

        // Bail if admin has already customised — we only fill the gap on first boot.
        // Defaults are "My FlexCms Site" / "FlexCMS" depending on which constructor ran.
        var isDefault = string.IsNullOrWhiteSpace(current.SiteName)
            || string.Equals(current.SiteName, "My FlexCms Site", StringComparison.Ordinal)
            || string.Equals(current.SiteName, "FlexCMS", StringComparison.Ordinal);
        if (!isDefault) return;

        if (!string.IsNullOrWhiteSpace(setup.SiteName)) current.SiteName = setup.SiteName;
        if (!string.IsNullOrWhiteSpace(setup.SiteTagline)) current.Tagline = setup.SiteTagline;
        if (!string.IsNullOrWhiteSpace(setup.SiteBaseUrl)) current.BaseUrl = setup.SiteBaseUrl;
        if (!string.IsNullOrWhiteSpace(setup.DefaultLanguage)) current.DefaultLanguage = setup.DefaultLanguage;
        if (!string.IsNullOrWhiteSpace(setup.TimeZoneId)) current.TimeZone = setup.TimeZoneId;

        try
        {
            await settings.SaveAsync(siteKey, current, ct);
            _logger.LogInformation(
                "SiteSettings bootstrap: applied setup.json values (site={Site}, tz={Tz}).",
                current.SiteName, current.TimeZone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SiteSettings bootstrap: failed to save SiteSettings.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
