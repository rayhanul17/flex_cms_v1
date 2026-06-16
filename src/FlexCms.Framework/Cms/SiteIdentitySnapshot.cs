namespace FlexCms.Framework.Cms;

/// <summary>
/// Persisted snapshot of the site's public identity (name, base URL, logo,
/// favicon). Stored under <c>"site:general"</c> in <c>fcms_settings</c> and
/// updated by the Settings admin page.
///
/// <para>
/// Promoted from per-view inline classes in <c>_AdminLayout.cshtml</c> /
/// <c>_Layout.cshtml</c> so framework components (tag helpers, services)
/// can read the same shape without duplicating the type definition.
/// </para>
/// </summary>
public sealed class SiteIdentitySnapshot
{
    public string SiteName { get; set; } = "";
    public string? Tagline { get; set; }
    public string? BaseUrl { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? FaviconMediaId { get; set; }
}
