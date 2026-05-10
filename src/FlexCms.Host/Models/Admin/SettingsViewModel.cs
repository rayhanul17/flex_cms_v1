using System.ComponentModel.DataAnnotations;
using FlexCms.Core.Models.Settings;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FlexCms.Host.Models.Admin;

public class SettingsViewModel
{
    [Display(Name = "Site Name")]
    public string SiteName { get; set; } = "";

    [Display(Name = "Site Tagline")]
    public string? SiteTagline { get; set; }

    [Display(Name = "Site Base URL")]
    public string? SiteBaseUrl { get; set; }

    [Display(Name = "Logo")]
    public Guid? LogoMediaId { get; set; }

    [Display(Name = "Favicon")]
    public Guid? FaviconMediaId { get; set; }

    // Resolved URLs for the picker preview — populated by SettingsController
    // when loading the form; ignored on POST.
    [ValidateNever] public string? LogoUrl { get; set; }
    [ValidateNever] public string? FaviconUrl { get; set; }

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

    // Theme color settings
    public ThemeSettings Theme { get; set; } = new();

    [ValidateNever] public List<TimeZoneOption> AvailableTimeZones { get; set; } = [];
    [ValidateNever] public string SampleFormatted { get; set; } = "";
}

public class TimeZoneOption
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
