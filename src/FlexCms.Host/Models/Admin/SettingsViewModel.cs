using System.ComponentModel.DataAnnotations;

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

    public List<TimeZoneOption> AvailableTimeZones { get; set; } = [];
    public string SampleFormatted { get; set; } = "";
}

public class TimeZoneOption
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
