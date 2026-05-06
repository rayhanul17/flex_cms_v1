namespace FlexCms.Core.Models.Settings;

public class SiteSettings
{
    // ── Site Identity ──────────────────────────────────────────────────────
    public string SiteName { get; set; } = "My FlexCms Site";
    public string Tagline { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string DefaultLanguage { get; set; } = "en";
    public string TimeZone { get; set; } = "Asia/Dhaka";

    /// <summary>
    /// .NET <see cref="DateTime.ToString(string)"/> format string used everywhere
    /// dates are displayed in the admin UI. Default: <c>yyyy-MM-dd HH:mm</c>.
    /// Examples: <c>dd MMM yyyy hh:mm tt</c>, <c>yyyy/MM/dd HH:mm:ss</c>.
    /// </summary>
    public string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm";
    public string? MetaDescription { get; set; }
    public string? GoogleAnalyticsId { get; set; }

    // ── Branding ───────────────────────────────────────────────────────────
    public Guid? LogoMediaId { get; set; }
    public Guid? FaviconMediaId { get; set; }

    // ── Homepage & Error Pages ─────────────────────────────────────────────
    public Guid? HomepageId { get; set; }
    public Guid? Custom404PageId { get; set; }
    public Guid? Custom401PageId { get; set; }
    public Guid? Custom403PageId { get; set; }
    public Guid? Custom500PageId { get; set; }

    // ── Media ──────────────────────────────────────────────────────────────
    public int MaxUploadSizeMb { get; set; } = 10;
    public string AllowedExtensions { get; set; } =
        ".jpg,.jpeg,.png,.gif,.webp,.pdf,.doc,.docx,.xls,.xlsx,.zip";

    // ── Content ────────────────────────────────────────────────────────────
    public int PostsPerPage { get; set; } = 10;
    public bool EnableScheduledPublish { get; set; } = true;
    public int TrashRetentionDays { get; set; } = 30;
    public bool EnableSearch { get; set; } = true;
    public bool EnableRssFeed { get; set; } = true;

    // ── Security ───────────────────────────────────────────────────────────
    public int SessionTimeoutMinutes { get; set; } = 480;
    public bool EnableHoneypot { get; set; } = true;
    public string AdminAllowedIps { get; set; } = "";
    public string BlockedIps { get; set; } = "";

    // ── Password Policy ────────────────────────────────────────────────────
    public int PasswordMinLength { get; set; } = 8;
    public bool PasswordRequireDigit { get; set; } = true;
    public bool PasswordRequireUppercase { get; set; } = false;
    public bool PasswordRequireSpecialChar { get; set; } = false;

    // ── Language ───────────────────────────────────────────────────────────
    public string LanguageMode { get; set; } = "cookie";

    // ── Login / Auth ───────────────────────────────────────────────────────
    public bool RequireEmailVerification { get; set; } = true;
    public string RequireTwoFactorForRolesJson { get; set; } = "[]";
    public string DefaultRoleLandingPagesJson { get; set; } =
        """{"SuperAdmin":"/admin","Admin":"/admin","Editor":"/admin/cms/posts","Author":"/admin/cms/posts/mine","Subscriber":"/profile"}""";
    public string FallbackLandingPage { get; set; } = "/";

    // ── SEO ────────────────────────────────────────────────────────────────
    public string RobotsTxtContent { get; set; } =
        "User-agent: *\nAllow: /\nDisallow: /admin/\nDisallow: /auth/\nSitemap: {sitemap_url}";
    public bool RobotsBlockAll { get; set; } = false;

    // ── Maintenance ────────────────────────────────────────────────────────
    public bool MaintenanceModeEnabled { get; set; } = false;
    public string MaintenanceMessage { get; set; } = "We're updating the site. Back shortly.";
    public string MaintenanceAllowedRoles { get; set; } = "SuperAdmin,Admin";
    public string MaintenanceBypassToken { get; set; } = "";

    // ── Retention ─────────────────────────────────────────────────────────
    public int LogRetentionDays { get; set; } = 30;
    public int ExportRetentionDays { get; set; } = 7;
    public int AuditRetentionDays { get; set; } = 90;

    // ── Hotlink Protection ─────────────────────────────────────────────────
    public bool PreventHotlinking { get; set; } = false;
    public string HotlinkWhitelist { get; set; } = "";

    // ── UI / UX ────────────────────────────────────────────────────────────
    public string PublicThemeId { get; set; } = "FlexCms.Default";
    public int NotificationFallbackPollSeconds { get; set; } = 60;
    public string AdminSearchHotkey { get; set; } = "k";

    // ── Terms ──────────────────────────────────────────────────────────────
    public string CurrentTermsVersion { get; set; } = "2026-01-01";
}
