using System.ComponentModel.DataAnnotations;
using FlexCms.Framework.Messaging;

namespace FlexCms.Host.Models.Admin;

public class MessagingSettingsViewModel
{
    // ── SMTP ─────────────────────────────────────────────────────────────────
    [Display(Name = "Enable SMTP email")]
    public bool SmtpEnabled { get; set; }

    [Display(Name = "SMTP host")]
    public string SmtpHost { get; set; } = "";

    [Display(Name = "SMTP port")]
    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    [Display(Name = "Use SSL/TLS")]
    public bool SmtpUseSsl { get; set; } = true;

    [Display(Name = "SMTP username")]
    public string SmtpUsername { get; set; } = "";

    /// <summary>
    /// Empty means "keep existing"; non-empty rotates the encrypted password.
    /// Plaintext is never round-tripped to the form.
    /// </summary>
    [Display(Name = "SMTP password (leave blank to keep existing)")]
    [DataType(DataType.Password)]
    public string? SmtpPassword { get; set; }

    [Display(Name = "From address")]
    [EmailAddress]
    public string SmtpFromAddress { get; set; } = "";

    [Display(Name = "From name")]
    public string SmtpFromName { get; set; } = "";

    public bool SmtpHasPassword { get; set; }

    // ── SMS ──────────────────────────────────────────────────────────────────
    [Display(Name = "Enable SMS")]
    public bool SmsEnabled { get; set; }

    [Display(Name = "Gateway")]
    public string SmsGateway { get; set; } = SmsGateways.Alpha;

    [Display(Name = "Sender ID / mask")]
    public string SmsSenderId { get; set; } = "";

    [Display(Name = "Username (Onnorokom only)")]
    public string SmsUsername { get; set; } = "";

    [Display(Name = "API key (leave blank to keep existing)")]
    [DataType(DataType.Password)]
    public string? SmsApiKey { get; set; }

    [Display(Name = "Endpoint override (advanced)")]
    public string SmsEndpointOverride { get; set; } = "";

    public bool SmsHasApiKey { get; set; }
}
