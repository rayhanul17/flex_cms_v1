using FlexCms.Core.Models.Settings;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Services;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/settings")]
public class SettingsController : BaseAdminController
{
    private const string SiteSettingsKey = "site:general";

    private readonly ISettingsService _settings;
    private readonly IMediaService _media;

    public SettingsController(ISettingsService settings, IMediaService media)
    {
        _settings = settings;
        _media = media;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.SettingsView)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var site = await _settings.GetAsync<SiteSettings>(SiteSettingsKey, ct: ct);
        var audit = await _settings.GetAsync<AuditEnabledDto>(AuditLogSettings.Key, ct: ct);
        var theme = await _settings.GetAsync<ThemeSettings>(ThemeSettings.Key, ct: ct);
        return View(await BuildVmAsync(site, audit.Enabled, theme, ct));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    [FcmsLog("settings.save", "SiteSettings")]
    public async Task<IActionResult> Index(SettingsViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(PopulateAvailable(vm));

        var site = await _settings.GetAsync<SiteSettings>(SiteSettingsKey, ct: ct);

        site.SiteName = vm.SiteName?.Trim() ?? "";
        site.Tagline = vm.SiteTagline?.Trim() ?? "";
        site.BaseUrl = vm.SiteBaseUrl?.Trim() ?? "";
        site.LogoMediaId = vm.LogoMediaId;
        site.FaviconMediaId = vm.FaviconMediaId;
        site.DefaultLanguage = vm.DefaultLanguage ?? "en";
        site.LanguageMode = (vm.LanguageMode ?? "cookie").ToLowerInvariant();
        site.TimeZone = vm.TimeZoneId ?? site.TimeZone;
        site.DateTimeFormat = string.IsNullOrWhiteSpace(vm.DateTimeFormat) ? "yyyy-MM-dd HH:mm" : vm.DateTimeFormat.Trim();
        site.TrashRetentionDays = vm.TrashRetentionDays;

        await _settings.SaveAsync(SiteSettingsKey, site, ct);
        await _settings.SaveAsync(AuditLogSettings.Key, new AuditEnabledDto { Enabled = vm.AuditEnabled }, ct);
        await _settings.SaveAsync(ThemeSettings.Key, vm.Theme, ct);

        FcmsLogContext.SetValue(HttpContext, site);
        ShowSuccess("Settings saved.");
        return RedirectToAction(nameof(Index));
    }


    [HttpPost("audit/toggle")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    public async Task<IActionResult> ToggleAudit(CancellationToken ct)
    {
        var cfg = await _settings.GetAsync<AuditEnabledDto>(AuditLogSettings.Key, ct: ct);
        cfg.Enabled = !cfg.Enabled;
        await _settings.SaveAsync(AuditLogSettings.Key, cfg, ct);
        return FcmsOk(cfg.Enabled ? "Audit logging enabled." : "Audit logging disabled.", new { enabled = cfg.Enabled });
    }


    private async Task<SettingsViewModel> BuildVmAsync(SiteSettings site, bool auditEnabled, ThemeSettings theme, CancellationToken ct)
    {
        var vm = new SettingsViewModel
        {
            SiteName = site.SiteName,
            SiteTagline = site.Tagline,
            SiteBaseUrl = site.BaseUrl,
            LogoMediaId = site.LogoMediaId,
            FaviconMediaId = site.FaviconMediaId,
            DefaultLanguage = site.DefaultLanguage,
            LanguageMode = site.LanguageMode,
            TimeZoneId = site.TimeZone,
            DateTimeFormat = site.DateTimeFormat,
            TrashRetentionDays = site.TrashRetentionDays,
            AuditEnabled = auditEnabled,
            Theme = theme,
        };
        if (site.LogoMediaId.HasValue)
            vm.LogoUrl = (await _media.GetByIdAsync(site.LogoMediaId.Value, ct))?.Url;
        if (site.FaviconMediaId.HasValue)
            vm.FaviconUrl = (await _media.GetByIdAsync(site.FaviconMediaId.Value, ct))?.Url;
        PopulateAvailable(vm);
        try { vm.SampleFormatted = FcmsTime.Format(FcmsTime.Now, vm.DateTimeFormat); }
        catch { vm.SampleFormatted = "(invalid format)"; }
        return vm;
    }

    private SettingsViewModel PopulateAvailable(SettingsViewModel vm)
    {
        vm.AvailableTimeZones = TimeZoneInfo.GetSystemTimeZones()
            .Select(tz => new TimeZoneOption { Id = tz.Id, DisplayName = tz.DisplayName })
            .OrderBy(t => t.DisplayName)
            .ToList();
        try { vm.SampleFormatted = FcmsTime.Format(FcmsTime.Now, vm.DateTimeFormat); }
        catch { vm.SampleFormatted = "(invalid format)"; }
        return vm;
    }

    private sealed class AuditEnabledDto
    {
        public bool Enabled { get; set; } = true;
    }
}
