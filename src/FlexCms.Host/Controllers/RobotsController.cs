using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

/// <summary>
/// Serves the contents of <c>SiteSettings.RobotsTxtContent</c> at
/// <c>/robots.txt</c>. The body supports a <c>{sitemap_url}</c> token that
/// gets substituted with the absolute URL of the site's sitemap.
///
/// <para>
/// If <c>SiteSettings.RobotsBlockAll</c> is true (e.g. staging environments),
/// the body is overridden to disallow everything regardless of the configured
/// content — guards against accidentally indexing pre-prod environments.
/// </para>
/// </summary>
[Route("robots.txt")]
public class RobotsController : Controller
{
    private readonly ISettingsService _settings;

    public RobotsController(ISettingsService settings) => _settings = settings;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var snap = await _settings.GetAsync<RobotsSnapshot>("site:general", ct);

        if (snap.RobotsBlockAll)
            return Content("User-agent: *\nDisallow: /\n", "text/plain");

        var body = string.IsNullOrWhiteSpace(snap.RobotsTxtContent)
            ? "User-agent: *\nAllow: /\n"
            : snap.RobotsTxtContent;

        // {sitemap_url} → fully-qualified sitemap path. Lets the same content
        // work in dev (localhost), staging, and prod without per-env edits.
        var sitemapUrl = $"{Request.Scheme}://{Request.Host}/sitemap.xml";
        body = body.Replace("{sitemap_url}", sitemapUrl, StringComparison.OrdinalIgnoreCase);

        return Content(body, "text/plain");
    }

    private sealed class RobotsSnapshot
    {
        public string RobotsTxtContent { get; set; } = "";
        public bool RobotsBlockAll { get; set; }
    }
}
