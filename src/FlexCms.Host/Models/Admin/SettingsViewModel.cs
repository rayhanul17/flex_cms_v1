using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FlexCms.Host.Models.Admin;

public class SettingsViewModel
{
    [Display(Name = "Site Name")]
    public string SiteName { get; set; } = "";

    [Display(Name = "Site Tagline")]
    public string SiteTagline { get; set; } = "";

    [Display(Name = "Site Base URL")]
    public string SiteBaseUrl { get; set; } = "";

    [Display(Name = "Default Language")]
    public string DefaultLanguage { get; set; } = "en";

    [Display(Name = "Language mode")]
    public string LanguageMode { get; set; } = "cookie";

    [Display(Name = "Time Zone")]
    public string TimeZoneId { get; set; } = "";

    [Display(Name = "Date/time display format")]
    public string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm";

    [Display(Name = "Trash retention days")]
    [Range(1, 365)]
    public int TrashRetentionDays { get; set; } = 30;

    [Display(Name = "Enable audit logging")]
    public bool AuditEnabled { get; set; } = true;

    // Display-only — populated server-side, never posted by the form.
    // Without [ValidateNever] the model binder treats non-nullable refs as
    // implicitly required (.NET 6+) and silently fails ModelState validation
    // when the form post omits them, causing the Save action to short-circuit
    // back to the View with no visible error.
    [ValidateNever] public List<TimeZoneOption> AvailableTimeZones { get; set; } = [];
    [ValidateNever] public string SampleFormatted { get; set; } = "";

    // ── Themes (Phase 11) ────────────────────────────────────────────────────
    [Display(Name = "Public theme")]
    public string PublicThemeId { get; set; } = "FlexCms.Default";

    [ValidateNever] public List<ThemeOption> AvailableThemes { get; set; } = [];
}

public class ThemeOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsBuiltIn { get; set; }
}

public class TimeZoneOption
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
